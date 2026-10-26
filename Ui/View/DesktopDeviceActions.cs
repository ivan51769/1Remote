using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using _1RM.Model;
using _1RM.Model.Protocol;
using _1RM.Service;
using _1RM.View.Host.ProtocolHosts;

namespace _1RM.View;

internal static class DesktopDeviceActions
{
    internal static string ValidateHost(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address != address.Trim() || address.StartsWith("-") ||
            Uri.CheckHostName(address) == UriHostNameType.Unknown) throw new InvalidOperationException("设备地址必须是有效的主机名或 IP 地址。");
        return address;
    }
    public static void OpenFiles(ProtocolBaseViewModel vm, Action<string> report)
    {
        try
        {
            if (vm.Server is not RDP rdp) return;
            var host = ValidateHost(rdp.Address);
            if (host.Contains(':')) throw new InvalidOperationException("IPv6 文件共享请使用该设备的 DNS 主机名。");
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = "\\\\" + host });

        }
        catch (Exception ex) { report("打开文件共享失败：" + ex.Message); }
    }
    public static void ShowMenu(ProtocolBaseViewModel vm, FrameworkElement anchor, Window owner, Action<string> report, Action refresh)
    {
        var menu = new ContextMenu();
        void Add(string title, Action action) { var item = new MenuItem { Header = title, FontSize = 12 }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        var aliasItem = new MenuItem { Header = "编辑备注名", FontSize = 12, IsEnabled = !DeviceAliasSettings.Current.PrivacyMode,
            ToolTip = "备注仅保存在本机；编辑前请关闭隐私模式" };
        aliasItem.Click += (_, _) => EditAlias(vm, owner);
        menu.Items.Add(aliasItem);
        Add("连接设置", () => vm.CmdEditServer.Execute(null));
        Add("复制副本", () => GlobalEventHelper.OnRequestGoToServerDuplicatePage?.Invoke(vm.Server, false));
        menu.Items.Add(new Separator());
        Add("重启远程电脑…", () => ConfirmPower(vm, "restart", owner, report));
        Add("关闭远程电脑…", () => ConfirmPower(vm, "shutdown", owner, report));
        Add("取消远程关机 / 重启…", () => ConfirmPower(vm, "abort", owner, report));
        menu.Items.Add(new Separator());
        Add("清除最后画面", () => { try { DesktopPreviewStore.Clear(vm.Server); refresh(); } catch (Exception ex) { report(ex.Message); } });
        bool enabled = DesktopPreviewStore.IsEnabled(vm.Server);
        Add(enabled ? "关闭此设备的预览保存" : "开启此设备的预览保存", () =>
        { try { DesktopPreviewStore.SetEnabled(vm.Server, !enabled); refresh(); } catch (Exception ex) { report(ex.Message); } });
        menu.PlacementTarget = anchor; menu.IsOpen = true;
    }
    private static void EditAlias(ProtocolBaseViewModel vm, Window owner)
    {
        if (DeviceAliasSettings.Current.PrivacyMode) return;
        var key = DesktopPreviewStore.Key(vm.Server);
        var editor = new TextBox { Text = DeviceAliasSettings.Current.GetAlias(key, vm.DisplayName), MaxLength = 64,
            Height = 32, Margin = new Thickness(0, 10, 0, 8), Background = System.Windows.Media.Brushes.White,
            Foreground = System.Windows.Media.Brushes.Black, FontSize = 14 };
        var body = new StackPanel { Margin = new Thickness(20) };
        var window = new Window { Title = "编辑备注名", Owner = owner, Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.White, Foreground = System.Windows.Media.Brushes.Black,
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"), Content = body };
        body.Children.Add(new TextBlock { Text = vm.DisplayName, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Black });
        body.Children.Add(editor);
        body.Children.Add(new TextBlock { Text = "留空可清除备注。备注只保存在本机，隐私模式下隐藏。", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = System.Windows.Media.Brushes.DimGray });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var save = new Button { Content = "保存", IsDefault = true, MinWidth = 70, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 70 };
        save.Click += (_, _) =>
        {
            try { DeviceAliasSettings.Current.SetAlias(key, vm.DisplayName, editor.Text); window.DialogResult = true; }
            catch (Exception) { MessageBox.Show(window, "备注保存失败，请检查本地配置目录是否可写。", "1Remote Preview", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        buttons.Children.Add(save); buttons.Children.Add(cancel); body.Children.Add(buttons);
        window.Loaded += (_, _) => { editor.Focus(); editor.SelectAll(); };
        window.ShowDialog();
    }
    public static async void ConfirmPower(ProtocolBaseViewModel vm, string action, Window owner, Action<string> report)
    {
        if (vm.Server is not RDP rdp) return;
        string verb = action == "restart" ? "重启" : action == "shutdown" ? "关机" : "取消关机 / 重启";
        try
        {
            // Resolve and pin one remote IP before confirmation. Never fall back to a local command.
            var address = ValidateHost(rdp.Address);
            var targets = await Dns.GetHostAddressesAsync(address);
            var locals = await Dns.GetHostAddressesAsync(Dns.GetHostName());
            if (targets.Length == 0 || targets.Any(x => IPAddress.IsLoopback(x) || locals.Contains(x) || x.Equals(IPAddress.Any) || x.Equals(IPAddress.IPv6Any)))
                throw new InvalidOperationException("拒绝操作：目标解析到了本机或无效地址。");
            var target = targets.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new InvalidOperationException("远程电源操作需要目标可解析的 IPv4 地址。");
            var message = $"目标：{vm.DisplayName}\n地址：{address} ({target})\n\n确认{verb}这台远程电脑？\n" +
                (action == "abort" ? "取消指令仅在目标存在尚未结束的关机倒计时时有效。" : "确认后立即发出指令，不强制关闭应用；请先保存远端工作。") +
                "\n\n使用当前 Windows 账户的远程关机权限，目标还需允许 Windows 远程管理。RDP 能登录并不代表有此权限。";
            if (MessageBox.Show(owner, message, "确认远程" + verb, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(action == "restart" ? "/r" : action == "shutdown" ? "/s" : "/a");
            start.ArgumentList.Add("/m"); start.ArgumentList.Add("\\\\" + target);
            // A nonzero shutdown.exe timeout implicitly enables /f. Use zero to avoid forced data loss.
            if (action != "abort") { start.ArgumentList.Add("/t"); start.ArgumentList.Add("0"); }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动远程操作。");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var detail = (await output) + (await error);
            report(process.ExitCode == 0 ? $"{vm.DisplayName}：{verb}指令已被接受；尚未验证远端实际状态。" : $"{vm.DisplayName}：{verb}失败（{process.ExitCode}）。{detail.Trim()}");
        }
        catch (Exception ex) { report("远程操作未执行：" + ex.Message); }
    }
}
