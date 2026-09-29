using System.IO;
using UnityEngine;

// Resolves a cache file for the AI clients. The player's own cache under
// persistentDataPath wins; otherwise falls back to the copy shipped with the
// game in StreamingAssets/<same folder>/<same file name>, so a fresh build
// with no API keys still gets every AI result that was generated during
// development. Copy the persistentDataPath caches (AiTextCache,
// AiTextureCache) into Assets/StreamingAssets to refresh what ships.
// Note: File.Exists on StreamingAssets only works on desktop platforms
// (on Android/WebGL it lives inside the package and needs UnityWebRequest).
public static class AiCache
{
    public static string FindExisting(string cachePath)
    {
        if (File.Exists(cachePath)) return cachePath;

        string folder = Path.GetFileName(Path.GetDirectoryName(cachePath));
        string bundled = Path.Combine(Application.streamingAssetsPath, folder, Path.GetFileName(cachePath));
        return File.Exists(bundled) ? bundled : null;
    }
}
