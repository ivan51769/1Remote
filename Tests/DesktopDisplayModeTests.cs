using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using _1RM.Controls;
using _1RM.Service;
using Xunit;

namespace Tests;

public class DesktopDisplayModeTests
{
    [Theory]
    [InlineData("Example-Server-gqamz88", "Server-amz88")]
    [InlineData("Example-Server-gqamz88-外网", "Server-amz88-外网")]
    [InlineData("Example-Server-admin", "Server-admin")]
    [InlineData("Example-Server-admin-外网", "Server-admin-外网")]
    [InlineData("Other-AMZ87", "Server-amz87")]
    [InlineData("Private-GQAMZ89", "Server-amz89")]
    [InlineData("Example-Server-gqamz88 - 副本", "Server-amz88-副本")]
    [InlineData("Example-Server-admin-外网 - 导入 2", "Server-admin-外网-导入2")]
    public void PrivacyRetainsOnlyUsefulDeviceCodeAndRoute(string input, string expected)
    {
        Assert.Equal(expected, DeviceAliasSettings.PrivateDeviceName(input));
    }

    [Theory]
    [InlineData("Private-systemadmin")]
    [InlineData("Private-admin2")]
    [InlineData("Private-amz88extra")]
    [InlineData("Private-xgqamz88")]
    [InlineData("Private-unknown-device")]
    public void UnrecognizedOrPartialDeviceCodesUseStableOpaqueNames(string name)
    {
        var masked = DeviceAliasSettings.PrivateDeviceName(name);
        Assert.Matches("^Server-[0-9A-F]{6}$", masked);
        Assert.Equal(masked, DeviceAliasSettings.PrivateDeviceName(name));
        Assert.DoesNotContain("Private", masked);
    }

    [Fact]
    public void DisplayModesPersistIndependentlyAndRestoreOriginalNamesAndAliases()
    {
        WithSettings((settings, path) =>
        {
            const string device = "Private-Server-gqamz88";
            settings.SetConnectionAlias("connection-a", "示例备注");
            settings.PrivacyMode = true;
            settings.EnhancedMode = true;

            var reloaded = new DeviceAliasSettings(path);
            Assert.True(reloaded.PrivacyMode);
            Assert.True(reloaded.EnhancedMode);
            Assert.Equal("Server-amz88", reloaded.GetVisibleDeviceName(device));
            Assert.Equal("", reloaded.GetVisibleAlias("connection-a", device));

            reloaded.EnhancedMode = false;
            Assert.True(new DeviceAliasSettings(path).PrivacyMode);
            reloaded.PrivacyMode = false;
            var restored = new DeviceAliasSettings(path);
            Assert.False(restored.EnhancedMode);
            Assert.Equal(device, restored.GetVisibleDeviceName(device));
            Assert.Equal("示例备注", restored.GetVisibleAlias("connection-a", device));
        });
    }

    [Fact]
    public void CopyAliasOverrideDoesNotRenameOriginalOrOtherRoutes()
    {
        var path = Path.Combine(Path.GetTempPath(), "1remote-display-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{\"DeviceNames\":{\"gqamz88\":\"原备注\"}}");
            var settings = new DeviceAliasSettings(path);
            settings.SetConnectionAlias("copy", "副本备注");
            Assert.Equal("原备注", settings.GetAlias("original", "Device-gqamz88"));
            Assert.Equal("原备注", settings.GetAlias("external", "Device-gqamz88-外网"));
            Assert.Equal("副本备注", new DeviceAliasSettings(path).GetAlias("copy", "Device-gqamz88-副本"));

            settings.SetConnectionAlias("copy", "");
            Assert.Equal("", new DeviceAliasSettings(path).GetAlias("copy", "Device-gqamz88-副本"));
            Assert.Equal("原备注", settings.GetAlias("original", "Device-gqamz88"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(150)]
    [InlineData(210)]
    [InlineData(270)]
    [InlineData(340)]
    public void DifferentScreenshotShapesKeepUniformCardsAndOneAlignedFooter(double width)
    {
        OnSta(() =>
        {
            double? cardHeight = null;
            foreach (var dimensions in new[] { (Width: 160, Height: 90), (Width: 120, Height: 90), (Width: 90, Height: 160) })
            {
                var card = new DesktopPreviewCard();
                var preview = (Grid)card.FindName("PreviewArea");
                var image = (Image)card.FindName("PreviewImage");
                image.Source = BitmapSource.Create(dimensions.Width, dimensions.Height, 96, 96,
                    PixelFormats.Bgra32, null, new byte[dimensions.Width * dimensions.Height * 4], dimensions.Width * 4);
                var timestamp = (TextBlock)card.FindName("Timestamp");
                timestamp.Text = width < 220 ? "10-26" : "10-26 12:34";
                for (int i = 0; i < 4; i++)
                {
                    card.Measure(new Size(width, double.PositiveInfinity));
                    card.Arrange(new Rect(0, 0, width, card.DesiredSize.Height));
                    card.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                }

                Assert.Equal(Stretch.UniformToFill, image.Stretch);
                Assert.Equal(preview.ActualWidth * 9 / 16, preview.Height, 5);
                if (cardHeight.HasValue) Assert.Equal(cardHeight.Value, card.ActualHeight, 5);
                cardHeight = card.ActualHeight;

                var footer = Assert.IsType<Grid>(timestamp.Parent);
                var buttons = footer.Children.OfType<Button>().ToArray();
                Assert.Equal(new[] { "FilesButton", "LargeImageButton", "More" }, buttons.Select(b => b.Name));
                var firstPosition = buttons[0].TranslatePoint(new Point(), footer);
                Assert.True(timestamp.ActualWidth >= 25, $"Timestamp is squeezed to {timestamp.ActualWidth}px at card width {width}px.");
                Assert.True(timestamp.TranslatePoint(new Point(), footer).X + timestamp.ActualWidth <= firstPosition.X);
                Assert.All(buttons, button => Assert.Equal(firstPosition.Y, button.TranslatePoint(new Point(), footer).Y, 5));
                Assert.Equal(footer.ActualWidth, buttons[^1].TranslatePoint(new Point(), footer).X + buttons[^1].ActualWidth, 5);
            }
        });
    }

    private static void WithSettings(Action<DeviceAliasSettings, string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "1remote-display-" + Guid.NewGuid() + ".json");
        try { test(new DeviceAliasSettings(path), path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
