using System.Collections.Concurrent;
using System.IO.Ports;
using ATEQ.LeakTest.Web.Models;

namespace ATEQ.LeakTest.Web.Services;

public class ScannerService
{
    private const int ScanIdleFlushMs = 80;
    private const int DebugChunkLimit = 20;
    private readonly FeatureFlags _flags;

    public ScannerService(FeatureFlags flags) { _flags = flags; }

    private SerialPort? _port;
    private CommConfig? _currentConfig;
    private CancellationTokenSource? _readCts;
    private readonly object _lock = new();

    // Scan state
    private ScannerEvent? _latestScan;
    private string? _consumedScanId;
    private string _buffer = "";

    // Debug state
    private long _bytesReceived;
    private long _chunksReceived;
    private string? _lastChunkAt;
    private string? _lastPublishedAt;
    private object? _modemSignals;
    private string _bufferPreview = "";
    private readonly ConcurrentQueue<ChunkRecord> _recentChunks = new();

    public Func<ScannerEvent, Task>? OnScan { get; set; }
    public bool IsMockConnected { get; private set; }
    public bool IsKeyboardWedge { get; private set; }

    // ==================== Configure ====================

    public async Task<object> ConfigureAsync(CommConfig config)
    {
        lock (_lock)
        {
            _currentConfig = new CommConfig
            {
                ComPort = config.ComPort,
                Baudrate = config.Baudrate,
                DataBits = config.DataBits,
                Parity = config.Parity?.ToLower() ?? "none",
                StopBits = config.StopBits,
                TimeoutMs = config.TimeoutMs,
                Dtr = config.Dtr,
                Rts = config.Rts,
                Enabled = config.Enabled
            };

            if (!_currentConfig.Enabled)
            {
                Disconnect();
                IsMockConnected = false;
                IsKeyboardWedge = false;
                return Task.FromResult((object)new { connected = false, enabled = false });
            }

            // Keyboard wedge mode: skip serial, mark ready for USB HID input
            if (string.Equals(_currentConfig.ComPort, "KEYBOARD_WEDGE", StringComparison.OrdinalIgnoreCase))
            {
                Disconnect();
                IsMockConnected = false;
                IsKeyboardWedge = true;
                ResetDebugState();
                Console.WriteLine("[scanner] keyboard wedge mode enabled");
                return Task.FromResult((object)new { connected = true, enabled = true });
            }

            // Mock mode: skip serial, mark connected
            if (string.Equals(_currentConfig.ComPort, "MOCK_SCANNER", StringComparison.OrdinalIgnoreCase))
            {
                if (!_flags.EnableMockMode)
                    throw new InvalidOperationException("Mock mode is not enabled. Cannot use MOCK_SCANNER.");

                Disconnect();
                IsMockConnected = true;
                ResetDebugState();
                Console.WriteLine("[scanner] mock scanner enabled");
                return Task.FromResult((object)new { connected = true, enabled = true });
            }

            IsMockConnected = false;
            IsKeyboardWedge = false;
            ResetDebugState();
        }

        await ConnectAsync();
        return new { connected = IsConnected, enabled = true };
    }

    private async Task ConnectAsync()
    {
        if (_currentConfig == null) return;

        await DisconnectAsync();
        var parity = _currentConfig.Parity?.ToLower() switch
        {
            "even" => Parity.Even, "odd" => Parity.Odd,
            "mark" => Parity.Mark, "space" => Parity.Space,
            _ => Parity.None
        };
        var stopBits = _currentConfig.StopBits switch
        {
            1 => StopBits.One, 1.5 => StopBits.OnePointFive, 2 => StopBits.Two, _ => StopBits.One
        };

        _port = new SerialPort(_currentConfig.ComPort, _currentConfig.Baudrate, parity, _currentConfig.DataBits, stopBits)
        {
            ReadTimeout = _currentConfig.TimeoutMs,
            WriteTimeout = _currentConfig.TimeoutMs
        };
        _port.Open();

        ApplyLineSignals();
        _readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoop(_readCts.Token));

        Console.WriteLine($"[scanner] connected {_currentConfig.ComPort}");
    }

    private async Task ReadLoop(CancellationToken ct)
    {
        var flushTimer = (Timer?)null;
        try
        {
            while (!ct.IsCancellationRequested && _port?.IsOpen == true)
            {
                try
                {
                    if (_port.BytesToRead > 0)
                    {
                        var buf = new byte[Math.Min(_port.BytesToRead, 4096)];
                        var read = _port.Read(buf, 0, buf.Length);
                        if (read > 0)
                            HandleIncomingChunk(buf.Take(read).ToArray(), ref flushTimer);
                    }
                }
                catch (TimeoutException) { /* no data, continue */ }
                catch (ObjectDisposedException) { break; }
                catch (IOException) { break; }
                catch (InvalidOperationException) { break; }
                await Task.Delay(10, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            flushTimer?.Dispose();
        }
    }

    private void HandleIncomingChunk(byte[] chunk, ref Timer? flushTimer)
    {
        var textChunk = System.Text.Encoding.UTF8.GetString(chunk);

        lock (_lock)
        {
            _bytesReceived += chunk.Length;
            _chunksReceived++;
            _lastChunkAt = DateTime.UtcNow.ToString("o");

            // Debug chunk recording
            _recentChunks.Enqueue(new ChunkRecord
            {
                At = _lastChunkAt,
                Size = chunk.Length,
                Hex = BitConverter.ToString(chunk).Replace("-", " "),
                TextPreview = textChunk.Replace("\r", "<CR>").Replace("\n", "<LF>")
            });
            while (_recentChunks.Count > DebugChunkLimit)
                _recentChunks.TryDequeue(out _);

            _buffer += textChunk;
            _bufferPreview = _buffer.Replace("\r", "<CR>").Replace("\n", "<LF>");
            if (_bufferPreview.Length > 200)
                _bufferPreview = _bufferPreview[^200..];

            // Parse scan lines
            int boundaryIdx;
            while ((boundaryIdx = FindBoundaryIndex(_buffer)) != -1)
            {
                var payload = _buffer[..boundaryIdx];
                _buffer = _buffer[(boundaryIdx + 1)..];
                PublishScan(payload);
            }
        }

        // Reset flush timer
        flushTimer?.Dispose();
        flushTimer = new Timer(_ =>
        {
            string payload;
            lock (_lock)
            {
                payload = _buffer;
                _buffer = "";
                _bufferPreview = "";
            }
            PublishScan(payload);
        }, null, ScanIdleFlushMs, Timeout.Infinite);
    }

    private static int FindBoundaryIndex(string text)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        var cr = text.IndexOf('\r');
        var lf = text.IndexOf('\n');
        if (cr == -1) return lf;
        if (lf == -1) return cr;
        return Math.Min(cr, lf);
    }

    private void PublishScan(string rawValue)
    {
        var rawText = NormalizeScanText(rawValue);
        if (string.IsNullOrEmpty(rawText)) return;

        lock (_lock)
        {
            _latestScan = new ScannerEvent
            {
                RawText = rawText,
                ScannedAt = DateTime.UtcNow.ToString("o")
            };
            _lastPublishedAt = _latestScan.ScannedAt;
        }

        Console.WriteLine($"[scanner] scan received {rawText}");
        var scan = _latestScan;
        _ = Task.Run(async () =>
        {
            if (OnScan != null) await OnScan(scan);
        });
    }

    private static string NormalizeScanText(string raw)
        => System.Text.RegularExpressions.Regex.Replace(raw ?? "", @"[ -]", "").Trim();

    // ==================== Disconnect ====================

    private void Disconnect()
    {
        _readCts?.Cancel();
        _port?.Close();
        _port?.Dispose();
        _port = null;
        _buffer = "";
        _bufferPreview = "";
    }

    private async Task DisconnectAsync()
    {
        _readCts?.Cancel();
        if (_port != null)
        {
            try { if (_port.IsOpen) _port.Close(); }
            catch { /* ignore */ }
            _port.Dispose();
            _port = null;
        }
        _buffer = "";
        _bufferPreview = "";
        await Task.CompletedTask;
    }

    public bool IsConnected => _port?.IsOpen == true || IsMockConnected || IsKeyboardWedge;

    /// <summary>Accept a scan event from keyboard wedge or mock input.</summary>
    public ScannerEvent AcceptScan(string rawText)
    {
        var scanEvent = new ScannerEvent
        {
            RawText = rawText,
            ScannedAt = DateTime.UtcNow.ToString("o")
        };
        lock (_lock)
        {
            _latestScan = scanEvent;
            _lastPublishedAt = scanEvent.ScannedAt;
            _bytesReceived += rawText.Length;
            _chunksReceived++;
            _lastChunkAt = scanEvent.ScannedAt;
        }
        Console.WriteLine($"[scanner] scan published: {rawText}");

        // Fire the OnScan callback if set (same path as real scan)
        var scan = scanEvent;
        _ = Task.Run(async () =>
        {
            if (OnScan != null) await OnScan(scan);
        });

        return scanEvent;
    }

    private void ResetDebugState()
    {
        _bytesReceived = 0;
        _chunksReceived = 0;
        _lastChunkAt = null;
        _lastPublishedAt = null;
        _modemSignals = null;
        _bufferPreview = "";
        while (_recentChunks.TryDequeue(out _)) { }
    }

    // ==================== Scan management ====================

    public ScannerEvent? GetLatestScan() { lock (_lock) return _latestScan; }

    public void SyncLatestScan(ScannerEvent? scanEvent)
    {
        if (scanEvent?.RawText == null) return;
        lock (_lock)
        {
            if (scanEvent.Id != null && _consumedScanId != null && scanEvent.Id != _consumedScanId)
                _consumedScanId = null;
            _latestScan = new ScannerEvent
            {
                Id = scanEvent.Id ?? "",
                RawText = scanEvent.RawText,
                ScannedAt = scanEvent.ScannedAt ?? DateTime.UtcNow.ToString("o")
            };
            _lastPublishedAt = _latestScan.ScannedAt;
        }
    }

    public bool IsConsumedScan(ScannerEvent? scanEvent)
        => scanEvent?.Id != null && _consumedScanId != null && scanEvent.Id == _consumedScanId;

    public ScannerEvent? GetLatestVisibleScan(ScannerEvent? fallbackScan = null)
    {
        lock (_lock)
        {
            if (_latestScan != null) return _latestScan;
            if (IsConsumedScan(fallbackScan)) return null;
            return fallbackScan;
        }
    }

    public void ClearLatestScan(object? match = null)
    {
        lock (_lock)
        {
            if (_latestScan == null) return;
            if (match == null) { _latestScan = null; return; }

            // Try to match by properties (reflection-light approach)
            var matchDict = match.GetType().GetProperties()
                .ToDictionary(p => p.Name, p => p.GetValue(match)?.ToString());
            var scanEventId = matchDict.GetValueOrDefault("ScannerEventId");
            var qrCode = matchDict.GetValueOrDefault("QrCode");

            var idMatched = scanEventId != null && _latestScan.Id == scanEventId;
            var qrMatched = _latestScan.Id == null && qrCode != null && _latestScan.RawText == qrCode;
            if (idMatched || qrMatched) _latestScan = null;
        }
    }

    public void MarkScanConsumed(object? match = null)
    {
        lock (_lock)
        {
            var matchDict = match?.GetType().GetProperties()
                .ToDictionary(p => p.Name, p => p.GetValue(match)?.ToString());
            if (matchDict?.TryGetValue("ScannerEventId", out var scanEventId) == true && scanEventId != null)
                _consumedScanId = scanEventId;
            ClearLatestScan(match);
        }
    }

    public void ConsumeCurrentScan(object? match = null)
    {
        lock (_lock)
        {
            var matchDict = match?.GetType().GetProperties()
                .ToDictionary(p => p.Name, p => p.GetValue(match)?.ToString());
            if (matchDict?.TryGetValue("ScannerEventId", out var scanEventId) == true && scanEventId != null)
                _consumedScanId = scanEventId;
            else if (_latestScan?.Id != null)
                _consumedScanId = _latestScan.Id;

            _latestScan = null;
            _buffer = "";
            _bufferPreview = "";
        }
    }

    // ==================== Debug ====================

    public object GetDebugState()
    {
        var chunks = _recentChunks.ToArray();
        return new
        {
            connected = IsConnected,
            latestScan = _latestScan,
            currentConfig = _currentConfig,
            debug = new
            {
                bytesReceived = _bytesReceived,
                chunksReceived = _chunksReceived,
                lastChunkAt = _lastChunkAt,
                lastPublishedAt = _lastPublishedAt,
                modemSignals = _modemSignals,
                bufferPreview = _bufferPreview,
                recentChunks = chunks
            }
        };
    }

    public async Task<object> UpdateLineSignalsAsync(object options)
    {
        var opt = options.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(options));
        var dtr = opt.TryGetValue("Dtr", out var v) && v is bool bDtr ? bDtr : true;
        var rts = opt.TryGetValue("Rts", out var rv) && rv is bool bRts ? bRts : true;
        var reconnect = !opt.TryGetValue("Reconnect", out var rcv) || rcv is not bool bRec || bRec;

        if (_currentConfig == null)
            throw new InvalidOperationException("Scanner config not initialized");

        _currentConfig.Dtr = dtr;
        _currentConfig.Rts = rts;

        if (reconnect)
            await ConnectAsync();
        else if (_port?.IsOpen == true)
            ApplyLineSignals();

        return GetDebugState();
    }

    private void ApplyLineSignals()
    {
        if (_port?.IsOpen != true || _currentConfig == null) return;

        _port.DtrEnable = _currentConfig.Dtr;
        _port.RtsEnable = _currentConfig.Rts;

        _modemSignals = new
        {
            CtsHolding = _port.CtsHolding,
            DsrHolding = _port.DsrHolding,
            CDHolding = _port.CDHolding
        };
    }

    private record ChunkRecord
    {
        public string? At { get; set; }
        public int Size { get; set; }
        public string Hex { get; set; } = "";
        public string TextPreview { get; set; } = "";
    }
}
