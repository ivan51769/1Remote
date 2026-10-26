using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace _1RM.Service;

internal static class WindowsDesktopWallpaper
{
    private static readonly Lazy<BitmapSource?> Cached = new(Load);
    public static BitmapSource? Image => Cached.Value;

    private static BitmapSource? Load()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Wallpaper", "Windows", "img0.jpg");
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 960; image.StreamSource = stream; image.EndInit(); image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }
}
