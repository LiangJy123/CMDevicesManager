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
        private readonly object _lockObject = new object();
        
        private bool _disposed = false;
        private bool _isProcessing = false;
        private bool _isRealTimeEnabled = false;
        private byte _currentTransferId = 1;
        
        // Configuration settings
        private readonly int _processingIntervalMs;
        private readonly int _maxQueueSize;
        private readonly int _maxRetryAttempts;
        private readonly int _transferIdRange = 59; // Transfer IDs range from 1-59
        
        // Statistics
        private long _totalFramesQueued = 0;
        private long _totalFramesSent = 0;
        private long _totalFramesDropped = 0;
        private long _totalRetries = 0;
        
        // Events
        public event EventHandler<JpegDataQueuedEventArgs>? JpegDataQueued;
        public event EventHandler<JpegDataSentEventArgs>? JpegDataSent;
        public event EventHandler<JpegDataDroppedEventArgs>? JpegDataDropped;
        public event EventHandler<TransmissionErrorEventArgs>? TransmissionError;
        public event EventHandler<QueueStatusChangedEventArgs>? QueueStatusChanged;
        public event EventHandler<RealTimeModeChangedEventArgs>? RealTimeModeChanged;
        
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
        /// Gets transmission statistics
        /// </summary>
        public TransmissionStatistics Statistics => new TransmissionStatistics
        {
            TotalFramesQueued = _totalFramesQueued,
            TotalFramesSent = _totalFramesSent,
            TotalFramesDropped = _totalFramesDropped,
            TotalRetries = _totalRetries,
            CurrentQueueSize = QueueSize,
            IsRealTimeModeEnabled = _isRealTimeEnabled
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
            
            // Create timer but don't start it yet
            _processingTimer = new System.Threading.Timer(ProcessQueueCallback, null, Timeout.Infinite, Timeout.Infinite);
            
            Logger.Info($"RealtimeJpegTransmissionService initialized: " +
                       $"ProcessingInterval={_processingIntervalMs}ms, " +
                       $"MaxQueueSize={_maxQueueSize}, " +
                       $"MaxRetryAttempts={_maxRetryAttempts}");
        }
        
        /// <summary>
        /// Queue JPEG data for transmission to HID devices
        /// </summary>
        /// <param name="jpegData">JPEG data bytes</param>
        /// <param name="priority">Priority level (higher values = higher priority)</param>
        /// <param name="metadata">Optional metadata for the transmission</param>
        /// <returns>True if data was queued successfully, false if queue is full</returns>
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
            
            // Check queue size limit
            if (_jpegDataQueue.Count >= _maxQueueSize)
            {
                // Drop oldest frame to make room
                if (_jpegDataQueue.TryDequeue(out var droppedItem))
                {
                    Interlocked.Increment(ref _totalFramesDropped);
                    Logger.Info($"Dropped frame due to queue overflow: metadata={droppedItem?.Metadata}");
                    
                    JpegDataDropped?.Invoke(this, new JpegDataDroppedEventArgs(
                        droppedItem?.JpegData ?? new byte[0], 
                        "Queue overflow", 
                        droppedItem?.Metadata));
                }
            }
            
            var item = new JpegTransmissionItem
            {
                JpegData = jpegData,
                Priority = priority,
                Metadata = metadata,
                QueuedTime = DateTime.Now,
                RetryCount = 0
            };
            
            _jpegDataQueue.Enqueue(item);
            Interlocked.Increment(ref _totalFramesQueued);
            
            Logger.Info($"JPEG data queued: size={jpegData.Length} bytes, priority={priority}, metadata={metadata}");
            
            JpegDataQueued?.Invoke(this, new JpegDataQueuedEventArgs(jpegData, priority, metadata));
            
            // Start processing if not already running
            StartProcessingIfNeeded();
            
            // Fire queue status changed event
            QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs(_jpegDataQueue.Count, _maxQueueSize));
            
            return true;
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
                    Logger.Info("Started JPEG data processing");
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
        /// Process items in the queue
        /// </summary>
        private async Task ProcessQueueAsync()
        {
            // Check if queue is empty
            if (_jpegDataQueue.IsEmpty)
            {
                // If real-time mode is enabled and queue is empty, consider disabling it
                await HandleEmptyQueue();
                return;
            }
            
            // Ensure HID devices are in real-time mode
            await EnsureRealTimeModeEnabled();
            
            // Process next item in queue
            if (_jpegDataQueue.TryDequeue(out var item))
            {
                await ProcessTransmissionItem(item);
                
                // Fire queue status changed event
                QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs(_jpegDataQueue.Count, _maxQueueSize));
            }
        }
        
        /// <summary>
        /// Handle empty queue scenario
        /// </summary>
        private async Task HandleEmptyQueue()
        {
            // Stop processing timer
            StopProcessing();
            
            // Optionally disable real-time mode when queue is empty for a period
            // This could be configurable behavior
            // For now, we'll keep real-time mode enabled for immediate response
            
            Logger.Info("Queue is empty, processing stopped");
        }
        
        /// <summary>
        /// Ensure HID devices are in real-time mode
        /// </summary>
        private async Task EnsureRealTimeModeEnabled()
        {
            if (_isRealTimeEnabled)
                return;
                
            try
            {
                Logger.Info("Enabling real-time mode on HID devices");
                var results = await _hidDeviceService.SetRealTimeDisplayAsync(true);
                
                var successCount = 0;
                foreach (var result in results)
                {
                    if (result.Value)
                        successCount++;
                }
                
                if (successCount > 0)
                {
                    _isRealTimeEnabled = true;
                    Logger.Info($"Real-time mode enabled on {successCount}/{results.Count} devices");
                    RealTimeModeChanged?.Invoke(this, new RealTimeModeChangedEventArgs(true, successCount, results.Count));
                }
                else
                {
                    Logger.Warn("Failed to enable real-time mode on any devices");
                    throw new InvalidOperationException("Failed to enable real-time mode on HID devices");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enable real-time mode: {ex.Message}", ex);
                TransmissionError?.Invoke(this, new TransmissionErrorEventArgs(ex, "Failed to enable real-time mode"));
                throw;
            }
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
        /// Clear all queued data
        /// </summary>
        public void ClearQueue()
        {
            var droppedCount = 0;
            while (_jpegDataQueue.TryDequeue(out var item))
            {
                droppedCount++;
                JpegDataDropped?.Invoke(this, new JpegDataDroppedEventArgs(
                    item.JpegData, 
                    "Queue cleared", 
                    item.Metadata));
            }
            
            Interlocked.Add(ref _totalFramesDropped, droppedCount);
            Logger.Info($"Cleared {droppedCount} items from queue");
            
            QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs(0, _maxQueueSize));
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
                // Stop processing
                StopProcessing();
                
                // Disable real-time mode
                if (_isRealTimeEnabled)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await DisableRealTimeModeAsync();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error disabling real-time mode during disposal: {ex.Message}", ex);
                        }
                    }).Wait(TimeSpan.FromSeconds(5));
                }
                
                // Clear queue
                ClearQueue();
                
                // Dispose timer
                _processingTimer?.Dispose();
                
                _disposed = true;
                Logger.Info("RealtimeJpegTransmissionService disposed");
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