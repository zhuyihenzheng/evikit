namespace Evikit.Core;

public static class Media
{
    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;
    public const int MaxInlineBytes = 25 * 1024 * 1024;
    public static bool IsVideo(string file) => Path.GetExtension(file).ToLowerInvariant() is
        ".mp4" or ".mov" or ".avi" or ".wmv" or ".mkv" or ".webm" or ".m4v";
}
