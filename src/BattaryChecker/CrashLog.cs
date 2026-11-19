using System.IO;

namespace BattaryChecker;

/// <summary>
/// The one trace an unhandled exception leaves: %LOCALAPPDATA%\BattaryChecker\crash.log, to attach to a bug
/// report. Kept small: past 256 KB it becomes crash.old.log and a new file starts.
/// </summary>
public static class CrashLog
{
    public const long MaxBytes = 256 * 1024;

    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(AppConfig.DefaultPath)!, "crash.log");

    /// <summary>Never throws: it runs while the app is already going down.</summary>
    public static void Write(Exception? error, string path, DateTime now)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                File.Move(path, Path.ChangeExtension(path, ".old.log"), overwrite: true);
            var version = typeof(CrashLog).Assembly.GetName().Version?.ToString(3);
            File.AppendAllText(path, $"{now:yyyy-MM-dd HH:mm:ss} Battery Checker {version}{Environment.NewLine}"
                + $"{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) // crash path: whatever goes wrong here must not stop the caller from undocking the widget
        {
        }
    }
}
