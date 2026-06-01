namespace VideoArchiveManager.Core.Services;

/// <summary>
/// Resolves the DaVinci Resolve proxy file for a given hero clip. Resolve's
/// built-in proxy generator writes a sibling "Proxy" folder next to each
/// source file and re-uses the source's base name (only the extension changes
/// based on the proxy codec — typically <c>.mov</c> for ProRes Proxy / DNxHR
/// LB and <c>.mp4</c> for H.264/H.265).
///
/// <para>
/// We deliberately match on the filename <em>stem</em> (everything before the
/// final dot) rather than enumerating a hard-coded extension list, so the
/// resolver keeps working when the user changes Resolve's proxy codec without
/// us needing a new config field.
/// </para>
///
/// <para>
/// This service is intentionally orthogonal to the scanner's
/// <see cref="VideoArchiveManager.Core.Configuration.AppSettings.ExcludedFolderNames"/>
/// list: the scanner excludes "Proxy" so proxies never appear as their own
/// catalog entries (the catalog stays clean and shows only hero clips),
/// while this resolver does direct path probes at playback time and finds
/// the proxy anyway. The two systems never coordinate and don't need to.
/// </para>
/// </summary>
public sealed class DaVinciProxyResolver : IProxyResolver
{
    // Singular "Proxy" is the exact folder name Resolve's proxy generator
    // creates. Plural "Proxies" / "_Proxies" variants exist in some shops but
    // are not the default; we hardcode the default for v1 and can add a
    // configurable list in AppSettings if a user reports a different naming.
    private const string ProxyFolderName = "Proxy";

    public string? TryResolveProxy(string heroFilePath)
    {
        if (string.IsNullOrWhiteSpace(heroFilePath)) return null;

        try
        {
            var heroDir = Path.GetDirectoryName(heroFilePath);
            if (string.IsNullOrEmpty(heroDir)) return null;

            var proxyDir = Path.Combine(heroDir, ProxyFolderName);
            if (!Directory.Exists(proxyDir)) return null;

            var stem = Path.GetFileNameWithoutExtension(heroFilePath);
            if (string.IsNullOrEmpty(stem)) return null;

            // Stem-only, case-insensitive match. Picks the first file in the
            // Proxy folder whose name-without-extension equals the hero's
            // name-without-extension. In the extremely unlikely event the
            // folder contains both <stem>.mov AND <stem>.mp4, EnumerateFiles'
            // platform-default ordering wins; both files target the same
            // hero so either is acceptable.
            foreach (var candidate in Directory.EnumerateFiles(proxyDir))
            {
                var candidateStem = Path.GetFileNameWithoutExtension(candidate);
                if (string.Equals(candidateStem, stem, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Any I/O failure (offline drive, permission denied, malformed
            // path) just falls through to the hero file. Proxy resolution
            // is opportunistic by design.
        }

        return null;
    }
}
