using System.Windows;
using System.Windows.Media;

namespace CaptureDesk.App;

internal static class Theme
{
    public static void Apply(bool dark)
    {
        var names = new[] { "CanvasBrush", "PanelBrush", "SurfaceMutedBrush", "LineBrush", "TextBrush", "MutedBrush", "AccentBrush", "AccentHoverBrush", "AccentSoftBrush", "FocusBrush" };
        var colors = dark
            ? new[] { "#202522", "#282E29", "#313B33", "#526055", "#EDF3EC", "#B4C3B6", "#397550", "#45825B", "#344F3B", "#9EBFA6" }
            : new[] { "#F5F6F2", "#FEFFFC", "#EDF0E8", "#DCE2D7", "#29382E", "#5D6B60", "#356B4B", "#2C5D40", "#E3EDDF", "#467F5E" };
        for (var i = 0; i < names.Length; i++)
            System.Windows.Application.Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        if (SystemParameters.HighContrast)
        {
            var r = System.Windows.Application.Current.Resources;
            r["CanvasBrush"] = r["PanelBrush"] = r["SurfaceMutedBrush"] = SystemColors.WindowBrush;
            r["TextBrush"] = r["MutedBrush"] = r["LineBrush"] = r["FocusBrush"] = SystemColors.WindowTextBrush;
            r["AccentBrush"] = r["AccentHoverBrush"] = SystemColors.HighlightBrush;
            r["AccentSoftBrush"] = SystemColors.ControlBrush;
        }
    }
}
