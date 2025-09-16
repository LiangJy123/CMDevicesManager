using CMDevicesManager.Helper;
using CMDevicesManager.Services;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// Simple and reliable service for real-time JPEG transmission to HID devices.
    /// Features automatic queue management and real-time mode handling.
    /// </summary>
    public class RealtimeJpegTransmissionService : IDisposable
    {
        private readonly HidDeviceService _hidDeviceService;
        private readonly ConcurrentQueue<JpegFrame> _frameQueue;
        private readonly CancellationTokenSource _cancellationTokenSource;
        
        // Configuration
        private readonly int _targetFps;
        private readonly int _maxQueueSize;
        private readonly TimeSpan _realtimeTimeout;
        
        // State
        private volatile bool _isRunning = false;
        private volatile bool _disposed = false;
        private byte _currentTransferId = 1;
        private DateTime _lastActivity = DateTime.Now;
        
        // Statistics 
        private long _totalQueued = 0;
        private long _totalSent = 0;
        private long _totalDropped = 0;
        
        // Events
        public event EventHandler<JpegFrameProcessedEventArgs>? FrameProcessed;
        public event EventHandler<JpegFrameDroppedEventArgs>? FrameDropped;
        public event EventHandler<RealtimeModeChangedEventArgs>? RealTimeModeChanged;
        public event EventHandler<RealtimeServiceErrorEventArgs>? ServiceError;
        
        // Properties
        public bool IsRunning => _isRunning && !_disposed;
        public int QueueSize => _frameQueue.Count;
        public int MaxQueueSize => _maxQueueSize;
        public bool IsRealTimeModeEnabled => _hidDeviceService.IsRealTimeDisplayEnabled;
        
        public TransmissionStats Statistics => new TransmissionStats
        {
            TotalQueued = Interlocked.Read(ref _totalQueued),
            TotalSent = Interlocked.Read(ref _totalSent),
            TotalDropped = Interlocked.Read(ref _totalDropped),
            CurrentQueueSize = QueueSize,
            IsRealTimeModeEnabled = IsRealTimeModeEnabled,
            LastActivity = _lastActivity
        };
        
        public RealtimeJpegTransmissionService(
            HidDeviceService hidDeviceService,
            int targetFps = 30,
            int maxQueueSize = 5,
            int realtimeTimeoutSeconds = 3)
        {
            _hidDeviceService = hidDeviceService ?? throw new ArgumentNullException(nameof(hidDeviceService));
            _targetFps = Math.Clamp(targetFps, 1, 60);
            _maxQueueSize = Math.Max(1, maxQueueSize);
            _realtimeTimeout = TimeSpan.FromSeconds(Math.Max(1, realtimeTimeoutSeconds));
            
            _frameQueue = new ConcurrentQueue<JpegFrame>();
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Start the processing task
            _ = Task.Run(ProcessingLoop, _cancellationTokenSource.Token);
            
            Logger.Info($"RealtimeJpegTransmissionService initialized: FPS={_targetFps}, MaxQueue={_maxQueueSize}, Timeout={_realtimeTimeout.TotalSeconds}s");
        }
        
        /// <summary>
        /// Queue JPEG data for transmission (keeping original method name for compatibility)
        /// </summary>
        public bool QueueJpegData(byte[] jpegData, string? metadata = null)
        {
            return QueueFrame(jpegData, metadata);
        }
        
        /// <summary>
        /// Queue JPEG data for transmission
        /// </summary>
        public bool QueueFrame(byte[] jpegData, string? metadata = null)
        {
            if (_disposed || jpegData == null || jpegData.Length == 0)
                return false;
            
            try
            {
                _lastActivity = DateTime.Now;
                
                // Drop oldest frames if queue is full
                while (_frameQueue.Count >= _maxQueueSize)
                {
                    if (_frameQueue.TryDequeue(out var droppedFrame))
                    {
                        Interlocked.Increment(ref _totalDropped);
                        OnFrameDropped(droppedFrame, "Queue overflow");
                    }
                }
                
                // Add new frame
                var frame = new JpegFrame
                {
                    Data = jpegData,
                    Metadata = metadata,
                    QueueTime = DateTime.Now
                };
                
                _frameQueue.Enqueue(frame);
                Interlocked.Increment(ref _totalQueued);
                
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
        /// Clear all queued frames
        /// </summary>
        public int ClearQueue()
        {
            int cleared = 0;
            while (_frameQueue.TryDequeue(out var frame))
            {
                cleared++;
                OnFrameDropped(frame, "Queue cleared");
            }
            
            if (cleared > 0)
            {
                Interlocked.Add(ref _totalDropped, cleared);
                Logger.Info($"Cleared {cleared} frames from queue");
            }
            
            return cleared;
        }
        
        /// <summary>
        /// Main processing loop
        /// </summary>
        private async Task ProcessingLoop()
        {
            var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _targetFps);
            var lastFrameTime = DateTime.Now;
            
            Logger.Info("Processing loop started");
            
            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var now = DateTime.Now;
                    
                    // Check if we have frames to process
                    if (_frameQueue.TryDequeue(out var frame))
                    {
                        _isRunning = true;
                        _lastActivity = now;
                        
                        // Ensure real-time mode is enabled
                        await EnsureRealtimeMode(true);
                        
                        // Process the frame
                        await ProcessFrame(frame);
                        
                        lastFrameTime = now;
                    }
                    else
                    {
                        _isRunning = false;
                        
                        // Check if we should disable real-time mode
                        if (IsRealTimeModeEnabled && (now - _lastActivity) > _realtimeTimeout)
                        {
                            await EnsureRealtimeMode(false);
                        }
                    }
                    
                    // Frame rate limiting
                    var elapsed = DateTime.Now - lastFrameTime;
                    var delay = frameInterval - elapsed;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, _cancellationTokenSource.Token);
                    }
                    else
                    {
                        // Yield control to prevent busy waiting
                        await Task.Delay(1, _cancellationTokenSource.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Processing loop cancelled");
            }
            catch (Exception ex)
            {
                Logger.Error($"Processing loop error: {ex.Message}", ex);
                OnServiceError(ex, "Processing loop error");
            }
            finally
            {
                Logger.Info("Processing loop stopped");
            }
        }
        
        /// <summary>
        /// Process a single frame
        /// </summary>
        private async Task ProcessFrame(JpegFrame frame)
        {
            try
            {
                var transferId = GetNextTransferId();
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
                    OnFrameDropped(frame, "All transmissions failed");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Frame processing error: {ex.Message}", ex);
                Interlocked.Increment(ref _totalDropped);
                OnFrameDropped(frame, ex.Message);
            }
        }
        
        /// <summary>
        /// Manage real-time mode
        /// </summary>
        private async Task EnsureRealtimeMode(bool enabled)
        {
            if (IsRealTimeModeEnabled == enabled) return;
            
            try
            {
                var results = await _hidDeviceService.SetRealTimeDisplayAsync(enabled);
                
                int successCount = 0;
                foreach (var result in results)
                {
                    if (result.Value) successCount++;
                }
                
                if (successCount > 0)
                {
                    OnRealTimeModeChanged(enabled, successCount, results.Count);
                    Logger.Info($"Real-time mode {(enabled ? "enabled" : "disabled")} on {successCount}/{results.Count} devices");
                }
                else
                {
                    Logger.Warn($"Failed to {(enabled ? "enable" : "disable")} real-time mode on any devices");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error managing real-time mode: {ex.Message}", ex);
                OnServiceError(ex, $"{(enabled ? "Enable" : "Disable")} real-time mode failed");
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
        
        // Event triggers
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
            
            Logger.Info("Disposing RealtimeJpegTransmissionService");
            
            try
            {
                // Stop processing
                _cancellationTokenSource.Cancel();
                
                // Disable real-time mode
                if (IsRealTimeModeEnabled)
                {
                    var task = EnsureRealtimeMode(false);
                    if (!task.Wait(TimeSpan.FromSeconds(2)))
                    {
                        Logger.Warn("Timeout disabling real-time mode during disposal");
                    }
                }
                
                // Clear remaining frames
                var clearedCount = ClearQueue();
                
                // Log final statistics
                var stats = Statistics;
                Logger.Info($"Service disposed. Final stats: Queued={stats.TotalQueued}, Sent={stats.TotalSent}, Dropped={stats.TotalDropped}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during disposal: {ex.Message}", ex);
            }
            finally
            {
                _cancellationTokenSource.Dispose();
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

/* 
Usage Examples:

// Basic usage (compatible with existing code)
var service = new RealtimeJpegTransmissionService(hidDeviceService);
service.QueueJpegData(jpegBytes, "MyFrame");

// Advanced usage with custom settings  
var service = new RealtimeJpegTransmissionService(
    hidDeviceService, 
    targetFps: 25,           // 25 FPS for smooth performance
    maxQueueSize: 3,         // Small queue for low latency
    realtimeTimeoutSeconds: 5 // 5 second timeout
);

// Monitor events
service.FrameProcessed += (s, e) => 
    Console.WriteLine($"Frame sent: {e.SuccessfulDevices}/{e.TotalDevices} devices");
    
service.FrameDropped += (s, e) => 
    Console.WriteLine($"Frame dropped: {e.Reason}");

// Check statistics
var stats = service.Statistics;
Console.WriteLine($"Success rate: {stats.SuccessRate:F1}%");

// Clean up
service.Dispose();
*/