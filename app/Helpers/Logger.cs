using System.Diagnostics;
using OHelper.Helpers;

public static class Logger
{
    private const int MaximumLines = 2000;
    private const long CleanupThresholdBytes = 1024 * 1024;
    private static readonly object Sync = new();

    public static readonly string appPath = Path.Combine(
        Environment.GetFolderPath(ProcessHelper.IsRunningAsSystem()
            ? Environment.SpecialFolder.CommonApplicationData
            : Environment.SpecialFolder.ApplicationData),
        "OHelper");

    public static readonly string logFile = Path.Combine(appPath, "log.txt");

    public static void WriteLine(string logMessage)
    {
        string line = $"{DateTime.Now:O}: {logMessage}";
        Debug.WriteLine(line);

        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(appPath);
                File.AppendAllText(logFile, line + Environment.NewLine);

                if (new FileInfo(logFile).Length >= CleanupThresholdBytes)
                    CleanupCore();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Logger failure: {ex.Message}");
            }
        }
    }

    public static void Cleanup()
    {
        lock (Sync)
        {
            try
            {
                CleanupCore();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Logger cleanup failure: {ex.Message}");
            }
        }
    }

    private static void CleanupCore()
    {
        if (!File.Exists(logFile)) return;

        string[] lines = File.ReadAllLines(logFile);
        if (lines.Length <= MaximumLines) return;

        File.WriteAllLines(logFile, lines[^MaximumLines..]);
    }
}
