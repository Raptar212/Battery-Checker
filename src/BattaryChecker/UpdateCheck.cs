using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace BattaryChecker;

/// <summary>A release on GitHub: its version, the installer to download and the installer's SHA-256 (hex).</summary>
public sealed record UpdateRelease(Version Version, Uri Installer, string Sha256);

/// <summary>Reads GitHub's "latest release" answer. No network here: <see cref="Updater"/> downloads, this decides.</summary>
public static class UpdateCheck
{
    public const string RepoUrl = "https://github.com/Raptar212/Battery-Checker";
    public const string LatestUrl = "https://api.github.com/repos/Raptar212/Battery-Checker/releases/latest";

    /// <summary>Only a file from this repository's releases is ever downloaded.</summary>
    const string DownloadPrefix = RepoUrl + "/releases/download/";

    public static string InstallerName(string version) => $"BatteryChecker-Setup-{version}.exe";

    /// <summary>
    /// The release in GitHub's JSON, or null when it is not usable: a draft or pre-release, a tag other
    /// than "v1.2.3", no installer, an installer from elsewhere or without a SHA-256.
    /// </summary>
    public static UpdateRelease? Parse(string json) => Load(json, out _);

    /// <summary>
    /// The version of the release even when <see cref="Parse"/> refuses its installer, so a manual check
    /// can say "a new version exists, but without an installer" instead of "this is the latest".
    /// </summary>
    public static Version? TagVersion(string json)
    {
        Load(json, out var version);
        return version;
    }

    static UpdateRelease? Load(string json, out Version? version)
    {
        version = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Read(doc.RootElement, out version);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static UpdateRelease? Read(JsonElement root, out Version? version)
    {
        version = null;
        if (root.ValueKind != JsonValueKind.Object || Bool(root, "draft") || Bool(root, "prerelease"))
            return null;
        if (Str(root, "tag_name") is not { } tag || !tag.StartsWith('v') || ParseVersion(tag[1..]) is not { } v)
            return null;
        version = v;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        var expectedName = InstallerName(tag[1..]);
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object || Str(asset, "name") != expectedName)
                continue;
            if (Str(asset, "browser_download_url") is not { } url || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal)
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;
            if (Str(asset, "digest") is not { } digest || !digest.StartsWith("sha256:", StringComparison.Ordinal)
                || digest.Length != 7 + 64 || !digest[7..].All(Uri.IsHexDigit))
                return null;
            return new UpdateRelease(v, uri, digest[7..].ToLowerInvariant());
        }
        return null;
    }

    /// <summary>Compares major.minor.patch only: the assembly's 1.2.0.0 equals the tag's 1.2.0.</summary>
    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    /// <summary>True when the file's SHA-256 is <paramref name="sha256"/> (hex, any case).</summary>
    public static bool HashMatches(Stream file, string sha256) =>
        Convert.ToHexStringLower(SHA256.HashData(file)) == sha256.ToLowerInvariant();

    /// <summary>"1.2.3" gives 1.2.3; exactly three plain numbers, anything else is null.</summary>
    static Version? ParseVersion(string text) =>
        text.Split('.') is { Length: 3 } parts && parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit))
            && Version.TryParse(text, out var v) ? v : null;

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
