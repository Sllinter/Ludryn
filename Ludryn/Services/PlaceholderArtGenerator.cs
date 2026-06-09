using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI;

namespace Ludryn.Services;

public static class PlaceholderArtGenerator
{
    public static WriteableBitmap CreateCover(string title, Color baseColor) =>
        CreateBitmap(400, 600, baseColor, Darken(baseColor, 0.48));

    public static WriteableBitmap CreateHero(string title, Color baseColor, Color accentColor) =>
        CreateBitmap(1280, 720, Darken(baseColor, 0.35), accentColor, horizontalGradient: true);

    public static Color ColorFromTitle(string title)
    {
        var hash = Math.Abs(title.GetHashCode());
        return Color.FromArgb(255, (byte)(80 + hash % 130), (byte)(70 + hash / 7 % 120), (byte)(100 + hash / 17 % 130));
    }

    public static Color AccentFrom(Color color) =>
        Color.FromArgb(255, (byte)Math.Min(255, color.R + 52), (byte)Math.Min(255, color.G + 68), (byte)Math.Min(255, color.B + 84));

    private static WriteableBitmap CreateBitmap(int width, int height, Color left, Color right, bool horizontalGradient = false)
    {
        var bitmap = new WriteableBitmap(width, height);
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var t = horizontalGradient ? (double)x / width : (double)y / height;
                var color = Lerp(left, right, t);
                var index = (y * width + x) * 4;
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = color.A;
            }
        }

        using var stream = bitmap.PixelBuffer.AsStream();
        stream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
        return bitmap;
    }

    private static Color Lerp(Color a, Color b, double t) =>
        Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static Color Darken(Color color, double amount) =>
        Color.FromArgb(255, (byte)(color.R * amount), (byte)(color.G * amount), (byte)(color.B * amount));
}
