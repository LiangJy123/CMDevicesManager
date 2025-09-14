using CMDevicesManager.Helper;
using CMDevicesManager.Services;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Service for handling real-time JPEG data transmission to HID devices.
    /// Uses a queue system to temporarily store data and automatically manages
    /// HID device real-time mode based on queue status.
    /// </summary>
    public class RealtimeJpegTransmissionService : IDisposable
    {
        private readonly ConcurrentQueue<JpegTransmissionItem> _jpegDataQueue;
        private readonly HidDeviceService _hidDeviceService;
        private readonly System.Threading.Timer _processingTimer;
        private readonly System.Threading.Timer _queueMonitorTimer;
        private readonly object _lockObject = new object();
        private readonly object _queueOperationLock = new object(); // Additional lock for queue operations
        private readonly object _timestampLock = new object(); // Lock for timestamp operations
        
        private bool _disposed = false;
        private bool _isProcessing = false;
        private bool _isRealTimeEnabled = false;
        private byte _currentTransferId = 1;
        private DateTime _lastQueueActivity = DateTime.Now; // Protected by _timestampLock
        private DateTime _lastRealTimeModeCheck = DateTime.Now;
        
        // Configuration settings
        private readonly int _processingIntervalMs;
        private readonly int _maxQueueSize;
        private readonly int _maxRetryAttempts;
        private readonly int _transferIdRange = 59; // Transfer IDs range from 1-59
        private readonly int _queueMonitorIntervalMs = 500; // Monitor queue every 500ms
        private readonly int _realTimeModeTimeoutMs = 10000; // Disable real-time mode after 10s of no activity
        
        // Statistics
        private long _totalFramesQueued = 0;
        private long _totalFramesSent = 0;
        private long _totalFramesDropped = 0;
        private long _totalRetries = 0;
        private long _realTimeModeEnableCount = 0;
        private long _realTimeModeDisableCount = 0;
        
        // Events
        public event EventHandler<JpegDataQueuedEventArgs>? JpegDataQueued;
        public event EventHandler<JpegDataSentEventArgs>? JpegDataSent;
        public event EventHandler<JpegDataDroppedEventArgs>? JpegDataDropped;
        public event EventHandler<TransmissionErrorEventArgs>? TransmissionError;
        public event EventHandler<QueueStatusChangedEventArgs>? QueueStatusChanged;
        public event EventHandler<RealTimeModeChangedEventArgs>? RealTimeModeChanged;
        public event EventHandler<QueueMonitorEventArgs>? QueueMonitorUpdate;
        
        /// <summary>
        /// Gets whether the service is currently processing the queue
        /// </summary>
        public bool IsProcessing => _isProcessing;
        
        /// <summary>
        /// Gets whether real-time mode is currently enabled on devices
        /// </summary>
        public bool IsRealTimeModeEnabled => _isRealTimeEnabled;
        
        /// <summary>
        /// Gets the current queue size
        /// </summary>
        public int QueueSize => _jpegDataQueue.Count;
        
        /// <summary>
        /// Gets the maximum allowed queue size
        /// </summary>
        public int MaxQueueSize => _maxQueueSize;
        
        /// <summary>
        /// Gets the processing interval in milliseconds
        /// </summary>
        public int ProcessingIntervalMs => _processingIntervalMs;
        
        /// <summary>
        /// Gets whether the queue has data waiting to be processed
        /// </summary>
        public bool HasQueuedData => !_jpegDataQueue.IsEmpty;
        
        /// <summary>
        /// Gets the time since last queue activity (thread-safe)
        /// </summary>
        public TimeSpan TimeSinceLastActivity
        {
            get
            {
                lock (_timestampLock)
                {
                    return DateTime.Now - _lastQueueActivity;
                }
            }
        }
        
        /// <summary>
        /// Gets enhanced transmission statistics
        /// </summary>
        public EnhancedTransmissionStatistics Statistics => new EnhancedTransmissionStatistics
        {
            TotalFramesQueued = _totalFramesQueued,
            TotalFramesSent = _totalFramesSent,
            TotalFramesDropped = _totalFramesDropped,
            TotalRetries = _totalRetries,
            CurrentQueueSize = QueueSize,
            IsRealTimeModeEnabled = _isRealTimeEnabled,
            RealTimeModeEnableCount = _realTimeModeEnableCount,
            RealTimeModeDisableCount = _realTimeModeDisableCount,
            TimeSinceLastActivity = TimeSinceLastActivity,
            IsProcessingActive = _isProcessing
        };
        
        /// <summary>
        /// Initialize the real-time JPEG transmission service
        /// </summary>
        /// <param name="hidDeviceService">HID device service instance</param>
        /// <param name="processingIntervalMs">Processing interval in milliseconds (default: 33ms ~30FPS)</param>
        /// <param name="maxQueueSize">Maximum queue size before dropping frames (default: 10)</param>
        /// <param name="maxRetryAttempts">Maximum retry attempts for failed transmissions (default: 3)</param>
        public RealtimeJpegTransmissionService(
            HidDeviceService hidDeviceService,
            int processingIntervalMs = 33,
            int maxQueueSize = 10,
            int maxRetryAttempts = 3)
        {
            _hidDeviceService = hidDeviceService ?? throw new ArgumentNullException(nameof(hidDeviceService));
            _processingIntervalMs = Math.Max(1, processingIntervalMs);
            _maxQueueSize = Math.Max(1, maxQueueSize);
            _maxRetryAttempts = Math.Max(1, maxRetryAttempts);
            
            _jpegDataQueue = new ConcurrentQueue<JpegTransmissionItem>();
            
            // Create timers but don't start them yet
            _processingTimer = new System.Threading.Timer(ProcessQueueCallback, null, Timeout.Infinite, Timeout.Infinite);
            _queueMonitorTimer = new System.Threading.Timer(QueueMonitorCallback, null, _queueMonitorIntervalMs, _queueMonitorIntervalMs);
            
            Logger.Info($"RealtimeJpegTransmissionService initialized: " +
                       $"ProcessingInterval={_processingIntervalMs}ms, " +
                       $"MaxQueueSize={_maxQueueSize}, " +
                       $"MaxRetryAttempts={_maxRetryAttempts}, " +
                       $"QueueMonitorInterval={_queueMonitorIntervalMs}ms");
        }
        
        /// <summary>
        /// Queue JPEG data for transmission to HID devices with thread-safe locking
        /// </summary>
        /// <param name="jpegData">JPEG data bytes (will be safely copied to prevent external modifications)</param>
        /// <param name="priority">Priority level (higher values = higher priority)</param>
        /// <param name="metadata">Optional metadata for the transmission</param>
        /// <returns>True if data was queued successfully, false if queue is full or service is disposed</returns>
        public bool QueueJpegData(byte[] jpegData, int priority = 0, string? metadata = null)
        {
            if (_disposed)
            {
                Logger.Warn("Cannot queue JPEG data: service is disposed");
                return false;
            }
            
            if (jpegData == null || jpegData.Length == 0)
            {
                Logger.Warn("Cannot queue empty JPEG data");
                return false;
            }

            // Use lock to ensure thread-safe access to jpegData and queue operations
            lock (_queueOperationLock)
            {
                try
                {
                    // Double-check disposal state inside lock
                    if (_disposed)
                    {
                        Logger.Warn("Cannot queue JPEG data: service was disposed during lock acquisition");
                        return false;
                    }

                    //// Create a defensive copy of jpegData to prevent external modifications
                    //byte[] jpegDataCopy;
                    //lock (jpegData) // Lock the jpegData parameter to prevent concurrent modifications
                    //{
                    //    jpegDataCopy = new byte[jpegData.Length];
                    //    Buffer.BlockCopy(jpegData, 0, jpegDataCopy, 0, jpegData.Length);
                    //}
                    //var jpegDataCopy = jpegData;

                    // Update activity timestamp (thread-safe)
                    var currentTime = DateTime.Now;
                    lock (_timestampLock)
                    {
                        _lastQueueActivity = currentTime;
                    }
                    
                    // Check queue size limit and drop oldest frame if needed
                    var currentQueueSize = _jpegDataQueue.Count;
                    if (currentQueueSize >= _maxQueueSize)
                    {
                        if (_jpegDataQueue.TryDequeue(out var droppedItem))
                        {
                            Interlocked.Increment(ref _totalFramesDropped);
                            Logger.Info($"Dropped frame due to queue overflow: metadata={droppedItem?.Metadata}, " +
                                      $"size={droppedItem?.JpegData?.Length ?? 0} bytes");
                            
                            // Fire dropped event outside of lock to prevent deadlocks
                            Task.Run(() =>
                            {
                                try
                                {
                                    JpegDataDropped?.Invoke(this, new JpegDataDroppedEventArgs(
                                        droppedItem?.JpegData ?? new byte[0], 
                                        "Queue overflow", 
                                        droppedItem?.Metadata));
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Error firing JpegDataDropped event: {ex.Message}", ex);
                                }
                            });
                        }
                    }
                    
                    // Create transmission item with locked data copy
                    var item = new JpegTransmissionItem
                    {
                        JpegData = jpegData, // Use the defensive copy
                        Priority = priority,
                        Metadata = metadata,
                        QueuedTime = currentTime,
                        RetryCount = 0
                    };
                    
                    // Enqueue the item (ConcurrentQueue is thread-safe, but we're already in lock for consistency)
                    _jpegDataQueue.Enqueue(item);
                    Interlocked.Increment(ref _totalFramesQueued);
                    
                    var newQueueSize = _jpegDataQueue.Count;
                    Logger.Info($"JPEG data queued safely: size={jpegData.Length} bytes, priority={priority}, " +
                              $"metadata={metadata}, queue size: {newQueueSize}");
                    
                    // Fire events outside of lock to prevent potential deadlocks
                    Task.Run(() =>
                    {
                        try
                        {
                            JpegDataQueued?.Invoke(this, new JpegDataQueuedEventArgs(jpegData, priority, metadata));
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error firing JpegDataQueued event: {ex.Message}", ex);
                        }
                    });
                    
                    Task.Run(() =>
                    {
                        try
                        {
                            QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs(newQueueSize, _maxQueueSize));
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error firing QueueStatusChanged event: {ex.Message}", ex);
                        }
                    });
                    
                    // Start processing immediately when data is queued (thread-safe)
                    StartProcessingIfNeeded();
                    
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error queuing JPEG data: {ex.Message}", ex);
                    
                    // Fire error event
                    Task.Run(() =>
                    {
                        try
                        {
                            TransmissionError?.Invoke(this, new TransmissionErrorEventArgs(ex, "Error queuing JPEG data"));
                        }
                        catch (Exception eventEx)
                        {
                            Logger.Error($"Error firing TransmissionError event: {eventEx.Message}", eventEx);
                        }
                    });
                    
                    return false;
                }
            }
        }
        
        /// <summary>
        /// Enhanced queue monitoring callback that manages real-time mode based on queue status
        /// </summary>
        private async void QueueMonitorCallback(object? state)
        {
            if (_disposed) return;
            
            try
            {
                var currentQueueSize = _jpegDataQueue.Count;
                var timeSinceActivity = DateTime.Now - _lastQueueActivity;
                var hasData = currentQueueSize > 0;
                
                // Fire monitoring update event
                QueueMonitorUpdate?.Invoke(this, new QueueMonitorEventArgs(
                    currentQueueSize, 
                    hasData, 
                    _isRealTimeEnabled, 
                    _isProcessing, 
                    timeSinceActivity));
                
                // Decision logic for real-time mode management
                if (hasData && !_isRealTimeEnabled)
                {
                    // Queue has data but real-time mode is not enabled - enable it
                    Logger.Info($"Queue has {currentQueueSize} items - enabling real-time mode");
                    await EnableRealTimeModeIfNeeded();
                }
                else if (!hasData && _isRealTimeEnabled && timeSinceActivity.TotalMilliseconds > _realTimeModeTimeoutMs)
                {
                    // Queue is empty and no activity for timeout period - consider disabling real-time mode
                    Logger.Info($"Queue empty for {timeSinceActivity.TotalSeconds:F1}s - considering disabling real-time mode");
                    await ConsiderDisablingRealTimeMode();
                }
                
                // Adaptive processing frequency based on queue load
                await AdjustProcessingFrequency(currentQueueSize);
                
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in queue monitoring: {ex.Message}", ex);
                TransmissionError?.Invoke(this, new TransmissionErrorEventArgs(ex, "Queue monitoring error"));
            }
        }
        
        /// <summary>
        /// Enable real-time mode if needed and update statistics
        /// </summary>
        private async Task EnableRealTimeModeIfNeeded()
        {
            if (_isRealTimeEnabled) return;
            
            try
            {
                Logger.Info("Enabling real-time mode due to queued data");
                var results = await _hidDeviceService.SetRealTimeDisplayAsync(true);
                
                var successCount = 0;
                foreach (var result in results)
                {
                    if (result.Value) successCount++;
                }
                
                if (successCount > 0)
                {
                    _isRealTimeEnabled = true;
                    Interlocked.Increment(ref _realTimeModeEnableCount);
                    _lastRealTimeModeCheck = DateTime.Now;
                    
                    Logger.Info($"Real-time mode enabled on {successCount}/{results.Count} devices (Enable count: {_realTimeModeEnableCount})");
                    RealTimeModeChanged?.Invoke(this, new RealTimeModeChangedEventArgs(true, successCount, results.Count));
                }
                else
                {
                    Logger.Warn("Failed to enable real-time mode on any devices");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enable real-time mode: {ex.Message}", ex);
                TransmissionError?.Invoke(this, new TransmissionErrorEventArgs(ex, "Failed to enable real-time mode"));
            }
        }
        
        /// <summary>
        /// Consider disabling real-time mode when queue is empty for extended period
        /// </summary>
        private async Task ConsiderDisablingRealTimeMode()
        {
            if (!_isRealTimeEnabled) return;
            
            // Additional check: only disable if we haven't checked recently and queue is truly empty
            var timeSinceLastCheck = DateTime.Now - _lastRealTimeModeCheck;
            if (timeSinceLastCheck.TotalMilliseconds < _realTimeModeTimeoutMs / 2) return;
            
            if (_jpegDataQueue.IsEmpty)
            {
                try
                {
                    Logger.Info("Disabling real-time mode due to queue inactivity");
                    var results = await _hidDeviceService.SetRealTimeDisplayAsync(false);
                    
                    var successCount = 0;
                    foreach (var result in results)
                    {
                        if (result.Value) successCount++;
                    }
                    
                    _isRealTimeEnabled = false;
                    Interlocked.Increment(ref _realTimeModeDisableCount);
                    _lastRealTimeModeCheck = DateTime.Now;
                    
                    Logger.Info($"Real-time mode disabled on {successCount}/{results.Count} devices (Disable count: {_realTimeModeDisableCount})");
                    RealTimeModeChanged?.Invoke(this, new RealTimeModeChangedEventArgs(false, successCount, results.Count));
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to disable real-time mode: {ex.Message}", ex);
                    TransmissionError?.Invoke(this, new TransmissionErrorEventArgs(ex, "Failed to disable real-time mode"));
                }
            }
        }
        
        /// <summary>
        /// Adjust processing frequency based on queue load for better performance
        /// </summary>
        private async Task AdjustProcessingFrequency(int queueSize)
        {
            if (!_isProcessing) return;
            
            int newInterval = _processingIntervalMs;
            
            // Adaptive processing intervals based on queue pressure
            if (queueSize > _maxQueueSize * 0.8) // High load (>80% full)
            {
                newInterval = Math.Max(_processingIntervalMs / 2, 10); // Faster processing, minimum 10ms
            }
            else if (queueSize > _maxQueueSize * 0.5) // Medium load (>50% full)
            {
                newInterval = (int)(_processingIntervalMs * 0.8); // Slightly faster
            }
            else if (queueSize < _maxQueueSize * 0.2) // Low load (<20% full)
            {
                newInterval = Math.Min(_processingIntervalMs * 2, 100); // Slower processing, maximum 100ms
            }
            
            // Only update if interval has changed significantly
            if (Math.Abs(newInterval - _processingIntervalMs) > 5)
            {
                _processingTimer.Change(0, newInterval);
                Logger.Info($"Adjusted processing interval to {newInterval}ms based on queue load ({queueSize} items)");
            }
        }
        
        /// <summary>
        /// Start processing the queue if there's data and processing is not already running
        /// </summary>
        private void StartProcessingIfNeeded()
        {
            lock (_lockObject)
            {
                if (!_isProcessing && !_jpegDataQueue.IsEmpty)
                {
                    _isProcessing = true;
                    _processingTimer.Change(0, _processingIntervalMs);
                    Logger.Info($"Started JPEG data processing - queue size: {_jpegDataQueue.Count}");
                }
            }
        }
        
        /// <summary>
        /// Stop processing the queue
        /// </summary>
        private void StopProcessing()
        {
            lock (_lockObject)
            {
                if (_isProcessing)
                {
                    _isProcessing = false;
                    _processingTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    Logger.Info("Stopped JPEG data processing");
                }
            }
        }
        
        /// <summary>
        /// Timer callback for processing the queue
        /// </summary>
        private async void ProcessQueueCallback(object? state)
        {
            if (_disposed || !_isProcessing)
                return;
                
            try
            {
                await ProcessQueueAsync();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in queue processing: {ex.Message}", ex);
                TransmissionError?.Invoke(this, new TransmissionErrorEventArgs(ex, "Queue processing error"));
            }
        }
        
        /// <summary>
        /// Enhanced process queue async with better error handling and monitoring
        /// </summary>
        private async Task ProcessQueueAsync()
        {
            // Check if queue is empty
            if (_jpegDataQueue.IsEmpty)
            {
                await HandleEmptyQueue();
                return;
            }
            
            // Ensure HID devices are in real-time mode before processing
            if (!_isRealTimeEnabled)
            {
                await EnableRealTimeModeIfNeeded();
                // If still not enabled, skip this processing cycle
                if (!_isRealTimeEnabled)
                {
                    Logger.Warn("Cannot process queue - real-time mode not enabled");
                    return;
                }
            }
            
            // Process next item in queue
            if (_jpegDataQueue.TryDequeue(out var item))
            {
                lock (_timestampLock)
                {
                    _lastQueueActivity = DateTime.Now;
                }
                await ProcessTransmissionItem(item);
                
                // Fire queue status changed event
                QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs(_jpegDataQueue.Count, _maxQueueSize));
            }
        }
        
        /// <summary>
        /// Handle empty queue scenario with improved logic
        /// </summary>
        private async Task HandleEmptyQueue()
        {
            // Stop processing timer
            StopProcessing();
            
            Logger.Info("Queue is empty, processing stopped - real-time mode monitoring continues");
            
            // Note: We don't immediately disable real-time mode here anymore
            // The queue monitor will handle real-time mode management based on timeout
        }
        
        /// <summary>
        /// Process a single transmission item
        /// </summary>
        private async Task ProcessTransmissionItem(JpegTransmissionItem item)
        {
            try
            {
                // Get next transfer ID
                var transferId = GetNextTransferId();
                
                Logger.Info($"Sending JPEG data: size={item.JpegData.Length} bytes, " +
                           $"transferId={transferId}, metadata={item.Metadata}");
                
                // Send data to HID devices
                var results = await _hidDeviceService.TransferDataAsync(item.JpegData, transferId);
                
                var successCount = 0;
                foreach (var result in results)
                {
                    if (result.Value)
                        successCount++;
                }
                
                if (successCount > 0)
                {
                    Interlocked.Increment(ref _totalFramesSent);
                    Logger.Info($"JPEG data sent successfully to {successCount}/{results.Count} devices");
                    
                    JpegDataSent?.Invoke(this, new JpegDataSentEventArgs(
                        item.JpegData, 
                        transferId, 
                        successCount, 
                        results.Count, 
                        item.Metadata));
                }
                else
                {
                    // All transmissions failed, retry if possible
                    await HandleTransmissionFailure(item, "All device transmissions failed");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing transmission item: {ex.Message}", ex);
                await HandleTransmissionFailure(item, ex.Message);
            }
        }
        
        /// <summary>
        /// Handle transmission failure with retry logic
        /// </summary>
        private async Task HandleTransmissionFailure(JpegTransmissionItem item, string errorMessage)
        {
            item.RetryCount++;
            Interlocked.Increment(ref _totalRetries);
            
            if (item.RetryCount <= _maxRetryAttempts)
            {
                Logger.Warn($"Retrying transmission (attempt {item.RetryCount}/{_maxRetryAttempts}): {errorMessage}");
                
                // Re-queue the item for retry
                _jpegDataQueue.Enqueue(item);
                
                // Add small delay before retry
                await Task.Delay(100);
            }
            else
            {
                Interlocked.Increment(ref _totalFramesDropped);
                Logger.Error($"Dropping frame after {_maxRetryAttempts} failed attempts: {errorMessage}");
                
                JpegDataDropped?.Invoke(this, new JpegDataDroppedEventArgs(
                    item.JpegData, 
                    $"Max retries exceeded: {errorMessage}", 
                    item.Metadata));
                
                TransmissionError?.Invoke(this, new TransmissionErrorEventArgs(
                    new Exception(errorMessage), 
                    "Transmission failed after retries"));
            }
        }
        
        /// <summary>
        /// Get next transfer ID with automatic cycling
        /// </summary>
        private byte GetNextTransferId()
        {
            var id = _currentTransferId;
            _currentTransferId++;
            if (_currentTransferId > _transferIdRange)
                _currentTransferId = 1;
            return id;
        }
        
        /// <summary>
        /// Manually disable real-time mode on HID devices
        /// </summary>
        public async Task DisableRealTimeModeAsync()
        {
            if (!_isRealTimeEnabled)
                return;
                
            try
            {
                Logger.Info("Disabling real-time mode on HID devices");
                var results = await _hidDeviceService.SetRealTimeDisplayAsync(false);
                
                var successCount = 0;
                foreach (var result in results)
                {
                    if (result.Value)
                        successCount++;
                }
                
                _isRealTimeEnabled = false;
                Logger.Info($"Real-time mode disabled on {successCount}/{results.Count} devices");
                RealTimeModeChanged?.Invoke(this, new RealTimeModeChangedEventArgs(false, successCount, results.Count));
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to disable real-time mode: {ex.Message}", ex);
                TransmissionError?.Invoke(this, new TransmissionErrorEventArgs(ex, "Failed to disable real-time mode"));
                throw;
            }
        }
        
        /// <summary>
        /// Clear all queued data with thread-safe locking
        /// </summary>
        public void ClearQueue()
        {
            lock (_queueOperationLock)
            {
                try
                {
                    var droppedCount = 0;
                    var droppedItems = new List<JpegTransmissionItem>();
                    
                    // Collect items to be dropped
                    while (_jpegDataQueue.TryDequeue(out var item))
                    {
                        droppedCount++;
                        droppedItems.Add(item);
                    }
                    
                    if (droppedCount > 0)
                    {
                        Interlocked.Add(ref _totalFramesDropped, droppedCount);
                        Logger.Info($"Cleared {droppedCount} items from queue safely");
                        
                        // Fire events outside of lock to prevent deadlocks
                        Task.Run(() =>
                        {
                            try
                            {
                                foreach (var item in droppedItems)
                                {
                                    JpegDataDropped?.Invoke(this, new JpegDataDroppedEventArgs(
                                        item.JpegData, 
                                        "Queue cleared", 
                                        item.Metadata));
                                }
                                
                                QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs(0, _maxQueueSize));
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Error firing events during queue clear: {ex.Message}", ex);
                            }
                        });
                    }
                    else
                    {
                        Logger.Info("Queue was already empty during clear operation");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error clearing queue: {ex.Message}", ex);
                    
                    Task.Run(() =>
                    {
                        try
                        {
                            TransmissionError?.Invoke(this, new TransmissionErrorEventArgs(ex, "Error clearing queue"));
                        }
                        catch (Exception eventEx)
                        {
                            Logger.Error($"Error firing TransmissionError event during clear: {eventEx.Message}", eventEx);
                        }
                    });
                }
            }
        }
        
        /// <summary>
        /// Reset statistics counters
        /// </summary>
        public void ResetStatistics()
        {
            Interlocked.Exchange(ref _totalFramesQueued, 0);
            Interlocked.Exchange(ref _totalFramesSent, 0);
            Interlocked.Exchange(ref _totalFramesDropped, 0);
            Interlocked.Exchange(ref _totalRetries, 0);
            Logger.Info("Statistics counters reset");
        }
        
        /// <summary>
        /// Dispose the service and cleanup resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
                
            Logger.Info("Disposing RealtimeJpegTransmissionService");
            
            try
            {
                // Stop processing and monitoring
                StopProcessing();
                
                // Stop queue monitoring timer
                _queueMonitorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                
                // Disable real-time mode with timeout
                if (_isRealTimeEnabled)
                {
                    var disableTask = Task.Run(async () =>
                    {
                        try
                        {
                            await DisableRealTimeModeAsync();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error disabling real-time mode during disposal: {ex.Message}", ex);
                        }
                    });
                    
                    // Wait up to 5 seconds for cleanup
                    if (!disableTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        Logger.Warn("Timeout waiting for real-time mode disable during disposal");
                    }
                }
                
                // Clear queue and notify about dropped items
                ClearQueue();
                
                // Dispose timers
                _processingTimer?.Dispose();
                _queueMonitorTimer?.Dispose();
                
                _disposed = true;
                
                // Log final statistics
                var finalStats = Statistics;
                Logger.Info("RealtimeJpegTransmissionService disposed. " +
                          $"Final stats: {finalStats.GetDetailedReport()}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during RealtimeJpegTransmissionService disposal: {ex.Message}", ex);
            }
        }
    }
    
    #region Supporting Classes and Event Args
    
    /// <summary>
    /// Represents an item in the transmission queue
    /// </summary>
    internal class JpegTransmissionItem
    {
        public byte[] JpegData { get; set; } = Array.Empty<byte>();
        public int Priority { get; set; }
        public string? Metadata { get; set; }
        public DateTime QueuedTime { get; set; }
        public int RetryCount { get; set; }
    }
    
    /// <summary>
    /// Transmission statistics data
    /// </summary>
    public class TransmissionStatistics
    {
        public long TotalFramesQueued { get; set; }
        public long TotalFramesSent { get; set; }
        public long TotalFramesDropped { get; set; }
        public long TotalRetries { get; set; }
        public int CurrentQueueSize { get; set; }
        public bool IsRealTimeModeEnabled { get; set; }
        
        /// <summary>
        /// Calculate success rate as percentage
        /// </summary>
        public double SuccessRate => TotalFramesQueued > 0 ? (double)TotalFramesSent / TotalFramesQueued * 100 : 0;
        
        /// <summary>
        /// Calculate drop rate as percentage
        /// </summary>
        public double DropRate => TotalFramesQueued > 0 ? (double)TotalFramesDropped / TotalFramesQueued * 100 : 0;
    }
    
    /// <summary>
    /// Enhanced transmission statistics with additional monitoring information
    /// </summary>
    public class EnhancedTransmissionStatistics
    {
        public long TotalFramesQueued { get; set; }
        public long TotalFramesSent { get; set; }
        public long TotalFramesDropped { get; set; }
        public long TotalRetries { get; set; }
        public int CurrentQueueSize { get; set; }
        public bool IsRealTimeModeEnabled { get; set; }
        public long RealTimeModeEnableCount { get; set; }
        public long RealTimeModeDisableCount { get; set; }
        public TimeSpan TimeSinceLastActivity { get; set; }
        public bool IsProcessingActive { get; set; }
        
        /// <summary>
        /// Calculate success rate as percentage
        /// </summary>
        public double SuccessRate => TotalFramesQueued > 0 ? (double)TotalFramesSent / TotalFramesQueued * 100 : 0;
        
        /// <summary>
        /// Calculate drop rate as percentage
        /// </summary>
        public double DropRate => TotalFramesQueued > 0 ? (double)TotalFramesDropped / TotalFramesQueued * 100 : 0;
        
        /// <summary>
        /// Calculate queue utilization percentage
        /// </summary>
        public double QueueUtilization { get; set; } = 0;
        
        /// <summary>
        /// Get health status based on performance metrics
        /// </summary>
        public string GetHealthStatus()
        {
            if (SuccessRate > 95 && DropRate < 2) return "Excellent";
            if (SuccessRate > 85 && DropRate < 5) return "Good";
            if (SuccessRate > 70 && DropRate < 10) return "Fair";
            return "Poor";
        }
        
        /// <summary>
        /// Get detailed status report
        /// </summary>
        public string GetDetailedReport()
        {
            return $"Queue: {CurrentQueueSize} items, " +
                   $"Success: {SuccessRate:F1}%, " +
                   $"Drop: {DropRate:F1}%, " +
                   $"RealTime: {(IsRealTimeModeEnabled ? "ON" : "OFF")}, " +
                   $"Processing: {(IsProcessingActive ? "ACTIVE" : "IDLE")}, " +
                   $"LastActivity: {TimeSinceLastActivity.TotalSeconds:F1}s ago, " +
                   $"Health: {GetHealthStatus()}";
        }
    }
    
    /// <summary>
    /// Event arguments for JPEG data queued events
    /// </summary>
    public class JpegDataQueuedEventArgs : EventArgs
    {
        public byte[] JpegData { get; }
        public int Priority { get; }
        public string? Metadata { get; }
        public DateTime Timestamp { get; }
        
        public JpegDataQueuedEventArgs(byte[] jpegData, int priority, string? metadata)
        {
            JpegData = jpegData;
            Priority = priority;
            Metadata = metadata;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for JPEG data sent events
    /// </summary>
    public class JpegDataSentEventArgs : EventArgs
    {
        public byte[] JpegData { get; }
        public byte TransferId { get; }
        public int SuccessfulDevices { get; }
        public int TotalDevices { get; }
        public string? Metadata { get; }
        public DateTime Timestamp { get; }
        
        public JpegDataSentEventArgs(byte[] jpegData, byte transferId, int successfulDevices, int totalDevices, string? metadata)
        {
            JpegData = jpegData;
            TransferId = transferId;
            SuccessfulDevices = successfulDevices;
            TotalDevices = totalDevices;
            Metadata = metadata;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for JPEG data dropped events
    /// </summary>
    public class JpegDataDroppedEventArgs : EventArgs
    {
        public byte[] JpegData { get; }
        public string Reason { get; }
        public string? Metadata { get; }
        public DateTime Timestamp { get; }
        
        public JpegDataDroppedEventArgs(byte[] jpegData, string reason, string? metadata)
        {
            JpegData = jpegData;
            Reason = reason;
            Metadata = metadata;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for transmission error events
    /// </summary>
    public class TransmissionErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public string Context { get; }
        public DateTime Timestamp { get; }
        
        public TransmissionErrorEventArgs(Exception exception, string context)
        {
            Exception = exception;
            Context = context;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for queue status changed events
    /// </summary>
    public class QueueStatusChangedEventArgs : EventArgs
    {
        public int CurrentSize { get; }
        public int MaxSize { get; }
        public double FillPercentage { get; }
        public DateTime Timestamp { get; }
        
        public QueueStatusChangedEventArgs(int currentSize, int maxSize)
        {
            CurrentSize = currentSize;
            MaxSize = maxSize;
            FillPercentage = maxSize > 0 ? (double)currentSize / maxSize * 100 : 0;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for real-time mode changed events
    /// </summary>
    public class RealTimeModeChangedEventArgs : EventArgs
    {
        public bool IsEnabled { get; }
        public int SuccessfulDevices { get; }
        public int TotalDevices { get; }
        public DateTime Timestamp { get; }
        
        public RealTimeModeChangedEventArgs(bool isEnabled, int successfulDevices, int totalDevices)
        {
            IsEnabled = isEnabled;
            SuccessfulDevices = successfulDevices;
            TotalDevices = totalDevices;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for queue monitoring updates
    /// </summary>
    public class QueueMonitorEventArgs : EventArgs
    {
        public int CurrentQueueSize { get; }
        public bool HasData { get; }
        public bool IsRealTimeModeEnabled { get; }
        public bool IsProcessingActive { get; }
        public TimeSpan TimeSinceLastActivity { get; }
        public DateTime Timestamp { get; }
        public double QueueUtilizationPercentage { get; }
        
        public QueueMonitorEventArgs(int queueSize, bool hasData, bool isRealTimeEnabled, bool isProcessing, TimeSpan timeSinceActivity)
        {
            CurrentQueueSize = queueSize;
            HasData = hasData;
            IsRealTimeModeEnabled = isRealTimeEnabled;
            IsProcessingActive = isProcessing;
            TimeSinceLastActivity = timeSinceActivity;
            Timestamp = DateTime.Now;
            QueueUtilizationPercentage = 0; // Will be calculated by the service
        }
        
        public string GetStatusSummary()
        {
            return $"Queue: {CurrentQueueSize}, Data: {(HasData ? "YES" : "NO")}, " +
                   $"RealTime: {(IsRealTimeModeEnabled ? "ON" : "OFF")}, " +
                   $"Processing: {(IsProcessingActive ? "ACTIVE" : "IDLE")}, " +
                   $"Inactive: {TimeSinceLastActivity.TotalSeconds:F1}s";
        }
    }
    
    #endregion
}


//// Get the service from ServiceLocator (after proper initialization)
//var realtimeJpegService = ServiceLocator.TryGetRealtimeJpegTransmissionService();

//if (realtimeJpegService != null)
//{
//    // Queue your JPEG data
//    var success = realtimeJpegService.QueueJpegData(yourJpegBytes, priority: 1);

//    // The service automatically:
//    // - Enables HID real-time mode when queue has data
//    // - Processes queue at ~30 FPS
//    // - Handles transmission errors and retries
//    // - Provides comprehensive statistics and events
//}