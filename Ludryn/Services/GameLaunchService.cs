using Ludryn.Models;
using Microsoft.Win32;
using System.Diagnostics;

namespace Ludryn.Services;

public sealed class GameLaunchService
{
    public bool Launch(Game game)
    {
        var installation = GetSelectedInstallation(game);
        if (installation is null)
        {
            return false;
        }

        if (EmulatorLaunchService.IsEmulatorInstallation(game, installation))
        {
            return EmulatorLaunchService.Launch(game, installation);
        }

        if (string.Equals(installation.Launcher, "Steam", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchSteamGame(installation.LaunchId);
        }

        if (string.Equals(installation.Launcher, "Epic", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(installation.LaunchId))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"com.epicgames.launcher://apps/{installation.LaunchId}?action=launch&silent=true",
                UseShellExecute = true
            });
            return true;
        }

        if (installation.Launcher is "GOG" or "Ubisoft Connect" or "EA Play")
        {
            return LaunchExecutableInstallation(installation);
        }

        if (string.Equals(installation.Launcher, "Xbox", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchWindowsGame(installation);
        }

        if (File.Exists(installation.LaunchId))
        {
            return LaunchExecutableInstallation(installation);
        }

        return false;
    }

    public bool IsRunning(Game game)
    {
        var installation = GetSelectedInstallation(game);
        if (installation is null || string.IsNullOrWhiteSpace(installation.InstallPath) || !Directory.Exists(installation.InstallPath))
        {
            return installation is not null &&
                EmulatorLaunchService.IsEmulatorInstallation(game, installation) &&
                EmulatorLaunchService.IsRunning(installation);
        }

        var installPath = Path.GetFullPath(installation.InstallPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var fileName = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                var processPath = Path.GetFullPath(fileName);
                if (processPath.StartsWith(installPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Some processes do not expose MainModule without elevated permissions.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    private static GameInstallation? GetSelectedInstallation(Game game)
    {
        if (game.Installations.Count == 0)
        {
            return null;
        }

        return game.Installations.FirstOrDefault(i =>
            string.Equals(i.Launcher, game.SelectedLauncher, StringComparison.OrdinalIgnoreCase))
            ?? game.Installations[0];
    }

    private static bool LaunchSteamGame(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return false;
        }

        var steamExe = FindSteamExecutable();
        if (!string.IsNullOrWhiteSpace(steamExe) && File.Exists(steamExe))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                Arguments = $"-silent -applaunch {appId}",
                UseShellExecute = true
            });
            return true;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://rungameid/{appId}",
            UseShellExecute = true
        });
        return true;
    }

    private static string FindSteamExecutable()
    {
        var registryPaths = new[]
        {
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
        };

        foreach (var registryPath in registryPaths)
        {
            var installPath = Registry.GetValue(registryPath, "SteamPath", null) as string
                ?? Registry.GetValue(registryPath, "InstallPath", null) as string;
            if (!string.IsNullOrWhiteSpace(installPath))
            {
                var steamExe = Path.Combine(installPath, "steam.exe");
                if (File.Exists(steamExe))
                {
                    return steamExe;
                }
            }
        }

        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe");
        return File.Exists(defaultPath) ? defaultPath : string.Empty;
    }

    private static bool LaunchExecutableInstallation(GameInstallation installation)
    {
        var executable = File.Exists(installation.LaunchId)
            ? installation.LaunchId
            : FindExecutableInInstallPath(installation.InstallPath);

        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = installation.LaunchArguments,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? installation.InstallPath,
            UseShellExecute = true
        });
        return true;
    }

    private static bool LaunchWindowsGame(GameInstallation installation)
    {
        if (File.Exists(installation.LaunchId))
        {
            return LaunchExecutableInstallation(installation);
        }

        if (!string.IsNullOrWhiteSpace(installation.LaunchId))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"shell:appsFolder\\{installation.LaunchId}",
                UseShellExecute = true
            });
            return true;
        }

        return false;
    }

    private static string FindExecutableInInstallPath(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return string.Empty;
        }

        try
        {
            return Directory.EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).Contains("unins", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .ThenByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
