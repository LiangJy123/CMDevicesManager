using CMDevicesManager.Helper;
using System;
using System.Threading.Tasks;

namespace CMDevicesManager.Services
{
    /// <summary>
    /// High-level service that integrates HidSwapChainService for real-time rendering applications.
    /// Provides easy-to-use interface for applications that need D3D-style frame presentation to HID devices.
    /// </summary>
    public class HidRealTimeRenderer : IDisposable
    {
        private readonly HidSwapChainService _swapChain;
        private bool _disposed = false;
        private bool _isRendering = false;
        
        // Statistics and monitoring
        private long _totalFramesRendered = 0;
        private DateTime _renderStartTime = DateTime.MinValue;
        
        // Events
        public event EventHandler<RenderFrameEventArgs>? FrameRendered;
        public event EventHandler<RenderErrorEventArgs>? RenderError;
        public event EventHandler<RenderStatisticsEventArgs>? StatisticsUpdated;
        
        /// <summary>
        /// Initialize the HID Real-Time Renderer with SwapChain
        /// </summary>
        /// <param name="hidDeviceService">HID device service</param>
        /// <param name="targetFps">Target frame rate (default: 30 FPS)</param>
        /// <param name="bufferCount">Number of frame buffers (2=double, 3=triple buffering)</param>
        /// <param name="enableVsync">Enable VSync presentation mode</param>
        public HidRealTimeRenderer(
            HidDeviceService hidDeviceService, 
            int targetFps = 30, 
            int bufferCount = 2,
            bool enableVsync = false)
        {
            var presentMode = enableVsync ? PresentMode.VSync : PresentMode.Immediate;
            
            _swapChain = new HidSwapChainService(
                hidDeviceService,
                bufferCount,
                SwapChainMode.Discard,
                presentMode,
                targetFps);
                
            // Subscribe to SwapChain events
            _swapChain.FramePresented += OnFramePresented;
            _swapChain.FrameDropped += OnFrameDropped;
            _swapChain.SwapChainError += OnSwapChainError;
            
            Logger.Info($"HidRealTimeRenderer created: {targetFps}FPS, {bufferCount} buffers, VSync={enableVsync}");
        }
        
        /// <summary>
        /// Initialize the renderer and start the SwapChain
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (_disposed)
            {
                Logger.Error("Cannot initialize disposed HidRealTimeRenderer");
                return false;
            }
            
            try
            {
                var success = await _swapChain.InitializeAsync();
                if (success)
                {
                    Logger.Info("HidRealTimeRenderer initialized successfully");
                }
                else
                {
                    Logger.Error("Failed to initialize HidRealTimeRenderer");
                }
                return success;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing HidRealTimeRenderer: {ex.Message}", ex);
                RenderError?.Invoke(this, new RenderErrorEventArgs(ex, "Initialization failed"));
                return false;
            }
        }
        
        /// <summary>
        /// Start real-time rendering loop
        /// </summary>
        public void StartRendering()
        {
            if (_disposed || _isRendering)
            {
                Logger.Warn("Cannot start rendering: already rendering or disposed");
                return;
            }
            
            try
            {
                _swapChain.StartPresentation();
                _isRendering = true;
                _renderStartTime = DateTime.Now;
                _totalFramesRendered = 0;
                
                Logger.Info("Real-time rendering started");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error starting rendering: {ex.Message}", ex);
                RenderError?.Invoke(this, new RenderErrorEventArgs(ex, "Start rendering failed"));
            }
        }
        
        /// <summary>
        /// Stop real-time rendering loop
        /// </summary>
        public void StopRendering()
        {
            if (!_isRendering)
            {
                return;
            }
            
            try
            {
                _swapChain.StopPresentation();
                _isRendering = false;
                
                var stats = GetRenderStatistics();
                Logger.Info($"Real-time rendering stopped. {stats.GetSummary()}");
                StatisticsUpdated?.Invoke(this, new RenderStatisticsEventArgs(stats));
            }
            catch (Exception ex)
            {
                Logger.Error($"Error stopping rendering: {ex.Message}", ex);
                RenderError?.Invoke(this, new RenderErrorEventArgs(ex, "Stop rendering failed"));
            }
        }
        
        /// <summary>
        /// Render a single frame using the SwapChain pattern
        /// </summary>
        /// <param name="frameData">JPEG or raw image data to render</param>
        /// <param name="metadata">Optional frame metadata</param>
        /// <returns>True if frame was successfully rendered and queued for presentation</returns>
        public async Task<bool> RenderFrameAsync(byte[] frameData, string? metadata = null)
        {
            if (_disposed || !_isRendering)
            {
                Logger.Warn("Cannot render frame: renderer not active");
                return false;
            }
            
            if (frameData == null || frameData.Length == 0)
            {
                Logger.Warn("Cannot render empty frame data");
                return false;
            }
            
            try
            {
                // Step 1: Get next available back buffer
                var backBuffer = _swapChain.GetNextBackBuffer();
                if (backBuffer == null)
                {
                    Logger.Warn("No back buffer available for rendering");
                    return false;
                }
                
                // Step 2: "Render" to the back buffer (copy frame data)
                backBuffer.SetData(frameData, metadata);
                
                // Step 3: Present the back buffer (queue for HID transmission)
                var presented = _swapChain.Present(backBuffer, priority: 0, metadata);
                
                if (presented)
                {
                    _totalFramesRendered++;
                    FrameRendered?.Invoke(this, new RenderFrameEventArgs(frameData.Length, metadata));
                    Logger.Info($"Frame rendered successfully: {frameData.Length:N0} bytes, Buffer {backBuffer.Index}");
                }
                else
                {
                    // Release buffer if presentation failed
                    _swapChain.ReleaseBuffer(backBuffer);
                    Logger.Warn("Failed to present frame");
                }
                
                return presented;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error rendering frame: {ex.Message}", ex);
                RenderError?.Invoke(this, new RenderErrorEventArgs(ex, "Frame rendering failed"));
                return false;
            }
        }
        
        /// <summary>
        /// Render frame with immediate presentation (bypass queue)
        /// </summary>
        public async Task<bool> RenderFrameImmediateAsync(byte[] frameData, string? metadata = null)
        {
            if (_disposed)
            {
                return false;
            }
            
            try
            {
                var backBuffer = _swapChain.GetNextBackBuffer();
                if (backBuffer == null)
                {
                    // Wait for buffer to become available
                    if (!_swapChain.WaitForAvailableBuffer(TimeSpan.FromMilliseconds(100)))
                    {
                        Logger.Warn("Timeout waiting for available buffer");
                        return false;
                    }
                    backBuffer = _swapChain.GetNextBackBuffer();
                    if (backBuffer == null)
                    {
                        return false;
                    }
                }
                
                backBuffer.SetData(frameData, metadata);
                
                var success = await _swapChain.PresentImmediateAsync(backBuffer, metadata);
                if (success)
                {
                    _totalFramesRendered++;
                    FrameRendered?.Invoke(this, new RenderFrameEventArgs(frameData.Length, metadata));
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in immediate frame render: {ex.Message}", ex);
                RenderError?.Invoke(this, new RenderErrorEventArgs(ex, "Immediate rendering failed"));
                return false;
            }
        }
        
        /// <summary>
        /// Get comprehensive rendering statistics
        /// </summary>
        public RenderingStatistics GetRenderStatistics()
        {
            var swapChainStats = _swapChain.GetStatistics();
            var renderTime = _renderStartTime != DateTime.MinValue ? DateTime.Now - _renderStartTime : TimeSpan.Zero;
            
            return new RenderingStatistics
            {
                // Renderer-specific stats
                TotalFramesRendered = _totalFramesRendered,
                RenderingDuration = renderTime,
                AverageRenderFps = renderTime.TotalSeconds > 0 ? _totalFramesRendered / renderTime.TotalSeconds : 0,
                IsRendering = _isRendering,
                
                // SwapChain stats
                SwapChainStats = swapChainStats,
                
                // Calculated metrics
                OverallSuccessRate = swapChainStats.PresentSuccessRate,
                EffectiveFrameRate = swapChainStats.EffectiveFrameRate,
                BufferUtilization = swapChainStats.BufferCount > 0 ? 
                    (double)(swapChainStats.RenderingBuffers + swapChainStats.PendingBuffers) / swapChainStats.BufferCount * 100 : 0
            };
        }
        
        /// <summary>
        /// Get current SwapChain status
        /// </summary>
        public string GetStatusReport()
        {
            var stats = GetRenderStatistics();
            return $"HidRealTimeRenderer Status: " +
                   $"Rendering={_isRendering}, " +
                   $"Frames={_totalFramesRendered}, " +
                   $"FPS={stats.EffectiveFrameRate:F1}, " +
                   $"Success={stats.OverallSuccessRate:F1}%, " +
                   $"BufferUtil={stats.BufferUtilization:F1}%";
        }
        
        #region Event Handlers
        
        private void OnFramePresented(object? sender, FramePresentedEventArgs e)
        {
            Logger.Info($"Frame presented: Buffer {e.Buffer.Index}, TransferID {e.TransferId}");
        }
        
        private void OnFrameDropped(object? sender, FrameDroppedEventArgs e)
        {
            Logger.Warn($"Frame dropped: Buffer {e.Buffer.Index}, Reason: {e.Reason}");
        }
        
        private void OnSwapChainError(object? sender, SwapChainErrorEventArgs e)
        {
            Logger.Error($"SwapChain error: {e.Context} - {e.Exception.Message}", e.Exception);
            RenderError?.Invoke(this, new RenderErrorEventArgs(e.Exception, $"SwapChain error: {e.Context}"));
        }
        
        #endregion
        
        /// <summary>
        /// Dispose the renderer and cleanup resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
                
            Logger.Info("Disposing HidRealTimeRenderer");
            
            try
            {
                StopRendering();
                _swapChain?.Dispose();
                
                _disposed = true;
                Logger.Info("HidRealTimeRenderer disposed successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error disposing HidRealTimeRenderer: {ex.Message}", ex);
            }
        }
    }
    
    #region Supporting Classes
    
    /// <summary>
    /// Comprehensive rendering statistics
    /// </summary>
    public class RenderingStatistics
    {
        // Renderer stats
        public long TotalFramesRendered { get; set; }
        public TimeSpan RenderingDuration { get; set; }
        public double AverageRenderFps { get; set; }
        public bool IsRendering { get; set; }
        
        // SwapChain stats
        public SwapChainStatistics SwapChainStats { get; set; } = new SwapChainStatistics();
        
        // Calculated metrics
        public double OverallSuccessRate { get; set; }
        public double EffectiveFrameRate { get; set; }
        public double BufferUtilization { get; set; }
        
        public string GetSummary()
        {
            return $"Rendered {TotalFramesRendered} frames in {RenderingDuration:hh\\:mm\\:ss}, " +
                   $"Avg FPS: {AverageRenderFps:F1}, Success: {OverallSuccessRate:F1}%";
        }
        
        public string GetDetailedReport()
        {
            return $"=== HID Real-Time Renderer Statistics ===\n" +
                   $"Rendering Active: {IsRendering}\n" +
                   $"Total Frames Rendered: {TotalFramesRendered:N0}\n" +
                   $"Rendering Duration: {RenderingDuration:hh\\:mm\\:ss}\n" +
                   $"Average Render FPS: {AverageRenderFps:F2}\n" +
                   $"Effective Frame Rate: {EffectiveFrameRate:F2}\n" +
                   $"Overall Success Rate: {OverallSuccessRate:F1}%\n" +
                   $"Buffer Utilization: {BufferUtilization:F1}%\n" +
                   $"\n=== SwapChain Details ===\n" +
                   $"{SwapChainStats.GetPerformanceReport()}";
        }
    }
    
    #endregion
    
    #region Event Args Classes
    
    /// <summary>
    /// Event arguments for frame rendered events
    /// </summary>
    public class RenderFrameEventArgs : EventArgs
    {
        public int FrameSize { get; }
        public string? Metadata { get; }
        public DateTime RenderTime { get; }
        
        public RenderFrameEventArgs(int frameSize, string? metadata)
        {
            FrameSize = frameSize;
            Metadata = metadata;
            RenderTime = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for render error events
    /// </summary>
    public class RenderErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public string Context { get; }
        public DateTime ErrorTime { get; }
        
        public RenderErrorEventArgs(Exception exception, string context)
        {
            Exception = exception;
            Context = context;
            ErrorTime = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Event arguments for statistics updated events
    /// </summary>
    public class RenderStatisticsEventArgs : EventArgs
    {
        public RenderingStatistics Statistics { get; }
        public DateTime UpdateTime { get; }
        
        public RenderStatisticsEventArgs(RenderingStatistics statistics)
        {
            Statistics = statistics;
            UpdateTime = DateTime.Now;
        }
    }
    
    #endregion
}