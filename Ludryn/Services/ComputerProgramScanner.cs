using Ludryn.Models;

namespace Ludryn.Services;

public static class ComputerProgramScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".lnk",
        ".url",
        ".appref-ms"
    };

    public static IReadOnlyList<ComputerProgramEntry> Scan()
    {
        var entries = new List<ComputerProgramEntry>();
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0
        };

        foreach (var root in GetShortcutRoots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.*", enumerationOptions)
                    .Where(path => SupportedExtensions.Contains(Path.GetExtension(path))))
                {
                    var entry = CreateEntry(path, root);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch
            {
                // Pastas protegidas do Menu Iniciar sao ignoradas.
            }
        }

        return entries
            .Where(entry => !IsSystemUtility(entry.Title, entry.ExecutablePath))
            .GroupBy(entry => $"{Normalize(entry.Title)}|{entry.ExecutablePath}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static ComputerProgramEntry? CreateEntry(string path, string root)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        var location = root.Contains("Start Menu", StringComparison.OrdinalIgnoreCase)
            ? "Menu Iniciar"
            : "Area de Trabalho";
        var source = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? location
            : $"Atalho - {location}";

        // Mantemos o proprio atalho. Assim o Windows preserva argumentos,
        // protocolos e destinos de apps empacotados ao executa-lo.
        return new ComputerProgramEntry
        {
            Title = Path.GetFileNameWithoutExtension(path),
            ExecutablePath = path,
            Source = source
        };
    }

    private static IEnumerable<string> GetShortcutRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");

        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var oneDrive = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(oneDrive))
            {
                yield return Path.Combine(oneDrive, "Desktop");
            }
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(
            appData,
            "Microsoft",
            "Internet Explorer",
            "Quick Launch",
            "User Pinned",
            "StartMenu");
    }

    private static bool IsSystemUtility(string title, string executablePath)
    {
        var combined = $"{title} {Path.GetFileName(executablePath)}";
        return combined.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("unins", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("install", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("help", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("readme", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
