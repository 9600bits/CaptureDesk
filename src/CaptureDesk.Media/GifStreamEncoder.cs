using System.IO;
using System.Windows.Media.Imaging;

namespace CaptureDesk.Media;

/// <summary>Streams independent WIC-encoded frames with per-frame palettes and timing.</summary>
public static class GifStreamEncoder
{
    public static void Write(Stream output, IReadOnlyList<(string Path, int Delay)> frames,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (frames.Count == 0) throw new InvalidOperationException("没有可导出的帧。");
        using var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, true);
        for (var index = 0; index < frames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var input = File.OpenRead(frames[index].Path);
            var bitmap = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var encoder = new GifBitmapEncoder();
            encoder.Frames.Add(bitmap);
            using var encoded = new MemoryStream();
            encoder.Save(encoded);
            var bytes = encoded.ToArray();
            var paletteSize = (bytes[10] & 0x80) != 0 ? 3 * (1 << ((bytes[10] & 7) + 1)) : 0;
            var offset = 13 + paletteSize;
            if (index == 0)
            {
                writer.Write("GIF89a"u8);
                writer.Write(bytes, 6, 4); // Logical dimensions.
                writer.Write((byte)0x70); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write(new byte[] { 0x21, 0xff, 11 });
                writer.Write("NETSCAPE2.0"u8);
                writer.Write(new byte[] { 3, 1, 0, 0, 0 });
            }
            while (bytes[offset] == 0x21)
            {
                offset += 2;
                while (bytes[offset] != 0) offset += bytes[offset] + 1;
                offset++;
            }
            if (bytes[offset] != 0x2c) throw new InvalidDataException("GIF 帧编码失败。");
            writer.Write(new byte[] { 0x21, 0xf9, 4, 4 }); // Keep frame until next full-frame replacement.
            writer.Write((ushort)Math.Clamp(frames[index].Delay, 2, 65535));
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write(bytes, offset, 9);
            var packed = bytes[offset + 9];
            offset += 10;
            if ((packed & 0x80) != 0)
            {
                writer.Write(packed);
                var localSize = 3 * (1 << ((packed & 7) + 1));
                writer.Write(bytes, offset, localSize);
                offset += localSize;
            }
            else
            {
                if (paletteSize == 0) throw new InvalidDataException("GIF 帧缺少调色板。");
                writer.Write((byte)(0x80 | (packed & 0x40) | (bytes[10] & 7)));
                writer.Write(bytes, 13, paletteSize);
            }
            writer.Write(bytes[offset++]); // LZW minimum code size.
            while (bytes[offset] != 0)
            {
                var length = bytes[offset] + 1;
                writer.Write(bytes, offset, length);
                offset += length;
            }
            writer.Write((byte)0);
            progress?.Report((index + 1d) / frames.Count);
        }
        writer.Write((byte)0x3b);
    }
}
