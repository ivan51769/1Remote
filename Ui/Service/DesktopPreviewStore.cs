using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using _1RM.Model.Protocol.Base;

namespace _1RM.Service;

/// <summary>One local, bounded preview per saved connection. Never captures the local desktop.</summary>
public static class DesktopPreviewStore
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1Remote", "DesktopPreviews");
    public static string Key(ProtocolBase server)
    {
        // Include installation and data source so independent portable databases cannot collide.
        var identity = AppContext.BaseDirectory + "\n" + server.DataSource?.DataSourceName + "\n" + server.Id;
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "");
    }
    public static string ImagePath(ProtocolBase server) => Path.Combine(Root, Key(server) + ".png");
    public static bool IsEnabled(ProtocolBase server) => !File.Exists(Path.Combine(Root, Key(server) + ".disabled"));
    public static void SetEnabled(ProtocolBase server, bool enabled)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, Key(server) + ".disabled");
        if (enabled) File.Delete(path); else { File.WriteAllText(path, "disabled"); Clear(server); }
    }
    public static void Clear(ProtocolBase server) => File.Delete(ImagePath(server));
    public static BitmapSource? Load(ProtocolBase server)
    {
        try
        {
            using var stream = File.Open(ImagePath(server), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            return image;
        }
        catch (IOException) { return null; }
        catch (Exception ex) { Shawn.Utils.SimpleLogHelper.Warning("Desktop preview read failed: " + ex.Message); return null; }
    }
    public static bool Capture(ProtocolBase server, IntPtr hwnd, int width, int height)
    {
        if (server.IsTmpSession() || !IsEnabled(server) || hwnd == IntPtr.Zero || width < 100 || height < 100 || width > 10000 || height > 10000) return false;
        using var full = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(full))
        {
            graphics.Clear(Color.Black);
            var dc = graphics.GetHdc();
            try { if (!PrintWindow(hwnd, dc, 3)) return false; }
            finally { graphics.ReleaseHdc(dc); }
        }
        return Save(server, full);
    }
    public static bool Save(ProtocolBase server, Bitmap bitmap)
    {
        if (!IsEnabled(server)) return false;
        // Reject blank/solid captures (common when a hardware accelerated RDP surface is unavailable).
        int min = 255, max = 0;
        for (int y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 24))
        for (int x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 32))
        {
            var p = bitmap.GetPixel(x, y); int l = (p.R + p.G + p.B) / 3;
            min = Math.Min(min, l); max = Math.Max(max, l);
        }
        if (max - min < 12) return false;
        var scale = Math.Min(1.0, 960.0 / bitmap.Width);
        using var thumbnail = new Bitmap(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));
        using (var g = Graphics.FromImage(thumbnail)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(bitmap, 0, 0, thumbnail.Width, thumbnail.Height); }
        Directory.CreateDirectory(Root);
        string path = ImagePath(server), temporary = path + ".tmp";
        try
        {
            thumbnail.Save(temporary, ImageFormat.Png);
            File.Move(temporary, path, true);
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
