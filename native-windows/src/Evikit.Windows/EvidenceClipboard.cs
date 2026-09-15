using Evikit.Core;

namespace Evikit.Windows;

internal static class EvidenceClipboard
{
    // Excel / DB grids may publish text AND a bitmap. Preserve structured cells
    // before considering the bitmap, including when an image group is selected.
    internal static string? TableText()
    {
        if (!Clipboard.ContainsText()) return null;
        string text = Clipboard.GetText();
        return EvidenceText.Analyze(text).Kind == "table" ? text : null;
    }
}
