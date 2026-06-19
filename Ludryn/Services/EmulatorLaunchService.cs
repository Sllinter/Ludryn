using Ludryn.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Ludryn.Services;

public static class EmulatorLaunchService
{
    private static readonly ConcurrentDictionary<string, string> RetroArchCoreCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Process> RunningGames =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool IsEmulatorInstallation(Game game, GameInstallation installation) =>
        IsEmulatorPlatform(game.Platform) &&
        File.Exists(installation.LaunchId) &&
        (File.Exists(installation.InstallPath) || Directory.Exists(installation.InstallPath));

    public static string GetDisplayName(string executablePath)
    {
        var id = GetEmulatorId(executablePath);
        return id switch
        {
            "ryujinx" => "Ryujinx",
            "yuzu" => "Yuzu",
            "suyu" => "Suyu",
            "sudachi" => "Sudachi",
            "pcsx2" => "PCSX2",
            "rpcs3" => "RPCS3",
            "retroarch" => "RetroArch",
            "dolphin" => "Dolphin",
            "ppsspp" => "PPSSPP",
            "duckstation" => "DuckStation",
            "cemu" => "Cemu",
            "citra" => "Citra",
            "lime3ds" => "Lime3DS",
            "azahar" => "Azahar",
            "melonds" => "melonDS",
            "desmume" => "DeSmuME",
            "flycast" => "Flycast",
            "redream" => "Redream",
            "mame" => "MAME",
            _ => Path.GetFileNameWithoutExtension(executablePath)
        };
    }

    public static bool CanUseForGame(string executablePath, string platform, string romPath)
    {
        if (!File.Exists(executablePath))
        {
            return false;
        }

        if (GetEmulatorId(executablePath) != "retroarch")
        {
            return false;
        }

        return IsRetroArchPlatform(platform) &&
            !string.IsNullOrWhiteSpace(FindRetroArchCore(executablePath, platform, romPath));
    }

    public static bool Launch(Game game, GameInstallation installation)
    {
        var executable = installation.LaunchId;
        var romPath = installation.InstallPath;
        if (!File.Exists(executable) || !File.Exists(romPath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty,
            UseShellExecute = false
        };

        var emulatorId = GetEmulatorId(executable);
        switch (emulatorId)
        {
            case "yuzu":
            case "suyu":
            case "sudachi":
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("-g");
                startInfo.ArgumentList.Add(romPath);
                break;
            case "pcsx2":
                startInfo.ArgumentList.Add("-fullscreen");
                startInfo.ArgumentList.Add(romPath);
                break;
            case "rpcs3":
                startInfo.ArgumentList.Add("--no-gui");
                startInfo.ArgumentList.Add(romPath);
                break;
            case "dolphin":
                startInfo.ArgumentList.Add("-b");
                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add(romPath);
                break;
            case "ppsspp":
                startInfo.ArgumentList.Add("--fullscreen");
                startInfo.ArgumentList.Add(romPath);
                break;
            case "duckstation":
                startInfo.ArgumentList.Add("-batch");
                startInfo.ArgumentList.Add("-fullscreen");
                startInfo.ArgumentList.Add(romPath);
                break;
            case "cemu":
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("-g");
                startInfo.ArgumentList.Add(romPath);
                break;
            case "retroarch":
                var corePath = FindRetroArchCore(executable, game.Platform, romPath);
                if (string.IsNullOrWhiteSpace(corePath))
                {
                    LudrynLogger.Log(
                        "emulator",
                        $"Launch failed: no compatible RetroArch core found; Platform={game.Platform}; Emulator={executable}; ROM={romPath}");
                    return false;
                }

                startInfo.ArgumentList.Add("-L");
                startInfo.ArgumentList.Add(corePath);
                startInfo.ArgumentList.Add("--fullscreen");
                startInfo.ArgumentList.Add(romPath);
                LudrynLogger.Log(
                    "emulator",
                    $"RetroArch core selected: {corePath}; Platform={game.Platform}; ROM={romPath}");
                break;
            default:
                startInfo.ArgumentList.Add(romPath);
                break;
        }

        try
        {
            var process = Process.Start(startInfo);
            if (process is not null)
            {
                TrackRunningGame(installation, process);
            }

            LudrynLogger.Log("emulator", $"Launch: {GetDisplayName(executable)}; Platform={game.Platform}; ROM={romPath}");
            return true;
        }
        catch (Exception ex)
        {
            LudrynLogger.Log("emulator", $"Launch failed: {ex.Message}; Emulator={executable}; ROM={romPath}");
            return false;
        }
    }

    public static bool IsRunning(GameInstallation installation)
    {
        var key = GetRunningGameKey(installation);
        if (!RunningGames.TryGetValue(key, out var process))
        {
            return false;
        }

        try
        {
            if (!process.HasExited)
            {
                return true;
            }
        }
        catch
        {
            // Treat inaccessible or disposed processes as no longer running.
        }

        RunningGames.TryRemove(key, out _);
        process.Dispose();
        return false;
    }

    private static void TrackRunningGame(GameInstallation installation, Process process)
    {
        var key = GetRunningGameKey(installation);
        if (RunningGames.TryGetValue(key, out var previousProcess))
        {
            previousProcess.Dispose();
        }

        RunningGames[key] = process;
    }

    private static string GetRunningGameKey(GameInstallation installation)
    {
        var executable = NormalizePath(installation.LaunchId);
        var gamePath = NormalizePath(installation.InstallPath);
        return $"{executable}|{gamePath}";
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string GetEmulatorId(string executablePath)
    {
        var name = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();
        if (name.Contains("ryujinx")) return "ryujinx";
        if (name.Contains("yuzu")) return "yuzu";
        if (name.Contains("suyu")) return "suyu";
        if (name.Contains("sudachi")) return "sudachi";
        if (name.Contains("pcsx2")) return "pcsx2";
        if (name.Contains("rpcs3")) return "rpcs3";
        if (name.Contains("retroarch")) return "retroarch";
        if (name.Contains("dolphin")) return "dolphin";
        if (name.Contains("ppsspp")) return "ppsspp";
        if (name.Contains("duckstation")) return "duckstation";
        if (name.Contains("cemu")) return "cemu";
        if (name.Contains("lime3ds")) return "lime3ds";
        if (name.Contains("azahar")) return "azahar";
        if (name.Contains("citra")) return "citra";
        if (name.Contains("melonds")) return "melonds";
        if (name.Contains("desmume")) return "desmume";
        if (name.Contains("flycast")) return "flycast";
        if (name.Contains("redream")) return "redream";
        if (name.Equals("mame", StringComparison.OrdinalIgnoreCase) || name.StartsWith("mame", StringComparison.OrdinalIgnoreCase)) return "mame";
        return name;
    }

    private static string FindRetroArchCore(string executablePath, string platform, string romPath)
    {
        var retroArchPath = Path.GetDirectoryName(executablePath) ?? string.Empty;
        var coresPath = Path.Combine(retroArchPath, "cores");
        if (!Directory.Exists(coresPath))
        {
            return string.Empty;
        }

        var cacheKey = string.Join(
            "|",
            executablePath,
            platform,
            Path.GetExtension(romPath),
            Directory.GetLastWriteTimeUtc(coresPath).Ticks);
        if (RetroArchCoreCache.TryGetValue(cacheKey, out var cachedCore) && File.Exists(cachedCore))
        {
            return cachedCore;
        }

        var metadataMatch = FindRetroArchCoreFromMetadata(
            coresPath,
            Path.Combine(retroArchPath, "info"),
            platform,
            Path.GetExtension(romPath));
        if (!string.IsNullOrWhiteSpace(metadataMatch))
        {
            RetroArchCoreCache[cacheKey] = metadataMatch;
            return metadataMatch;
        }

        var candidates = platform switch
        {
            "Nintendo 64" or "N64" => ["mupen64plus_next_libretro.dll", "parallel_n64_libretro.dll"],
            "GameCube" or "Wii" => ["dolphin_libretro.dll"],
            "NES" => ["mesen_libretro.dll", "fceumm_libretro.dll", "nestopia_libretro.dll"],
            "Game Boy" or "Game Boy Color" => ["gambatte_libretro.dll", "mgba_libretro.dll"],
            "Nintendo DS" => ["melonds_libretro.dll", "desmume_libretro.dll"],
            "Nintendo 3DS" => ["citra_libretro.dll"],
            "Super Nintendo" or "SNES" => ["snes9x_libretro.dll", "bsnes_libretro.dll"],
            "Game Boy Advance" or "GBA" => ["mgba_libretro.dll", "vba_next_libretro.dll"],
            "PlayStation" or "PlayStation 1" or "PS1" => ["swanstation_libretro.dll", "pcsx_rearmed_libretro.dll"],
            "PlayStation 2" or "PS2" => ["pcsx2_libretro.dll"],
            "Mega Drive" or "Genesis" or "Master System" or "Game Gear" => ["genesis_plus_gx_libretro.dll", "picodrive_libretro.dll"],
            "Sega Saturn" => ["mednafen_saturn_libretro.dll", "yabause_libretro.dll"],
            "Dreamcast" => ["flycast_libretro.dll"],
            "Arcade" => ["fbneo_libretro.dll", "mame_libretro.dll", "mame2003_plus_libretro.dll"],
            "PSP" => ["ppsspp_libretro.dll"],
            _ => Array.Empty<string>()
        };

        var exactMatch = candidates
            .Select(fileName => Path.Combine(coresPath, fileName))
            .FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(exactMatch))
        {
            RetroArchCoreCache[cacheKey] = exactMatch;
            return exactMatch;
        }

        var patterns = platform switch
        {
            "Nintendo 64" or "N64" => ["*mupen64plus*_libretro.dll", "*parallel*n64*_libretro.dll"],
            "GameCube" or "Wii" => ["*dolphin*_libretro.dll"],
            "NES" => ["*mesen*_libretro.dll", "*fceumm*_libretro.dll", "*nestopia*_libretro.dll"],
            "Game Boy" or "Game Boy Color" => ["*gambatte*_libretro.dll", "*mgba*_libretro.dll"],
            "Nintendo DS" => ["*melonds*_libretro.dll", "*desmume*_libretro.dll"],
            "Nintendo 3DS" => ["*citra*_libretro.dll"],
            "Super Nintendo" or "SNES" => ["*snes9x*_libretro.dll", "*bsnes*_libretro.dll"],
            "Game Boy Advance" or "GBA" => ["*mgba*_libretro.dll", "*vba*_libretro.dll"],
            "PlayStation" or "PlayStation 1" or "PS1" => ["*swanstation*_libretro.dll", "*pcsx*rearmed*_libretro.dll"],
            "PlayStation 2" or "PS2" => ["*pcsx2*_libretro.dll"],
            "Mega Drive" or "Genesis" or "Master System" or "Game Gear" => ["*genesis*gx*_libretro.dll", "*picodrive*_libretro.dll"],
            "Sega Saturn" => ["*mednafen*saturn*_libretro.dll", "*yabause*_libretro.dll"],
            "Dreamcast" => ["*flycast*_libretro.dll"],
            "Arcade" => ["*fbneo*_libretro.dll", "*mame*_libretro.dll"],
            "PSP" => ["*ppsspp*_libretro.dll"],
            _ => Array.Empty<string>()
        };

        foreach (var pattern in patterns)
        {
            var match = Directory
                .EnumerateFiles(coresPath, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(match))
            {
                RetroArchCoreCache[cacheKey] = match;
                return match;
            }
        }

        return string.Empty;
    }

    private static bool IsRetroArchPlatform(string platform) =>
        GetRetroArchPlatformAliases(platform).Length > 0;

    private static string FindRetroArchCoreFromMetadata(
        string coresPath,
        string infoPath,
        string platform,
        string romExtension)
    {
        if (!Directory.Exists(infoPath))
        {
            return string.Empty;
        }

        var extension = romExtension.TrimStart('.').ToLowerInvariant();
        var platformAliases = GetRetroArchPlatformAliases(platform);
        if (platformAliases.Length == 0)
        {
            return string.Empty;
        }

        var matches = new List<(string CorePath, int Score)>();
        foreach (var corePath in Directory.EnumerateFiles(coresPath, "*_libretro.dll", SearchOption.TopDirectoryOnly))
        {
            var coreName = Path.GetFileNameWithoutExtension(corePath);
            var infoFile = Path.Combine(infoPath, $"{coreName}.info");
            if (!File.Exists(infoFile))
            {
                continue;
            }

            var metadata = ReadRetroArchInfo(infoFile);
            if (!metadata.TryGetValue("supported_extensions", out var extensions) ||
                !extensions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(item => string.Equals(item, extension, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var systemMetadata = string.Join(
                " ",
                GetMetadataValue(metadata, "display_name"),
                GetMetadataValue(metadata, "systemname"),
                GetMetadataValue(metadata, "systemid"),
                GetMetadataValue(metadata, "database"));
            var normalizedMetadata = NormalizeCoreMetadata(systemMetadata);
            var score = platformAliases
                .Where(alias => normalizedMetadata.Contains(alias, StringComparison.Ordinal))
                .Select(alias => 100 + alias.Length)
                .DefaultIfEmpty(0)
                .Max();
            if (score == 0)
            {
                continue;
            }

            if (GetMetadataValue(metadata, "categories").Contains("Emulator", StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            matches.Add((corePath, score));
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.CorePath, StringComparer.OrdinalIgnoreCase)
            .Select(match => match.CorePath)
            .FirstOrDefault() ?? string.Empty;
    }

    private static Dictionary<string, string> ReadRetroArchInfo(string infoFile)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadLines(infoFile))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var separator = trimmed.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = trimmed[..separator].Trim();
                var value = trimmed[(separator + 1)..].Trim().Trim('"');
                metadata[key] = value;
            }
        }
        catch (Exception ex)
        {
            LudrynLogger.Log("emulator", $"Could not read RetroArch core metadata: {infoFile}; Error={ex.Message}");
        }

        return metadata;
    }

    private static string GetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) ? value : string.Empty;

    private static string[] GetRetroArchPlatformAliases(string platform)
    {
        var aliases = platform switch
        {
            "Nintendo 64" or "N64" => ["nintendo64", "n64"],
            "GameCube" => ["nintendogamecube", "gamecube"],
            "Wii" => ["nintendowii"],
            "NES" => ["nintendoentertainmentsystem", "famicom", "nes"],
            "Game Boy" => ["nintendogameboy", "gameboy"],
            "Game Boy Color" => ["nintendogameboycolor", "gameboycolor"],
            "Nintendo DS" => ["nintendods", "nds"],
            "Nintendo 3DS" => ["nintendo3ds", "3ds"],
            "Super Nintendo" or "SNES" => ["supernintendoentertainmentsystem", "superfamicom", "supernes", "snes", "sfc"],
            "Game Boy Advance" or "GBA" => ["nintendogameboyadvance", "gameboyadvance", "gba"],
            "PlayStation" or "PlayStation 1" or "PS1" => ["sonyplaystation", "playstation", "psx"],
            "PlayStation 2" or "PS2" => ["sonyplaystation2", "playstation2", "ps2"],
            "Mega Drive" or "Genesis" => ["segamegadrive", "segagenesis", "megadrive", "genesis"],
            "Master System" => ["segamastersystem", "mastersystem"],
            "Game Gear" => ["segagamegear", "gamegear"],
            "Sega Saturn" => ["segasaturn", "saturn"],
            "Dreamcast" => ["segadreamcast", "dreamcast"],
            "Arcade" => ["arcade"],
            "PSP" => ["sonyplaystationportable", "playstationportable", "psp"],
            _ => Array.Empty<string>()
        };

        return aliases.Select(NormalizeCoreMetadata).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeCoreMetadata(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsEmulatorPlatform(string platform) =>
        platform is "Nintendo Switch"
            or "Nintendo 64"
            or "Super Nintendo"
            or "Game Boy Advance"
            or "PlayStation"
            or "PlayStation 1"
            or "PlayStation 2"
            or "PlayStation 3"
            or "Wii"
            or "Wii U"
            or "GameCube"
            or "NES"
            or "Game Boy"
            or "Game Boy Color"
            or "Nintendo DS"
            or "Nintendo 3DS"
            or "Mega Drive"
            or "Genesis"
            or "Master System"
            or "Game Gear"
            or "Sega Saturn"
            or "Dreamcast"
            or "Arcade"
            or "PSP"
            or "PS1"
            or "PS2"
            or "PS3"
            or "GBA"
            or "SNES"
            or "N64";
}
