using Microsoft.UI.Xaml.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Windows.Storage.Streams;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Ludryn.Services;

public static class ApplicationIconService
{
    private static readonly string IconCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ludryn",
        "ApplicationIcons");

    public static async Task<BitmapImage?> GetIconAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var cachePath = GetCachePath(path);
            if (!File.Exists(cachePath))
            {
                var iconBytes = GetShellIconBytes(path);
                if (iconBytes is not null)
                {
                    Directory.CreateDirectory(IconCacheDirectory);
                    await File.WriteAllBytesAsync(cachePath, iconBytes);
                }
            }

            if (File.Exists(cachePath))
            {
                return await LoadBitmapAsync(cachePath);
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                256,
                ThumbnailOptions.UseCurrentScale);
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return null;
            }

            thumbnail.Seek(0);
            using (var reader = new DataReader(thumbnail))
            {
                var thumbnailSize = checked((uint)thumbnail.Size);
                await reader.LoadAsync(thumbnailSize);
                var bytes = new byte[thumbnailSize];
                reader.ReadBytes(bytes);
                Directory.CreateDirectory(IconCacheDirectory);
                await File.WriteAllBytesAsync(cachePath, bytes);
            }

            return await LoadBitmapAsync(cachePath);
        }
        catch (Exception ex)
        {
            LudrynLogger.Log("library", $"Falha ao carregar ícone de '{path}': {ex.Message}");
            return null;
        }
    }

    public static string GetCachedIconPath(string path)
    {
        var cachePath = GetCachePath(path);
        return File.Exists(cachePath) ? cachePath : string.Empty;
    }

    public static BitmapImage? LoadCachedIcon(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        try
        {
            return new BitmapImage(new Uri(iconPath, UriKind.Absolute));
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? GetShellIconBytes(string path)
    {
        var itemId = typeof(IShellItemImageFactory).GUID;
        var result = SHCreateItemFromParsingName(path, IntPtr.Zero, ref itemId, out var factory);
        if (result != 0 || factory is null)
        {
            return null;
        }

        IntPtr bitmapHandle = IntPtr.Zero;
        try
        {
            factory.GetImage(
                new NativeSize { Width = 256, Height = 256 },
                ShellImageFlags.BiggerSizeOk | ShellImageFlags.IconOnly,
                out bitmapHandle);
            if (bitmapHandle == IntPtr.Zero)
            {
                return null;
            }

            using var bitmap = Image.FromHbitmap(bitmapHandle);
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            return memory.ToArray();
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }

            Marshal.FinalReleaseComObject(factory);
        }
    }

    private static async Task<BitmapImage> LoadBitmapAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var image = new BitmapImage
        {
            DecodePixelWidth = 256,
            DecodePixelHeight = 256
        };
        await image.SetSourceAsync(stream);
        return image;
    }

    private static string GetCachePath(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())));
        return Path.Combine(IconCacheDirectory, $"{hash}-v2.png");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [Flags]
    private enum ShellImageFlags
    {
        BiggerSizeOk = 0x1,
        IconOnly = 0x4
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, ShellImageFlags flags, out IntPtr bitmapHandle);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? shellItem);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objectHandle);
}
