using CMDevicesManager.Helper;
using CMDevicesManager.Services;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// HID SwapChain Service - Implements D3D-style swapchain logic for HID device data transmission.
    /// Features double/triple buffering, present operations, frame synchronization, and VSync-like timing.
    /// </summary>
    public class HidSwapChainService : IDisposable
    {
        private readonly HidDeviceService _hidDeviceService;
        private readonly object _lockObject = new object();
        private readonly object _bufferLock = new object();
        
        private bool _disposed = false;
        private bool _isInitialized = false;
        private bool _isPresentationActive = false;
        
        // SwapChain Configuration
        private readonly int _bufferCount;
        private readonly SwapChainMode _swapChainMode;
        private readonly PresentMode _presentMode;
        private readonly int _targetRefreshRate;
        
        // Frame Buffers (D3D-style)
        private FrameBuffer[] _frameBuffers;
        private int _currentBackBufferIndex = 0;
        private int _frontBufferIndex = -1; // -1 means no presented frame
        
        // Present Queue and Timing
        private readonly ConcurrentQueue<PresentRequest> _presentQueue;
        private readonly System.Threading.Timer _presentTimer;
        private readonly System.Threading.Timer _vsyncTimer;
        
        // Transfer ID Management (HID-specific)
        private byte _currentTransferId = 1;
        private readonly int _transferIdRange = 59; // 1-59 range as per HID protocol
        
        // Performance Tracking
        private DateTime _lastPresentTime = DateTime.MinValue;
        private long _totalFramesPresented = 0;
        private long _totalFramesDropped = 0;
        private long _totalPresentAttempts = 0;
        private double _averageFrameTime = 0.0;
        
        // Synchronization
        private readonly SemaphoreSlim _presentSemaphore;
        private readonly ManualResetEventSlim _bufferAvailableEvent;
        
        // Events (D3D-inspired)
        public event EventHandler<FramePresentedEventArgs>? FramePresented;
        public event EventHandler<FrameDroppedEventArgs>? FrameDropped;
        public event EventHandler<SwapChainErrorEventArgs>? SwapChainError;
        public event EventHandler<BufferStatusChangedEventArgs>? BufferStatusChanged;
        public event EventHandler<VsyncEventArgs>? VsyncOccurred;
        
        /// <summary>
        /// Initialize HID SwapChain Service with D3D-style configuration
        /// </summary>
        /// <param name="hidDeviceService">HID device service instance</param>
        /// <param name="bufferCount">Number of back buffers (2=double buffering, 3=triple buffering)</param>
        /// <param name="swapChainMode">SwapChain presentation mode</param>
        /// <param name="presentMode">Present synchronization mode</param>
        /// <param name="targetRefreshRate">Target refresh rate in Hz (default: 30 Hz)</param>
        public HidSwapChainService(
            HidDeviceService hidDeviceService,
            int bufferCount = 2,
            SwapChainMode swapChainMode = SwapChainMode.Discard,
            PresentMode presentMode = PresentMode.Immediate,
            int targetRefreshRate = 30)
        {
            _hidDeviceService = hidDeviceService ?? throw new ArgumentNullException(nameof(hidDeviceService));
            _bufferCount = Math.Clamp(bufferCount, 2, 4); // Support 2-4 buffers
            _swapChainMode = swapChainMode;
            _presentMode = presentMode;
            _targetRefreshRate = Math.Clamp(targetRefreshRate, 1, 120);
            
            _frameBuffers = new FrameBuffer[_bufferCount];
            _presentQueue = new ConcurrentQueue<PresentRequest>();
            _presentSemaphore = new SemaphoreSlim(1, 1);
            _bufferAvailableEvent = new ManualResetEventSlim(true);
            
            // Calculate frame timing
            var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _targetRefreshRate);
            
            // Create present timer (handles frame presentation)
            _presentTimer = new System.Threading.Timer(PresentTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
            
            // Create VSync timer (simulates display refresh)
            _vsyncTimer = new System.Threading.Timer(VsyncTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
            
            Logger.Info($"HidSwapChainService initialized: Buffers={_bufferCount}, Mode={_swapChainMode}, " +
                       $"Present={_presentMode}, RefreshRate={_targetRefreshRate}Hz");
        }
        
        /// <summary>
        /// Initialize the SwapChain and enable real-time mode on HID devices
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (_disposed)
            {
                Logger.Error("Cannot initialize disposed HidSwapChainService");
                return false;
            }
            
            lock (_lockObject)
            {
                if (_isInitialized)
                {
                    Logger.Warn("HidSwapChainService already initialized");
                    return true;
                }
            }
            
            try
            {
                // Initialize frame buffers
                InitializeFrameBuffers();
                
                // Enable real-time mode on HID devices (similar to setting display mode)
                var realTimeResults = await _hidDeviceService.SetRealTimeDisplayAsync(true);
                var enabledDevices = 0;
                foreach (var result in realTimeResults)
                {
                    if (result.Value) enabledDevices++;
                }
                
                if (enabledDevices == 0)
                {
                    Logger.Warn("No HID devices enabled for real-time mode");
                    return false;
                }
                
                lock (_lockObject)
                {
                    _isInitialized = true;
                }
                
                Logger.Info($"HidSwapChain initialized successfully - {enabledDevices} devices enabled for real-time mode");
                BufferStatusChanged?.Invoke(this, new BufferStatusChangedEventArgs(_bufferCount, 0, enabledDevices));
                
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize HidSwapChainService: {ex.Message}", ex);
                SwapChainError?.Invoke(this, new SwapChainErrorEventArgs(ex, "Initialization failed"));
                return false;
            }
        }
        
        /// <summary>
        /// Start the SwapChain presentation loop (similar to D3D Present calls)
        /// </summary>
        public void StartPresentation()
        {
            if (_disposed || !_isInitialized)
            {
                Logger.Error("Cannot start presentation: service not initialized or disposed");
                return;
            }
            
            lock (_lockObject)
            {
                if (_isPresentationActive)
                {
                    Logger.Warn("Presentation already active");
                    return;
                }
                
                _isPresentationActive = true;
            }
            
            var frameInterval = (int)(1000.0 / _targetRefreshRate);
            
            // Start present timer
            _presentTimer.Change(0, frameInterval);
            
            // Start VSync timer (offset slightly to simulate real display timing)
            if (_presentMode == PresentMode.VSync)
            {
                _vsyncTimer.Change(frameInterval / 2, frameInterval);
            }
            
            Logger.Info($"SwapChain presentation started - {_targetRefreshRate}Hz refresh rate");
        }
        
        /// <summary>
        /// Stop the SwapChain presentation loop
        /// </summary>
        public void StopPresentation()
        {
            lock (_lockObject)
            {
                if (!_isPresentationActive)
                {
                    return;
                }
                
                _isPresentationActive = false;
            }
            
            // Stop timers
            _presentTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _vsyncTimer.Change(Timeout.Infinite, Timeout.Infinite);
            
            // Clear presentation queue
            while (_presentQueue.TryDequeue(out _)) { }
            
            Logger.Info("SwapChain presentation stopped");
        }
        
        /// <summary>
        /// Get next available back buffer for rendering (similar to IDXGISwapChain::GetBuffer)
        /// </summary>
        /// <returns>Frame buffer for rendering, or null if no buffer available</returns>
        public FrameBuffer? GetNextBackBuffer()
        {
            if (_disposed || !_isInitialized)
            {
                return null;
            }
            
            lock (_bufferLock)
            {
                // Find next available back buffer
                for (int attempts = 0; attempts < _bufferCount; attempts++)
                {
                    var bufferIndex = (_currentBackBufferIndex + attempts) % _bufferCount;
                    var buffer = _frameBuffers[bufferIndex];
                    
                    if (buffer.State == BufferState.Available)
                    {
                        buffer.State = BufferState.Rendering;
                        buffer.AcquisitionTime = DateTime.Now;
                        _currentBackBufferIndex = bufferIndex;
                        
                        Logger.Info($"Acquired back buffer {bufferIndex} for rendering");
                        return buffer;
                    }
                }
                
                // No buffer available - handle based on swap chain mode
                HandleBufferStarvation();
                return null;
            }
        }
        
        /// <summary>
        /// Present the current back buffer (similar to IDXGISwapChain::Present)
        /// </summary>
        /// <param name="buffer">Frame buffer to present</param>
        /// <param name="priority">Present priority (higher = more important)</param>
        /// <param name="metadata">Optional metadata for debugging</param>
        /// <returns>True if successfully queued for presentation</returns>
        public bool Present(FrameBuffer buffer, int priority = 0, string? metadata = null)
        {
            if (_disposed || !_isInitialized || !_isPresentationActive)
            {
                return false;
            }
            
            if (buffer == null || buffer.Data == null || buffer.Data.Length == 0)
            {
                Logger.Warn("Cannot present null or empty buffer");
                return false;
            }
            
            lock (_bufferLock)
            {
                if (buffer.State != BufferState.Rendering)
                {
                    Logger.Warn($"Buffer {buffer.Index} not in rendering state - cannot present");
                    return false;
                }
                
                // Mark buffer as pending presentation
                buffer.State = BufferState.PendingPresent;
                buffer.PresentationTime = DateTime.Now;
            }
            
            // Create present request
            var presentRequest = new PresentRequest
            {
                Buffer = buffer,
                Priority = priority,
                Metadata = metadata,
                RequestTime = DateTime.Now,
                TransferId = GetNextTransferId()
            };
            
            // Queue for presentation
            _presentQueue.Enqueue(presentRequest);
            Interlocked.Increment(ref _totalPresentAttempts);
            
            Logger.Info($"Queued buffer {buffer.Index} for presentation (Priority: {priority}, TransferID: {presentRequest.TransferId})");
            return true;
        }
        
        /// <summary>
        /// Force immediate presentation of buffer (bypass queue)
        /// </summary>
        public async Task<bool> PresentImmediateAsync(FrameBuffer buffer, string? metadata = null)
        {
            if (_disposed || !_isInitialized)
            {
                return false;
            }
            
            try
            {
                await _presentSemaphore.WaitAsync();
                
                var transferId = GetNextTransferId();
                var success = await PresentBufferToDevicesAsync(buffer, transferId, metadata);
                
                if (success)
                {
                    lock (_bufferLock)
                    {
                        buffer.State = BufferState.Presented;
                        _frontBufferIndex = buffer.Index;
                    }
                    
                    Interlocked.Increment(ref _totalFramesPresented);
                    UpdateFrameTimingStats();
                    
                    FramePresented?.Invoke(this, new FramePresentedEventArgs(buffer, transferId, metadata));
                }
                
                return success;
            }
            finally
            {
                _presentSemaphore.Release();
            }
        }
        
        /// <summary>
        /// Release a frame buffer back to available state (similar to releasing D3D resources)
        /// </summary>
        public void ReleaseBuffer(FrameBuffer buffer)
        {
            if (buffer == null) return;
            
            lock (_bufferLock)
            {
                buffer.State = BufferState.Available;
                buffer.Data = null; // Clear data to free memory
                buffer.ReleaseTime = DateTime.Now;
                
                Logger.Info($"Released buffer {buffer.Index}");
                
                // Signal that a buffer is now available
                _bufferAvailableEvent.Set();
            }
        }
        
        /// <summary>
        /// Wait for buffer to become available (with timeout)
        /// </summary>
        public bool WaitForAvailableBuffer(TimeSpan timeout)
        {
            return _bufferAvailableEvent.Wait(timeout);
        }
        
        /// <summary>
        /// Get SwapChain performance statistics
        /// </summary>
        public SwapChainStatistics GetStatistics()
        {
            lock (_bufferLock)
            {
                var availableBuffers = 0;
                var renderingBuffers = 0;
                var pendingBuffers = 0;
                
                for (int i = 0; i < _frameBuffers.Length; i++)
                {
                    switch (_frameBuffers[i].State)
                    {
                        case BufferState.Available: availableBuffers++; break;
                        case BufferState.Rendering: renderingBuffers++; break;
                        case BufferState.PendingPresent: pendingBuffers++; break;
                    }
                }
                
                return new SwapChainStatistics
                {
                    TotalFramesPresented = _totalFramesPresented,
                    TotalFramesDropped = _totalFramesDropped,
                    TotalPresentAttempts = _totalPresentAttempts,
                    AverageFrameTime = _averageFrameTime,
                    CurrentRefreshRate = _targetRefreshRate,
                    BufferCount = _bufferCount,
                    AvailableBuffers = availableBuffers,
                    RenderingBuffers = renderingBuffers,
                    PendingBuffers = pendingBuffers,
                    QueuedPresentRequests = _presentQueue.Count,
                    FrontBufferIndex = _frontBufferIndex,
                    BackBufferIndex = _currentBackBufferIndex,
                    SwapChainMode = _swapChainMode,
                    PresentMode = _presentMode,
                    IsInitialized = _isInitialized,
                    IsPresentationActive = _isPresentationActive
                };
            }
        }
        
        #region Private Methods
        
        private void InitializeFrameBuffers()
        {
            for (int i = 0; i < _bufferCount; i++)
            {
                _frameBuffers[i] = new FrameBuffer
                {
                    Index = i,
                    State = BufferState.Available,
                    CreationTime = DateTime.Now
                };
            }
            
            Logger.Info($"Initialized {_bufferCount} frame buffers");
        }
        
        private async void PresentTimerCallback(object? state)
        {
            if (_disposed || !_isPresentationActive)
                return;
            
            try
            {
                // Process presentation queue
                if (_presentQueue.TryDequeue(out var presentRequest))
                {
                    await ProcessPresentRequestAsync(presentRequest);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in present timer callback: {ex.Message}", ex);
                SwapChainError?.Invoke(this, new SwapChainErrorEventArgs(ex, "Present timer error"));
            }
        }
        
        private void VsyncTimerCallback(object? state)
        {
            if (_disposed || !_isPresentationActive || _presentMode != PresentMode.VSync)
                return;
            
            try
            {
                // Simulate VSync signal
                VsyncOccurred?.Invoke(this, new VsyncEventArgs(DateTime.Now, _targetRefreshRate));
                
                // In VSync mode, process present queue at VSync intervals
                if (_presentMode == PresentMode.VSync)
                {
                    Task.Run(async () =>
                    {
                        if (_presentQueue.TryDequeue(out var presentRequest))
                        {
                            await ProcessPresentRequestAsync(presentRequest);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in VSync timer callback: {ex.Message}", ex);
            }
        }
        
        private async Task ProcessPresentRequestAsync(PresentRequest request)
        {
            try
            {
                await _presentSemaphore.WaitAsync();
                
                var success = await PresentBufferToDevicesAsync(request.Buffer, request.TransferId, request.Metadata);
                
                lock (_bufferLock)
                {
                    if (success)
                    {
                        request.Buffer.State = BufferState.Presented;
                        _frontBufferIndex = request.Buffer.Index;
                        
                        Interlocked.Increment(ref _totalFramesPresented);
                        UpdateFrameTimingStats();
                        
                        FramePresented?.Invoke(this, new FramePresentedEventArgs(request.Buffer, request.TransferId, request.Metadata));
                    }
                    else
                    {
                        request.Buffer.State = BufferState.Available;
                        Interlocked.Increment(ref _totalFramesDropped);
                        
                        FrameDropped?.Invoke(this, new FrameDroppedEventArgs(request.Buffer, "HID transmission failed", request.Metadata));
                    }
                    
                    // Signal buffer availability
                    _bufferAvailableEvent.Set();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing present request: {ex.Message}", ex);
                SwapChainError?.Invoke(this, new SwapChainErrorEventArgs(ex, "Present processing error"));
            }
            finally
            {
                _presentSemaphore.Release();
            }
        }
        
        private async Task<bool> PresentBufferToDevicesAsync(FrameBuffer buffer, byte transferId, string? metadata)
        {
            try
            {
                if (buffer.Data == null || buffer.Data.Length == 0)
                {
                    Logger.Warn("Cannot present buffer with no data");
                    return false;
                }
                
                // Send data to HID devices using TransferDataAsync
                var results = await _hidDeviceService.TransferDataAsync(buffer.Data, transferId);
                
                var successCount = 0;
                foreach (var result in results)
                {
                    if (result.Value) successCount++;
                }
                
                var success = successCount > 0;
                
                Logger.Info($"Presented buffer {buffer.Index} to {successCount}/{results.Count} devices " +
                           $"(TransferID: {transferId}, Size: {buffer.Data.Length:N0} bytes)" +
                           (string.IsNullOrEmpty(metadata) ? "" : $", Metadata: {metadata}"));
                
                return success;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to present buffer to HID devices: {ex.Message}", ex);
                return false;
            }
        }
        
        private void HandleBufferStarvation()
        {
            switch (_swapChainMode)
            {
                case SwapChainMode.Discard:
                    // Force oldest buffer to available state
                    var oldestBuffer = FindOldestBuffer();
                    if (oldestBuffer != null)
                    {
                        Logger.Warn($"Buffer starvation - discarding buffer {oldestBuffer.Index}");
                        oldestBuffer.State = BufferState.Available;
                        Interlocked.Increment(ref _totalFramesDropped);
                    }
                    break;
                    
                case SwapChainMode.Sequential:
                    // Wait for next buffer in sequence
                    Logger.Info("Buffer starvation - waiting for sequential buffer");
                    break;
                    
                case SwapChainMode.FlipDiscard:
                    // Similar to discard but with different semantics
                    Logger.Warn("Buffer starvation in flip discard mode");
                    break;
            }
        }
        
        private FrameBuffer? FindOldestBuffer()
        {
            FrameBuffer? oldest = null;
            DateTime oldestTime = DateTime.MaxValue;
            
            foreach (var buffer in _frameBuffers)
            {
                if (buffer.State != BufferState.Available && buffer.AcquisitionTime < oldestTime)
                {
                    oldest = buffer;
                    oldestTime = buffer.AcquisitionTime;
                }
            }
            
            return oldest;
        }
        
        private byte GetNextTransferId()
        {
            var id = _currentTransferId;
            _currentTransferId++;
            if (_currentTransferId > _transferIdRange)
                _currentTransferId = 1;
            return id;
        }
        
        private void UpdateFrameTimingStats()
        {
            var currentTime = DateTime.Now;
            if (_lastPresentTime != DateTime.MinValue)
            {
                var frameTime = (currentTime - _lastPresentTime).TotalMilliseconds;
                _averageFrameTime = (_averageFrameTime * 0.9) + (frameTime * 0.1); // Moving average
            }
            _lastPresentTime = currentTime;
        }
        
        #endregion
        
        /// <summary>
        /// Dispose the SwapChain service and cleanup resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            
            Logger.Info("Disposing HidSwapChainService");
            
            try
            {
                // Stop presentation
                StopPresentation();
                
                // Disable real-time mode on HID devices
                if (_isInitialized)
                {
                    var disableTask = Task.Run(async () =>
                    {
                        try
                        {
                            await _hidDeviceService.SetRealTimeDisplayAsync(false);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error disabling real-time mode during disposal: {ex.Message}", ex);
                        }
                    });
                    
                    if (!disableTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        Logger.Warn("Timeout waiting for real-time mode disable during disposal");
                    }
                }
                
                // Dispose timers
                _presentTimer?.Dispose();
                _vsyncTimer?.Dispose();
                
                // Dispose synchronization objects
                _presentSemaphore?.Dispose();
                _bufferAvailableEvent?.Dispose();
                
                // Clear frame buffers
                if (_frameBuffers != null)
                {
                    foreach (var buffer in _frameBuffers)
                    {
                        buffer?.Dispose();
                    }
                }
                
                _disposed = true;
                
                var finalStats = GetStatistics();
                Logger.Info($"HidSwapChainService disposed. Final stats: " +
                          $"Presented={finalStats.TotalFramesPresented}, " +
                          $"Dropped={finalStats.TotalFramesDropped}, " +
                          $"AvgFrameTime={finalStats.AverageFrameTime:F2}ms");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during HidSwapChainService disposal: {ex.Message}", ex);
            }
        }
    }
    
    #region Supporting Classes and Enums
    
    /// <summary>
    /// SwapChain modes (similar to DXGI swap chain modes)
    /// </summary>
    public enum SwapChainMode
    {
        /// <summary>
        /// Discard back buffer contents after presenting (most efficient)
        /// </summary>
        Discard,
        
        /// <summary>
        /// Present buffers in sequential order
        /// </summary>
        Sequential,
        
        /// <summary>
        /// Flip and discard (modern D3D12 style)
        /// </summary>
        FlipDiscard
    }
    
    /// <summary>
    /// Present modes (synchronization behavior)
    /// </summary>
    public enum PresentMode
    {
        /// <summary>
        /// Present immediately without waiting for VSync
        /// </summary>
        Immediate,
        
        /// <summary>
        /// Synchronize with display refresh rate (VSync)
        /// </summary>
        VSync,
        
        /// <summary>
        /// Adaptive sync (fallback to immediate if can't sync)
        /// </summary>
        Adaptive
    }
    
    /// <summary>
    /// Frame buffer states (similar to D3D resource states)
    /// </summary>
    public enum BufferState
    {
        Available,
        Rendering,
        PendingPresent,
        Presented
    }
    
    /// <summary>
    /// Frame buffer representing a renderable surface
    /// </summary>
    public class FrameBuffer : IDisposable
    {
        public int Index { get; set; }
        public BufferState State { get; set; } = BufferState.Available;
        public byte[]? Data { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime AcquisitionTime { get; set; }
        public DateTime PresentationTime { get; set; }
        public DateTime ReleaseTime { get; set; }
        public string? Metadata { get; set; }
        
        public void SetData(byte[] data, string? metadata = null)
        {
            Data = data;
            Metadata = metadata;
        }
        
        public void Dispose()
        {
            Data = null;
            Metadata = null;
        }
    }
    
    /// <summary>
    /// Present request for queuing frame presentations
    /// </summary>
    public class PresentRequest
    {
        public FrameBuffer Buffer { get; set; } = null!;
        public int Priority { get; set; }
        public string? Metadata { get; set; }
        public DateTime RequestTime { get; set; }
        public byte TransferId { get; set; }
    }
    
    /// <summary>
    /// SwapChain performance and status statistics
    /// </summary>
    public class SwapChainStatistics
    {
        public long TotalFramesPresented { get; set; }
        public long TotalFramesDropped { get; set; }
        public long TotalPresentAttempts { get; set; }
        public double AverageFrameTime { get; set; }
        public int CurrentRefreshRate { get; set; }
        public int BufferCount { get; set; }
        public int AvailableBuffers { get; set; }
        public int RenderingBuffers { get; set; }
        public int PendingBuffers { get; set; }
        public int QueuedPresentRequests { get; set; }
        public int FrontBufferIndex { get; set; }
        public int BackBufferIndex { get; set; }
        public SwapChainMode SwapChainMode { get; set; }
        public PresentMode PresentMode { get; set; }
        public bool IsInitialized { get; set; }
        public bool IsPresentationActive { get; set; }
        
        public double PresentSuccessRate => TotalPresentAttempts > 0 ? 
            (double)TotalFramesPresented / TotalPresentAttempts * 100 : 0;
        
        public double DropRate => TotalPresentAttempts > 0 ? 
            (double)TotalFramesDropped / TotalPresentAttempts * 100 : 0;
        
        public double EffectiveFrameRate => AverageFrameTime > 0 ? 1000.0 / AverageFrameTime : 0;
        
        public string GetPerformanceReport()
        {
            return $"SwapChain Performance: " +
                   $"Presented={TotalFramesPresented}, " +
                   $"Dropped={TotalFramesDropped}, " +
                   $"Success={PresentSuccessRate:F1}%, " +
                   $"EffectiveFPS={EffectiveFrameRate:F1}, " +
                   $"AvgFrameTime={AverageFrameTime:F2}ms, " +
                   $"Buffers={AvailableBuffers}/{BufferCount} available";
        }
    }
    
    #endregion
    
    #region Event Args Classes
    
    /// <summary>
    /// Event arguments for frame presented events
    /// </summary>
    public class FramePresentedEventArgs : EventArgs
    {
        public FrameBuffer Buffer { get; }
        public byte TransferId { get; }
        public string? Metadata { get; }
        public DateTime PresentTime { get; }
        
        public FramePresentedEventArgs(FrameBuffer buffer, byte transferId, string? metadata)
        {
            Buffer = buffer;
            TransferId = transferId;
            Metadata = metadata;
            PresentTime = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for frame dropped events
    /// </summary>
    public class FrameDroppedEventArgs : EventArgs
    {
        public FrameBuffer Buffer { get; }
        public string Reason { get; }
        public string? Metadata { get; }
        public DateTime DropTime { get; }
        
        public FrameDroppedEventArgs(FrameBuffer buffer, string reason, string? metadata)
        {
            Buffer = buffer;
            Reason = reason;
            Metadata = metadata;
            DropTime = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for SwapChain errors
    /// </summary>
    public class SwapChainErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public string Context { get; }
        public DateTime ErrorTime { get; }
        
        public SwapChainErrorEventArgs(Exception exception, string context)
        {
            Exception = exception;
            Context = context;
            ErrorTime = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for buffer status changes
    /// </summary>
    public class BufferStatusChangedEventArgs : EventArgs
    {
        public int TotalBuffers { get; }
        public int AvailableBuffers { get; }
        public int ConnectedDevices { get; }
        public DateTime StatusTime { get; }
        
        public BufferStatusChangedEventArgs(int totalBuffers, int availableBuffers, int connectedDevices)
        {
            TotalBuffers = totalBuffers;
            AvailableBuffers = availableBuffers;
            ConnectedDevices = connectedDevices;
            StatusTime = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for VSync events
    /// </summary>
    public class VsyncEventArgs : EventArgs
    {
        public DateTime VsyncTime { get; }
        public int RefreshRate { get; }
        
        public VsyncEventArgs(DateTime vsyncTime, int refreshRate)
        {
            VsyncTime = vsyncTime;
            RefreshRate = refreshRate;
        }
    }
    
    #endregion
}