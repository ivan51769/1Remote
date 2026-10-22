using System;
using System.Drawing;
using System.IO;
using _1RM.Model.Protocol;
using _1RM.Service;
using Xunit;

namespace Tests;

public class DesktopPreviewTests
{
    [Fact]
    public void LastImageSurvivesBlankCaptureAndReloadAndCanBeDisabled()
    {
        var server = new RDP { Id = "preview-test-" + Guid.NewGuid().ToString("N") };
        try
        {
            using var frame = new Bitmap(1920, 1080);
            using (var g = Graphics.FromImage(frame)) { g.Clear(Color.Navy); g.FillRectangle(Brushes.White, 0, 0, 960, 540); }
            Assert.True(DesktopPreviewStore.Save(server, frame));
            var path = DesktopPreviewStore.ImagePath(server);
            var bytes = File.ReadAllBytes(path);
            using var saved = new Bitmap(path);
            Assert.Equal(960, saved.Width); Assert.Equal(540, saved.Height);
            using var blank = new Bitmap(1920, 1080);
            Assert.False(DesktopPreviewStore.Save(server, blank));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            var reloaded = new RDP { Id = server.Id };
            Assert.Equal(path, DesktopPreviewStore.ImagePath(reloaded));
            saved.Dispose();
            DesktopPreviewStore.SetEnabled(reloaded, false);
            Assert.False(File.Exists(path));
            Assert.False(DesktopPreviewStore.Save(reloaded, frame));
            DesktopPreviewStore.SetEnabled(reloaded, true);
            Assert.True(DesktopPreviewStore.Save(reloaded, frame));
        }
        finally { DesktopPreviewStore.SetEnabled(server, true); DesktopPreviewStore.Clear(server); }
    }
    [Fact]
    public void DeviceIdsAreIsolatedAndCannotEscapeCacheDirectory()
    {
        var first = new RDP { Id = "../../escape.png" };
        var second = new RDP { Id = "second" };
        Assert.NotEqual(DesktopPreviewStore.ImagePath(first), DesktopPreviewStore.ImagePath(second));
        Assert.Equal(DesktopPreviewStore.Root, Path.GetDirectoryName(DesktopPreviewStore.ImagePath(first)));
        Assert.False(DesktopPreviewStore.Capture(first, IntPtr.Zero, 100, 100));
    }
}
