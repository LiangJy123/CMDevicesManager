using CMDevicesManager.Helper;
using CMDevicesManager.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Simplified service for handling real-time JPEG data transmission to HID devices.
    /// Uses a queue system with automatic real-time mode management.
    /// </summary>
    public class RealtimeJpegTransmissionService : IDisposable
    {
        private readonly ConcurrentQueue<JpegFrame> _frameQueue;
        private readonly HidDeviceService _hidDeviceService;
        private readonly System.Threading.Timer _processTimer;
        private readonly SemaphoreSlim _processingLock;
        private readonly object _stateLock = new object();
        
        private volatile bool _disposed = false;
        private volatile bool _isProcessing = false;
        private volatile bool _isRealTimeEnabled = false;
        private byte _currentTransferId = 1;
        
        // Configuration
        private readonly int _processingIntervalMs;
        private readonly int _maxQueueSize;
        private readonly int _realTimeTimeoutMs;
        
        // Statistics (using Interlocked for thread safety)
        private long _totalQueued = 0;
        private long _totalSent = 0;
        private long _totalDropped = 0;
        private DateTime _lastActivity = DateTime.Now;
        
        // Events
        public event EventHandler<JpegFrameProcessedEventArgs>? FrameProcessed;
        public event EventHandler<JpegFrameDroppedEventArgs>? FrameDropped;
        public event EventHandler<RealtimeModeChangedEventArgs>? RealTimeModeChanged;
        public event EventHandler<RealtimeServiceErrorEventArgs>? ServiceError;
        
        // Properties
        public bool IsRealTimeModeEnabled => _isRealTimeEnabled;
        public int QueueSize => _frameQueue.Count;
        public int MaxQueueSize => _maxQueueSize;
        public bool IsProcessing => _isProcessing;
        
        public TransmissionStats Statistics => new TransmissionStats
        {
            TotalQueued = Interlocked.Read(ref _totalQueued),
            TotalSent = Interlocked.Read(ref _totalSent),
            TotalDropped = Interlocked.Read(ref _totalDropped),
            CurrentQueueSize = QueueSize,
            IsRealTimeModeEnabled = _isRealTimeEnabled,
            LastActivity = _lastActivity
        };
        
        public RealtimeJpegTransmissionService(
            HidDeviceService hidDeviceService,
            int processingIntervalMs = 33, // ~30 FPS
            int maxQueueSize = 10,
            int realTimeTimeoutMs = 5000)
        {
            _hidDeviceService = hidDeviceService ?? throw new ArgumentNullException(nameof(hidDeviceService));
            _processingIntervalMs = Math.Max(16, processingIntervalMs); // Min 16ms (~60 FPS max)
            _maxQueueSize = Math.Max(1, maxQueueSize);
            _realTimeTimeoutMs = Math.Max(1000, realTimeTimeoutMs);
            
            _frameQueue = new ConcurrentQueue<JpegFrame>();
            _processingLock = new SemaphoreSlim(1, 1);
            _processTimer = new System.Threading.Timer(ProcessQueueCallback, null, Timeout.Infinite, Timeout.Infinite);
            
            Debug.WriteLine($"RealtimeJpegTransmissionService initialized: " +
                          $"Interval={_processingIntervalMs}ms, MaxQueue={_maxQueueSize}, Timeout={_realTimeTimeoutMs}ms");
        }
        
        /// <summary>
        /// Queue JPEG data for transmission
        /// </summary>
        public bool QueueJpegData(byte[] jpegData, string? metadata = null)
        {
            if (_disposed)
            {
                Logger.Warn("Service is disposed");
                return false;
            }
            
            if (jpegData == null || jpegData.Length == 0)
            {
                Logger.Warn("Invalid JPEG data");
                return false;
            }
            
            try
            {
                // Update activity time
                _lastActivity = DateTime.Now;
                
                // Handle queue overflow - drop oldest frame
                while (_frameQueue.Count >= _maxQueueSize)
                {
                    if (_frameQueue.TryDequeue(out var droppedFrame))
                    {
                        Interlocked.Increment(ref _totalDropped);
                        OnFrameDropped(droppedFrame, "Queue overflow");
                    }
                }
                
                // Create and enqueue new frame
                var frame = new JpegFrame
                {
                    Data = jpegData,
                    Metadata = metadata,
                    QueueTime = DateTime.Now
                };
                
                _frameQueue.Enqueue(frame);
                Interlocked.Increment(ref _totalQueued);
                
                Debug.WriteLine($"Frame queued: {jpegData.Length} bytes, queue size: {_frameQueue.Count}");
                
                // Start processing if not already running
                _ = Task.Run(StartProcessingAsync);
                
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error queuing frame: {ex.Message}", ex);
                OnServiceError(ex, "Queue error");
                return false;
            }
        }
        
        /// <summary>
        /// Start processing if needed
        /// </summary>
        private async Task StartProcessingAsync()
        {
            if (_disposed || _isProcessing || _frameQueue.IsEmpty) 
                return;
            
            if (!await _processingLock.WaitAsync(100))
                return;
            
            try
            {
                if (_isProcessing || _frameQueue.IsEmpty)
                    return;
                
                lock (_stateLock)
                {
                    if (_isProcessing) return;
                    _isProcessing = true;
                }
                
                // Enable real-time mode if needed
                await EnsureRealTimeModeAsync();
                
                // Start timer
                _processTimer.Change(_processingIntervalMs, _processingIntervalMs);
                
                Debug.WriteLine("Processing started");
            }
            finally
            {
                _processingLock.Release();
            }
        }
        
        /// <summary>
        /// Stop processing
        /// </summary>
        private void StopProcessing()
        {
            lock (_stateLock)
            {
                if (!_isProcessing) return;
                _isProcessing = false;
            }
            
            _processTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Debug.WriteLine("Processing stopped");
        }
        
        /// <summary>
        /// Timer callback for processing frames
        /// </summary>
        private async void ProcessQueueCallback(object? state)
        {
            if (_disposed) return;
            
            try
            {
                // Check if queue is empty
                if (_frameQueue.IsEmpty)
                {
                    await HandleEmptyQueueAsync();
                    return;
                }
                
                // Ensure real-time mode is enabled
                if (!_isRealTimeEnabled)
                {
                    await EnsureRealTimeModeAsync();
                    if (!_isRealTimeEnabled)
                    {
                        Logger.Warn("Cannot process - real-time mode not enabled");
                        return;
                    }
                }
                
                // Process next frame
                if (_frameQueue.TryDequeue(out var frame))
                {
                    _lastActivity = DateTime.Now;
                    await ProcessFrameAsync(frame);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in processing callback: {ex.Message}", ex);
                OnServiceError(ex, "Processing error");
            }
        }
        
        /// <summary>
        /// Process a single frame
        /// </summary>
        private async Task ProcessFrameAsync(JpegFrame frame)
        {
            try
            {
                var transferId = GetNextTransferId();
                
                Debug.WriteLine($"Sending frame: {frame.Data.Length} bytes, transferId={transferId}");
                
                // Send to HID devices
                var results = await _hidDeviceService.TransferDataAsync(frame.Data, transferId);
                
                int successCount = 0;
                foreach (var result in results)
                {
                    if (result.Value) successCount++;
                }
                
                if (successCount > 0)
                {
                    Interlocked.Increment(ref _totalSent);
                    OnFrameProcessed(frame, transferId, successCount, results.Count);
                }
                else
                {
                    Interlocked.Increment(ref _totalDropped);
                    OnFrameDropped(frame, "All device transmissions failed");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing frame: {ex.Message}", ex);
                Interlocked.Increment(ref _totalDropped);
                OnFrameDropped(frame, ex.Message);
            }
        }
        
        /// <summary>
        /// Handle empty queue - stop processing and manage real-time mode
        /// </summary>
        private async Task HandleEmptyQueueAsync()
        {
            StopProcessing();
            
            // Check if we should disable real-time mode
            var timeSinceActivity = DateTime.Now - _lastActivity;
            if (_isRealTimeEnabled && timeSinceActivity.TotalMilliseconds > _realTimeTimeoutMs)
            {
                await DisableRealTimeModeAsync();
            }
        }
        
        /// <summary>
        /// Ensure real-time mode is enabled
        /// </summary>
        private async Task EnsureRealTimeModeAsync()
        {
            if (_isRealTimeEnabled) return;
            
            try
            {
                Debug.WriteLine("Enabling real-time mode");
                var results = await _hidDeviceService.SetRealTimeDisplayAsync(true);
                
                int successCount = 0;
                foreach (var result in results)
                {
                    if (result.Value) successCount++;
                }
                
                if (successCount > 0)
                {
                    _isRealTimeEnabled = true;
                    Debug.WriteLine($"Real-time mode enabled on {successCount} devices");
                    OnRealTimeModeChanged(true, successCount, results.Count);
                }
                else
                {
                    Logger.Warn("Failed to enable real-time mode on any devices");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error enabling real-time mode: {ex.Message}", ex);
                OnServiceError(ex, "Enable real-time mode failed");
            }
        }
        
        /// <summary>
        /// Disable real-time mode
        /// </summary>
        private async Task DisableRealTimeModeAsync()
        {
            if (!_isRealTimeEnabled) return;
            
            try
            {
                Debug.WriteLine("Disabling real-time mode");
                var results = await _hidDeviceService.SetRealTimeDisplayAsync(false);
                
                int successCount = 0;
                foreach (var result in results)
                {
                    if (result.Value) successCount++;
                }
                
                _isRealTimeEnabled = false;
                Debug.WriteLine($"Real-time mode disabled on {successCount} devices");
                OnRealTimeModeChanged(false, successCount, results.Count);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error disabling real-time mode: {ex.Message}", ex);
                OnServiceError(ex, "Disable real-time mode failed");
            }
        }
        
        /// <summary>
        /// Get next transfer ID (1-59)
        /// </summary>
        private byte GetNextTransferId()
        {
            var id = _currentTransferId;
            _currentTransferId = (byte)(_currentTransferId % 59 + 1);
            return id;
        }
        
        /// <summary>
        /// Clear all queued frames
        /// </summary>
        public void ClearQueue()
        {
            var droppedCount = 0;
            while (_frameQueue.TryDequeue(out var frame))
            {
                droppedCount++;
                OnFrameDropped(frame, "Queue cleared");
            }
            
            if (droppedCount > 0)
            {
                Interlocked.Add(ref _totalDropped, droppedCount);
                Debug.WriteLine($"Cleared {droppedCount} frames from queue");
            }
        }
        
        /// <summary>
        /// Manually disable real-time mode
        /// </summary>
        public async Task ManualDisableRealTimeModeAsync()
        {
            await DisableRealTimeModeAsync();
        }
        
        // Event handlers
        private void OnFrameProcessed(JpegFrame frame, byte transferId, int successCount, int totalDevices)
        {
            FrameProcessed?.Invoke(this, new JpegFrameProcessedEventArgs(frame, transferId, successCount, totalDevices));
        }
        
        private void OnFrameDropped(JpegFrame frame, string reason)
        {
            FrameDropped?.Invoke(this, new JpegFrameDroppedEventArgs(frame, reason));
        }
        
        private void OnRealTimeModeChanged(bool enabled, int successCount, int totalDevices)
        {
            RealTimeModeChanged?.Invoke(this, new RealtimeModeChangedEventArgs(enabled, successCount, totalDevices));
        }
        
        private void OnServiceError(Exception exception, string context)
        {
            ServiceError?.Invoke(this, new RealtimeServiceErrorEventArgs(exception, context));
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            Debug.WriteLine("Disposing RealtimeJpegTransmissionService");
            
            try
            {
                // Stop processing
                StopProcessing();
                
                // Disable real-time mode
                if (_isRealTimeEnabled)
                {
                    var task = DisableRealTimeModeAsync();
                    if (!task.Wait(TimeSpan.FromSeconds(3)))
                    {
                        Logger.Warn("Timeout disabling real-time mode during disposal");
                    }
                }
                
                // Clear queue
                ClearQueue();
                
                // Dispose resources
                _processTimer?.Dispose();
                _processingLock?.Dispose();
                
                var stats = Statistics;
                Debug.WriteLine($"Service disposed. Stats: Queued={stats.TotalQueued}, " +
                              $"Sent={stats.TotalSent}, Dropped={stats.TotalDropped}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during disposal: {ex.Message}", ex);
            }
        }
    }
    
    #region Supporting Classes
    
    /// <summary>
    /// Represents a JPEG frame in the queue
    /// </summary>
    public class JpegFrame
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string? Metadata { get; set; }
        public DateTime QueueTime { get; set; }
    }
    
    /// <summary>
    /// Transmission statistics
    /// </summary>
    public class TransmissionStats
    {
        public long TotalQueued { get; set; }
        public long TotalSent { get; set; }
        public long TotalDropped { get; set; }
        public int CurrentQueueSize { get; set; }
        public bool IsRealTimeModeEnabled { get; set; }
        public DateTime LastActivity { get; set; }
        
        public double SuccessRate => TotalQueued > 0 ? (double)TotalSent / TotalQueued * 100 : 0;
        public double DropRate => TotalQueued > 0 ? (double)TotalDropped / TotalQueued * 100 : 0;
        
        public override string ToString()
        {
            return $"Queued: {TotalQueued}, Sent: {TotalSent}, Dropped: {TotalDropped}, " +
                   $"Success: {SuccessRate:F1}%, Queue: {CurrentQueueSize}, RealTime: {IsRealTimeModeEnabled}";
        }
    }
    
    /// <summary>
    /// Event arguments for frame processed events
    /// </summary>
    public class JpegFrameProcessedEventArgs : EventArgs
    {
        public JpegFrame Frame { get; }
        public byte TransferId { get; }
        public int SuccessfulDevices { get; }
        public int TotalDevices { get; }
        public DateTime Timestamp { get; }
        
        public JpegFrameProcessedEventArgs(JpegFrame frame, byte transferId, int successfulDevices, int totalDevices)
        {
            Frame = frame;
            TransferId = transferId;
            SuccessfulDevices = successfulDevices;
            TotalDevices = totalDevices;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for frame dropped events
    /// </summary>
    public class JpegFrameDroppedEventArgs : EventArgs
    {
        public JpegFrame Frame { get; }
        public string Reason { get; }
        public DateTime Timestamp { get; }
        
        public JpegFrameDroppedEventArgs(JpegFrame frame, string reason)
        {
            Frame = frame;
            Reason = reason;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for real-time mode changed events
    /// </summary>
    public class RealtimeModeChangedEventArgs : EventArgs
    {
        public bool IsEnabled { get; }
        public int SuccessfulDevices { get; }
        public int TotalDevices { get; }
        public DateTime Timestamp { get; }
        
        public RealtimeModeChangedEventArgs(bool isEnabled, int successfulDevices, int totalDevices)
        {
            IsEnabled = isEnabled;
            SuccessfulDevices = successfulDevices;
            TotalDevices = totalDevices;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for service error events
    /// </summary>
    public class RealtimeServiceErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public string Context { get; }
        public DateTime Timestamp { get; }
        
        public RealtimeServiceErrorEventArgs(Exception exception, string context)
        {
            Exception = exception;
            Context = context;
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