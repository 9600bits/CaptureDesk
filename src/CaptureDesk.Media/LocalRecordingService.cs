using System.Diagnostics;
using System.IO;
using CaptureDesk.Core;
using CaptureDesk.Native;

namespace CaptureDesk.Media;

// Small offline GIF backend. WGC/MP4 are separate, not advertised by this backend.
public sealed class LocalRecordingService : IRecordingService, IDisposable
{
    private readonly object _gate = new();
    private readonly Stopwatch _clock = new();
    private readonly List<(string Path, double Time)> _frames = [];
    private CancellationTokenSource? _stop;
    private Task? _capture;
    private bool _paused;
    public bool IsRecording => _capture is { IsCompleted: false };
    public bool IsPaused { get { lock (_gate) return _paused; } }
    public TimeSpan Elapsed { get { lock (_gate) return _clock.Elapsed; } }
    public string? RecoveryDirectory { get; private set; }
    public string? Failure { get; private set; }
    public static string RecoveryRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptureDesk", "Recordings");
    private double? _recoveredDuration;
    public double Duration => _recoveredDuration ?? Elapsed.TotalSeconds;
    public static LocalRecordingService Recover(string journal)
    {
        var service = new LocalRecordingService { RecoveryDirectory = Path.GetDirectoryName(Path.GetFullPath(journal)) };
        foreach (var line in File.ReadLines(journal))
        {
            var fields = line.Split('\t');
            if (fields.Length != 2 || fields[0] != Path.GetFileName(fields[0]) || !fields[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            if (!double.TryParse(fields[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var time) || !double.IsFinite(time) || time < 0) continue;
            if (service._frames.Count > 0 && time <= service._frames[^1].Time) continue;
            var path = Path.Combine(service.RecoveryDirectory!, fields[0]);
            if (File.Exists(path)) service._frames.Add((path, time));
            if (service._frames.Count >= 10000) break;
        }
        if (service._frames.Count == 0) throw new InvalidDataException("没有可恢复的帧。");
        service._recoveredDuration = service._frames[^1].Time + .1;
        return service;
    }
    public int FrameRate { get; init; } = 10;
    public bool IncludeCursor { get; init; }
    public Task StartAsync(CaptureRegion region, CancellationToken cancellationToken = default)
    {
        if (_capture is not null) throw new InvalidOperationException("录制会话已启动。");
        if (region.IsEmpty || (long)region.Width * region.Height > 8_300_000) throw new ArgumentException("请选择不超过 830 万像素的区域。");
        cancellationToken.ThrowIfCancellationRequested();
        RecoveryDirectory = Path.Combine(RecoveryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RecoveryDirectory);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _clock.Start();
        _capture = Task.Run(async () =>
        {
            long bytes = 0;
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var tick = Stopwatch.StartNew();
                    lock (_gate)
                    {
                        if (!_paused)
                        {
                            var at = _clock.Elapsed.TotalSeconds;
                            var frame = NativeCaptureService.CaptureRegion(region, IncludeCursor);
                            var path = Path.Combine(RecoveryDirectory, $"{_frames.Count:D6}.png");
                            PngCodec.Save(frame, path);
                            _frames.Add((path, at));
                            File.AppendAllText(Path.Combine(RecoveryDirectory, "frames.tsv"), $"{Path.GetFileName(path)}\t{at.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}\n");
                            bytes += new FileInfo(path).Length;
                            if (bytes > 512L * 1024 * 1024 || at >= 300) { Failure = "已达到单次录制上限（5 分钟或 512 MB），请保存当前片段。"; break; }
                        }
                    }
                    await Task.Delay(Math.Max(1, 1000 / Math.Clamp(FrameRate, 1, 30) - (int)tick.ElapsedMilliseconds), _stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Failure = ex.Message; }
            finally { lock (_gate) _clock.Stop(); }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }
    public Task PauseAsync()
    {
        lock (_gate)
        {
            if (!IsRecording) return Task.CompletedTask;
            _paused = !_paused;
            if (_paused) _clock.Stop(); else _clock.Start();
        }
        return Task.CompletedTask;
    }
    public async Task FinishAsync()
    {
        _stop?.Cancel();
        if (_capture is not null) await _capture;
    }
    public async Task StopAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        await FinishAsync();
        await ExportAsync(outputPath, null, cancellationToken);
    }
    public async Task ExportAsync(string outputPath, IProgress<double>? progress, CancellationToken cancellationToken = default, double start = 0, double? end = null)
    {
        if (IsRecording) throw new InvalidOperationException("请先停止录制。");
        var until = end ?? Duration;
        if (!double.IsFinite(start) || !double.IsFinite(until) || start < 0 || until <= start || start >= Duration || until > Duration + .1)
            throw new ArgumentException("剪辑范围须在录制时长内，且结束时间大于开始时间。");
        var frames = _frames.Select((frame, i) => (frame.Path, Start: Math.Max(start, frame.Time),
                End: Math.Min(until, i + 1 < _frames.Count ? _frames[i + 1].Time : Math.Max(Duration, frame.Time + 1d / FrameRate))))
            .Where(x => x.End > x.Start).Select(x => (x.Path, Delay: (int)Math.Round(100 * (x.End - x.Start)))).ToArray();
        // Write beside destination; failures/cancellation never replace an existing user file.
        var temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using var stream = File.Create(temporary);
                GifStreamEncoder.Write(stream, frames, progress, cancellationToken);
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, outputPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() { _stop?.Cancel(); _stop?.Dispose(); }
}
