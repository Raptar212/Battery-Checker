using System.Diagnostics;
using System.IO;
using System.Net.Http;
using BattaryChecker.UI;

namespace BattaryChecker;

/// <summary>Network side of the update check: asks GitHub for the latest release, downloads and starts its installer.</summary>
static class Updater
{
    public static readonly Version Current = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);

    // One client for the app's lifetime; created on the first check, so a session without one costs nothing.
    static readonly Lazy<HttpClient> Client = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BatteryChecker/{Current.ToString(3)}"); // GitHub's API requires one
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    });

    /// <summary>The latest release if it is newer than this program; null when there is none.</summary>
    /// <exception cref="HttpRequestException">No network, GitHub unreachable or an error status.</exception>
    /// <exception cref="TaskCanceledException">Timed out.</exception>
    /// <exception cref="InvalidDataException">A newer version exists, but without a usable installer.</exception>
    public static async Task<UpdateRelease?> FindNewer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var json = await Client.Value.GetStringAsync(UpdateCheck.LatestUrl, cts.Token);
        if (UpdateCheck.Parse(json) is { } release)
            return UpdateCheck.IsNewer(release.Version, Current) ? release : null;
        if (UpdateCheck.TagVersion(json) is { } version && UpdateCheck.IsNewer(version, Current))
            throw new InvalidDataException(Text.UpdateNoInstaller(version.ToString(3)));
        return null;
    }

    /// <summary>Downloads the installer to %TEMP% and returns its path once its SHA-256 matches the release.</summary>
    /// <exception cref="HttpRequestException">Download failed.</exception>
    /// <exception cref="TaskCanceledException">Timed out.</exception>
    /// <exception cref="IOException">Could not write the file.</exception>
    /// <exception cref="InvalidDataException">The file is not the one the release lists.</exception>
    public static async Task<string> Download(UpdateRelease release)
    {
        // Own name, not the server's: nothing from the response decides where the file lands.
        var path = Path.Combine(Path.GetTempPath(), UpdateCheck.InstallerName(release.Version.ToString(3)));
        var part = path + ".part";
        // The client's timeout covers only the headers (ResponseHeadersRead); this one covers the body too.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            using (var response = await Client.Value.GetAsync(release.Installer, HttpCompletionOption.ResponseHeadersRead, cts.Token))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(part);
                await response.Content.CopyToAsync(file, cts.Token);
                file.Position = 0;
                if (!await Task.Run(() => UpdateCheck.HashMatches(file, release.Sha256))) // tens of MB: not on the UI thread
                    throw new InvalidDataException(Text.UpdateHashMismatch);
            }
            File.Move(part, path, overwrite: true);
            return path;
        }
        finally
        {
            File.Delete(part); // a failed or cancelled download; after the move there is nothing to delete
        }
    }

    /// <summary>The usual installer with its windows; it keeps the settings and offers to start the new version.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows could not start it.</exception>
    public static void Run(string installer)
    {
        using var process = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true })
            ?? throw new InvalidOperationException(Text.UpdateNotStarted);
    }

    /// <summary>A short reason for the user: the network's own messages are in English.</summary>
    public static string Reason(Exception e) => e switch
    {
        TaskCanceledException => Text.GitHubTimeout,
        HttpRequestException { StatusCode: { } code } => Text.GitHubStatus((int)code),
        HttpRequestException => Text.GitHubUnreachable,
        _ => e.Message,
    };
}
