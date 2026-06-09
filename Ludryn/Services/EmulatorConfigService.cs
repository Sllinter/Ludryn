using Ludryn.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ludryn.Services;

public sealed class EmulatorConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ludryn",
        "emulators.json");

    private readonly EmulatorConfigStore _store;

    public EmulatorConfigService()
    {
        _store = Load();
    }

    public IReadOnlyList<EmulatorEntry> Emulators => _store.Emulators;
    public IReadOnlyList<RomDirectoryEntry> RomDirectories => _store.RomDirectories;

    public void AddEmulator(string name, string executablePath, string platform)
    {
        name = EmulatorLaunchService.GetDisplayName(executablePath);
        _store.Emulators.RemoveAll(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Platform, platform, StringComparison.OrdinalIgnoreCase));
        _store.Emulators.Add(new EmulatorEntry(name, executablePath, platform));
        Save();
    }

    public void RemoveEmulator(EmulatorEntry emulator)
    {
        _store.Emulators.RemoveAll(e =>
            string.Equals(e.Name, emulator.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.ExecutablePath, emulator.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Platform, emulator.Platform, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public void AddRomDirectory(string path, string platform)
    {
        _store.RomDirectories.RemoveAll(d =>
            string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.Platform, platform, StringComparison.OrdinalIgnoreCase));
        _store.RomDirectories.Add(new RomDirectoryEntry(path, platform));
        Save();
    }

    public void RemoveRomDirectory(RomDirectoryEntry directory)
    {
        _store.RomDirectories.RemoveAll(d =>
            string.Equals(d.Path, directory.Path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.Platform, directory.Platform, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public IReadOnlyList<Game> ScanRomGames()
    {
        var games = new List<Game>();
        foreach (var directory in _store.RomDirectories)
        {
            games.AddRange(ScanRomDirectory(directory.Path, directory.Platform, includeConfiguredEmulators: true));
        }

        return games
            .GroupBy(g => $"{NormalizeTitle(g.Platform)}:{NormalizeTitle(g.Title)}")
            .Select(MergeRomGroup)
            .OrderBy(g => g.Title)
            .ToList();
    }

    public IReadOnlyList<Game> PreviewRomDirectory(string path, string platform) =>
        ScanRomDirectory(path, platform, includeConfiguredEmulators: false)
            .OrderBy(g => g.Title)
            .ToList();

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static EmulatorConfigStore Load()
    {
        try
        {
            return File.Exists(ConfigPath)
                ? JsonSerializer.Deserialize<EmulatorConfigStore>(File.ReadAllText(ConfigPath)) ?? new EmulatorConfigStore()
                : new EmulatorConfigStore();
        }
        catch
        {
            return new EmulatorConfigStore();
        }
    }

    private static Game MergeRomGroup(IEnumerable<Game> group)
    {
        var games = group.ToList();
        var first = games[0];
        foreach (var game in games.Skip(1))
        {
            foreach (var installation in game.Installations)
            {
                if (!first.Installations.Any(i => string.Equals(i.Launcher, installation.Launcher, StringComparison.OrdinalIgnoreCase)))
                {
                    first.Installations.Add(installation);
                }
            }
        }

        if (first.Installations.Count > 0)
        {
            first.SelectedLauncher = first.Installations[0].Launcher;
        }

        return first;
    }

    private IEnumerable<Game> ScanRomDirectory(string path, string platform, bool includeConfiguredEmulators)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        var games = new List<Game>();
        foreach (var target in DiscoverGameTargets(path, platform))
        {
            var game = CreateRomGame(target.Title, platform, target.LaunchPath);
            if (includeConfiguredEmulators)
            {
                foreach (var emulator in _store.Emulators.Where(e =>
                    string.Equals(e.Platform, platform, StringComparison.OrdinalIgnoreCase) ||
                    EmulatorLaunchService.CanUseForGame(e.ExecutablePath, platform, target.LaunchPath)))
                {
                    game.Installations.Add(new GameInstallation
                    {
                        Launcher = emulator.Name,
                        LaunchId = emulator.ExecutablePath,
                        InstallPath = target.LaunchPath,
                        IsDetected = true
                    });
                }
            }

            games.Add(game);
        }

        return games;
    }

    private static IEnumerable<EmulatedGameTarget> DiscoverGameTargets(string path, string platform)
    {
        if (platform.Equals("PlayStation 3", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoverPlayStation3Games(path);
        }

        if (platform.Equals("Wii U", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoverWiiUGames(path);
        }

        var recursive = platform is "Wii" or "GameCube";
        return EnumerateFilesSafely(path, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(file => IsRomFile(file, platform))
            .Select(file => new EmulatedGameTarget(GetFileGameTitle(file, platform), file))
            .GroupBy(target => NormalizeTitle(target.Title), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private static IEnumerable<EmulatedGameTarget> DiscoverPlayStation3Games(string path)
    {
        var candidates = new List<string> { path };
        candidates.AddRange(EnumerateDirectoriesSafely(path));

        var results = new List<EmulatedGameTarget>();
        foreach (var candidate in candidates)
        {
            var ebootPath = FindPlayStation3BootFile(candidate);
            if (ebootPath is null)
            {
                continue;
            }

            var gameRoot = Path.GetFileName(candidate).Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(candidate)?.FullName ?? candidate
                : candidate;
            results.Add(new EmulatedGameTarget(CleanTitle(Path.GetFileName(gameRoot)), ebootPath));
        }

        results.AddRange(EnumerateFilesSafely(path, SearchOption.TopDirectoryOnly)
            .Where(file => IsRomFile(file, "PlayStation 3"))
            .Select(file => new EmulatedGameTarget(GetFileGameTitle(file, "PlayStation 3"), file)));

        return results
            .GroupBy(target => target.LaunchPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private static IEnumerable<EmulatedGameTarget> DiscoverWiiUGames(string path)
    {
        var candidates = new List<string> { path };
        candidates.AddRange(EnumerateDirectoriesSafely(path));

        var results = new List<EmulatedGameTarget>();
        foreach (var candidate in candidates)
        {
            var codePath = Path.Combine(candidate, "code");
            var rpxPath = EnumerateFilesSafely(codePath, SearchOption.TopDirectoryOnly)
                .FirstOrDefault(file => Path.GetExtension(file).Equals(".rpx", StringComparison.OrdinalIgnoreCase));
            if (rpxPath is null)
            {
                continue;
            }

            results.Add(new EmulatedGameTarget(ReadWiiUTitle(candidate) ?? CleanTitle(Path.GetFileName(candidate)), rpxPath));
        }

        results.AddRange(EnumerateFilesSafely(path, SearchOption.TopDirectoryOnly)
            .Where(file => IsRomFile(file, "Wii U"))
            .Select(file => new EmulatedGameTarget(GetFileGameTitle(file, "Wii U"), file)));

        return results
            .GroupBy(target => target.LaunchPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private static string? FindPlayStation3BootFile(string candidate)
    {
        var paths = new[]
        {
            Path.Combine(candidate, "PS3_GAME", "USRDIR", "EBOOT.BIN"),
            Path.Combine(candidate, "USRDIR", "EBOOT.BIN")
        };
        return paths.FirstOrDefault(File.Exists);
    }

    private static string? ReadWiiUTitle(string gameDirectory)
    {
        var metaPath = Path.Combine(gameDirectory, "meta", "meta.xml");
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(metaPath);
            var preferredNames = new[] { "longname_pt", "longname_en", "longname" };
            foreach (var name in preferredNames)
            {
                var value = document.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Replace("\n", " ").Replace("\r", " ").Trim();
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static IEnumerable<string> EnumerateFilesSafely(string path, SearchOption searchOption)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(path, "*.*", searchOption).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafely(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).ToList()
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static string GetFileGameTitle(string filePath, string platform)
    {
        var fileTitle = Path.GetFileNameWithoutExtension(filePath);
        if (platform.Equals("Wii", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(fileTitle, @"^[A-Z0-9]{6}$", RegexOptions.IgnoreCase))
        {
            var parentTitle = Path.GetFileName(Path.GetDirectoryName(filePath));
            if (!string.IsNullOrWhiteSpace(parentTitle))
            {
                fileTitle = parentTitle;
            }
        }

        return CleanTitle(fileTitle);
    }

    private static Game CreateRomGame(string title, string platform, string romPath)
    {
        var cover = PlaceholderArtGenerator.ColorFromTitle(title);
        var accent = PlaceholderArtGenerator.AccentFrom(cover);
        return new Game
        {
            Id = $"rom-{NormalizeTitle(platform)}-{NormalizeTitle(title)}",
            Title = title,
            Platform = platform,
            SelectedLauncher = platform,
            CoverArtColor = cover,
            AccentColor = accent,
            PlayTime = "0h 00min",
            LastPlayed = File.GetLastWriteTime(romPath),
            AddedAt = GetAddedAt(romPath)
        };
    }

    private static DateTime GetAddedAt(string path)
    {
        try
        {
            return File.Exists(path)
                ? File.GetCreationTime(path)
                : Directory.Exists(path)
                    ? Directory.GetCreationTime(path)
                    : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static bool IsRomFile(string path, string? platform = null)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (platform?.Equals("Wii", StringComparison.OrdinalIgnoreCase) == true)
        {
            return extension is ".wbfs" or ".iso" or ".rvz" or ".wia" or ".gcz" or ".wad";
        }

        if (platform?.Equals("Wii U", StringComparison.OrdinalIgnoreCase) == true)
        {
            return extension is ".wua" or ".wux" or ".wud" or ".rpx";
        }

        if (platform?.Equals("PlayStation 3", StringComparison.OrdinalIgnoreCase) == true)
        {
            return extension is ".iso";
        }

        if (platform is not null)
        {
            return platform switch
            {
                "GameCube" => extension is ".iso" or ".gcm" or ".rvz" or ".gcz" or ".ciso",
                "PlayStation" or "PlayStation 1" => extension is ".cue" or ".chd" or ".pbp" or ".m3u" or ".iso" or ".bin",
                "NES" => extension is ".nes" or ".fds",
                "Game Boy" => extension is ".gb",
                "Game Boy Color" => extension is ".gbc",
                "Nintendo DS" => extension is ".nds",
                "Nintendo 3DS" => extension is ".3ds" or ".cci" or ".cia",
                "Mega Drive" or "Genesis" => extension is ".md" or ".gen" or ".smd" or ".bin",
                "Master System" => extension is ".sms",
                "Game Gear" => extension is ".gg",
                "Sega Saturn" => extension is ".cue" or ".chd" or ".iso" or ".ccd" or ".mds",
                "Dreamcast" => extension is ".gdi" or ".cdi" or ".chd",
                "Arcade" => extension is ".zip" or ".7z",
                _ => extension is ".nsp" or ".xci" or ".iso" or ".chd" or ".cso" or ".rvz" or ".z64" or ".n64" or ".v64" or ".gba" or ".gbc" or ".gb" or ".sfc" or ".smc" or ".nes" or ".bin"
            };
        }

        return extension is ".nsp" or ".xci" or ".iso" or ".chd" or ".cso" or ".rvz" or ".z64" or ".n64" or ".v64" or ".gba" or ".gbc" or ".gb" or ".sfc" or ".smc" or ".nes" or ".bin";
    }

    private static string CleanTitle(string title)
    {
        var cleaned = title
            .Replace('_', ' ')
            .Replace('.', ' ')
            .Trim();

        cleaned = Regex.Replace(cleaned, @"\s*[\(\[\{]([^\)\]\}]+)[\)\]\}]", match =>
        {
            var tag = match.Groups[1].Value.Trim();
            return IsRomMetadataTag(tag) ? string.Empty : match.Value;
        });

        cleaned = Regex.Replace(cleaned, @"\s+-\s+(Update|DLC|Demo|Beta|Proto|Sample|Trial|eShop|Digital)\b.*$", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? title.Trim() : cleaned;
    }

    private static bool IsRomMetadataTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return true;
        }

        var normalized = tag.Trim();
        var compact = normalized.Replace(" ", string.Empty);
        var lower = normalized.ToLowerInvariant();

        if (Regex.IsMatch(compact, @"^(en|ja|fr|de|es|it|pt|pt-br|zh|zh-hant|zh-hans|ko|nl|sv|no|da|fi|ru|pl|cs|hu|tr|ar)(,(en|ja|fr|de|es|it|pt|pt-br|zh|zh-hant|zh-hans|ko|nl|sv|no|da|fi|ru|pl|cs|hu|tr|ar))*$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(lower, @"^(world|usa|europe|japan|asia|korea|china|taiwan|brazil|latin america|australia|canada|uk|france|germany|spain|italy|netherlands|russia)$"))
        {
            return true;
        }

        if (Regex.IsMatch(lower, @"^(rev|revision|v|ver|version|update|upd|dlc|demo|beta|proto|prototype|sample|trial|debug|patched|translated|hack|mod|proper|repack|dump|scene|eshop|digital|cart|cartridge|xci|nsp|cia|iso|chd|rvz|cso|nkit|decrypted|encrypted|trimmed|untrimmed|clean|dirty)\b"))
        {
            return true;
        }

        if (Regex.IsMatch(lower, @"^\d+(\.\d+)*$"))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeTitle(string title) =>
        new(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private sealed record EmulatedGameTarget(string Title, string LaunchPath);
}

public sealed class EmulatorConfigStore
{
    public List<EmulatorEntry> Emulators { get; set; } = [];
    public List<RomDirectoryEntry> RomDirectories { get; set; } = [];
}

public sealed record EmulatorEntry(string Name, string ExecutablePath, string Platform);
public sealed record RomDirectoryEntry(string Path, string Platform);
