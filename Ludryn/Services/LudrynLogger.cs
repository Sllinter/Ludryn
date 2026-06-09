using System.Diagnostics;

namespace Ludryn.Services;

public static class LudrynLogger
{
    private static readonly object Gate = new();

    public static void Log(string area, string message) =>
        Write(area, message, null);

    public static void Error(string area, string message, Exception exception) =>
        Write(area, message, exception);

    private static void Write(string area, string message, Exception? exception)
    {
        var line = $"{DateTime.Now:O} [{area}] {message}";
        if (exception is not null)
        {
            line += $"{Environment.NewLine}{exception}";
        }

        Debug.WriteLine(line);

        lock (Gate)
        {
            foreach (var logDir in GetLogDirectories())
            {
                TryWrite(logDir, area, line);
            }
        }
    }

    private static IEnumerable<string> GetLogDirectories()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ludryn",
            "logs");

        yield return Path.Combine(AppContext.BaseDirectory, "logs");

        var projectDir = TryFindProjectDirectory();
        if (projectDir is not null)
        {
            yield return Path.Combine(projectDir, "Logs");
        }

        yield return Path.Combine(Directory.GetCurrentDirectory(), "Ludryn", "Logs");
    }

    private static string? TryFindProjectDirectory()
    {
        try
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var projectPath = Path.Combine(directory.FullName, "Ludryn.csproj");
                if (File.Exists(projectPath))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static void TryWrite(string logDir, string area, string line)
    {
        try
        {
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, $"{SanitizeFileName(area)}.log"),
                line + Environment.NewLine);
            File.AppendAllText(
                Path.Combine(logDir, "ludryn.log"),
                line + Environment.NewLine);
        }
        catch
        {
            // Logging should never interfere with the app flow.
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '-');
        }

        return string.IsNullOrWhiteSpace(value) ? "app" : value;
    }
}
