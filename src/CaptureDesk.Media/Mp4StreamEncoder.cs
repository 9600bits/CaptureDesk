using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace CaptureDesk.Media;

/// <summary>Creates an H.264 MP4 from the PNG frame journal using Windows' built-in media stack.</summary>
public static class Mp4StreamEncoder
{
    public static async Task WriteAsync(
        string outputPath,
        IReadOnlyList<(string Path, int Delay)> frames,
        int frameRate,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (frames.Count == 0) throw new InvalidOperationException("没有可导出的帧。");
        cancellationToken.ThrowIfCancellationRequested();

        var (width, height) = ReadDimensions(frames[0].Path);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        if (profile.Video is null) throw new InvalidOperationException("系统没有可用的 H.264 视频编码器。");
        profile.Container.Subtype = "MPEG4";
        profile.Video.Subtype = "H264";
        profile.Video.Width = (uint)(width & ~1);
        profile.Video.Height = (uint)(height & ~1);
        profile.Video.FrameRate.Numerator = (uint)Math.Clamp(frameRate, 1, 30);
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.Bitrate = Math.Max(profile.Video.Bitrate, (uint)Math.Clamp((long)profile.Video.Width * profile.Video.Height * 4, 400_000, 25_000_000));

        var composition = new MediaComposition();
        try
        {
            for (var index = 0; index < frames.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = await StorageFile.GetFileFromPathAsync(frames[index].Path).AsTask(cancellationToken);
                var duration = TimeSpan.FromMilliseconds(Math.Max(1, frames[index].Delay * 10));
                var clip = await MediaClip.CreateFromImageFileAsync(source, duration).AsTask(cancellationToken);
                composition.Clips.Add(clip);
                progress?.Report((index + 1d) / frames.Count * .25);
            }

            var fullOutputPath = Path.GetFullPath(outputPath);
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(fullOutputPath)!).AsTask(cancellationToken);
            var destination = await folder.CreateFileAsync(Path.GetFileName(fullOutputPath), CreationCollisionOption.ReplaceExisting).AsTask(cancellationToken);
            await composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, profile).AsTask(cancellationToken);
            progress?.Report(1);
        }
        catch (OperationCanceledException) { throw; }
        catch (COMException ex)
        {
            throw new InvalidOperationException("系统 H.264 编码器不可用，或当前录制尺寸不受 MP4 编码器支持。请改用 GIF 或调整录制区域后重试。", ex);
        }
        catch (Exception ex) when (ex.HResult != 0)
        {
            throw new InvalidOperationException("MP4 导出失败。系统可能不支持当前视频编码设置，请改用 GIF 或调整录制区域后重试。", ex);
        }
    }

    private static (int Width, int Height) ReadDimensions(string path)
    {
        using var input = File.OpenRead(path);
        var frame = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var width = frame.PixelWidth & ~1;
        var height = frame.PixelHeight & ~1;
        if (width < 2 || height < 2) throw new InvalidDataException("录制帧尺寸不足，无法导出 MP4。");
        return (width, height);
    }
}
