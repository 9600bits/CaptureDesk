using System.IO;
using System.Windows;

namespace CaptureDesk.App;

internal static class SaveDialogHelper
{
    public static string? ShowImage(Window owner, string baseName, string? initialDirectory = null)
    {
        var jpeg = App.Settings.SaveFormat == "JPEG";
        return Show(owner, new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG 图像|*.png|JPEG 图像|*.jpg;*.jpeg",
            FilterIndex = jpeg ? 2 : 1,
            DefaultExt = jpeg ? ".jpg" : ".png",
            AddExtension = true,
            InitialDirectory = initialDirectory ?? "",
            FileName = Path.GetFileNameWithoutExtension(baseName)
        }, index => index == 2 ? ".jpg" : ".png", index => index == 2 ? [".jpg", ".jpeg"] : [".png"]);
    }

    internal static string NormalizeExtension(string path, string expected, IReadOnlyCollection<string> accepted)
    {
        var extension = Path.GetExtension(path);
        return accepted.Contains(extension, StringComparer.OrdinalIgnoreCase) ? path : Path.ChangeExtension(path, expected);
    }

    private static string? Show(Window owner, Microsoft.Win32.SaveFileDialog dialog,
        Func<int, string> expectedExtension, Func<int, IReadOnlyCollection<string>> acceptedExtensions)
    {
        if (dialog.ShowDialog(owner) != true) return null;
        var path = NormalizeExtension(dialog.FileName, expectedExtension(dialog.FilterIndex), acceptedExtensions(dialog.FilterIndex));
        if (!string.Equals(path, dialog.FileName, StringComparison.OrdinalIgnoreCase) && File.Exists(path) &&
            MessageBox.Show(owner, $"{Path.GetFileName(path)} 已存在。是否替换？", "确认另存为", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return null;
        return path;
    }
}
