using Microsoft.UI.Xaml.Media.Imaging;

namespace Ludryn.Services;

public static class PlatformIconService
{
    private static readonly Dictionary<string, BitmapImage> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapImage GetIcon(string platform)
    {
        var fileName = GetIconFileName(platform);
        if (IconCache.TryGetValue(fileName, out var cachedIcon))
        {
            return cachedIcon;
        }

        try
        {
            var icon = new BitmapImage(new Uri($"ms-appx:///Assets/PlatformIcons/{fileName}"));
            IconCache[fileName] = icon;
            return icon;
        }
        catch
        {
            var fallback = new BitmapImage(new Uri("ms-appx:///Assets/PlatformIcons/steam.ico"));
            IconCache[fileName] = fallback;
            return fallback;
        }
    }

    public static string GetDisplayName(string platform) => platform switch
    {
        "Yuzu" => "Nintendo Switch",
        "PCSX2" => "PlayStation 2",
        "RPCS3" => "PlayStation 3",
        "RetroArch" => "Nintendo 64",
        "Dolphin" => "Nintendo GameCube / Wii",
        "Cemu" => "Wii U",
        "Epic" => "Epic Games",
        _ => platform
    };

    private static string GetIconFileName(string platform) => platform switch
    {
        "Steam" => "steam.ico",
        "Epic" or "Epic Games" => "Epic Games.ico",
        "GOG" => "gog.ico",
        "Ubisoft Connect" => "ubisoftconnect.png",
        "EA Play" => "EAPLAY.ico",
        "Xbox" => "xbox.ico",
        "PCSX2" or "PlayStation 2" or "PS2" => "ps2.png",
        "RPCS3" or "PlayStation 3" or "PS3" => "ps3.png",
        "Yuzu" or "Nintendo Switch" => "nintendo.ico",
        "Dolphin" or "GameCube" => "gamecube.png",
        "Wii" => "wii.png",
        "Cemu" or "Wii U" => "wiiu.png",
        "RetroArch" or "Nintendo 64" or "N64" => "N64.ico",
        "GBA" or "Game Boy Advance" => "gba.ico",
        "NES" => "nes.png",
        "Game Boy" => "gb.png",
        "Game Boy Color" => "gbc.png",
        "Nintendo DS" => "nds.png",
        "Nintendo 3DS" => "3ds.png",
        "SNES" or "Super Nintendo" => "SNES.ico",
        "Mega Drive" or "Genesis" => "megadrive.png",
        "Master System" => "mastersystem.png",
        "Game Gear" => "gamegear.png",
        "Sega Saturn" => "saturn.png",
        "Dreamcast" => "dreamcast.png",
        "Arcade" => "arcade.png",
        "PSP" => "PSP.ico",
        "PlayStation" or "PlayStation 1" or "PS1" => "Playstation.ico",
        _ => "steam.ico"
    };
}
