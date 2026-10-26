using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using _1RM.Service;
using Xunit;

namespace Tests;

public class DesktopSessionPrivacyTests
{
    [Fact]
    public void AliasPersistsAndPrivacyHidesWithoutDeletingIt()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var settings = new DeviceAliasSettings(file);
            settings.SetAlias("device-1", "Test-device01", "测试备注");
            Assert.Equal("测试备注", new DeviceAliasSettings(file).GetVisibleAlias("device-1", "Test-device01"));
            settings.PrivacyMode = true;
            var reloaded = new DeviceAliasSettings(file);
            Assert.True(reloaded.PrivacyMode);
            Assert.Equal("", reloaded.GetVisibleAlias("device-1", "Test-device01"));
            Assert.Equal("测试备注", reloaded.GetAlias("device-1", "Test-device01"));
            reloaded.PrivacyMode = false;
            Assert.Equal("测试备注", reloaded.GetVisibleAlias("device-1", "Test-device01"));
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void LocalMappingMatchesWholeDeviceCodeAndUpdatesBothRoutes()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, "{\"DeviceNames\":{\"device01\":\"示例甲\",\"device02\":\"示例乙\"}}");
            var settings = new DeviceAliasSettings(file);
            Assert.Equal("示例甲", settings.GetAlias("id-a", "Test-device01-外网"));
            Assert.Equal("", settings.GetAlias("id-b", "Test-device010"));
            settings.SetAlias("id-a", "Test-device01-外网", "新备注");
            Assert.Equal("新备注", new DeviceAliasSettings(file).GetAlias("id-c", "Test-device01"));
            settings.SetAlias("id-a", "Test-device01", "");
            Assert.Equal("", settings.GetAlias("id-c", "Test-device01-外网"));
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void BackgroundActivationRunsUiAccessOnOwningDispatcherExactlyOnce()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new TextBox();
                var frame = new DispatcherFrame();
                int calls = 0;
                var task = Task.Run(() => SessionUiDispatcher.Invoke(control, () =>
                {
                    control.VerifyAccess();
                    control.Text = "activated";
                    return ++calls;
                }));
                task.ContinueWith(_ => control.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
                var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timeout.Tick += (_, _) => frame.Continue = false;
                timeout.Start(); Dispatcher.PushFrame(frame); timeout.Stop();
                Assert.True(task.IsCompleted, "UI dispatch timed out");
                Assert.Equal(1, task.GetAwaiter().GetResult());
                Assert.Equal("activated", control.Text);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
