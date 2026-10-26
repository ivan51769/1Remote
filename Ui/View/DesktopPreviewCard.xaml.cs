using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using _1RM.Service;
using _1RM.View;
using _1RM.View.Host.ProtocolHosts;

namespace _1RM.Controls;

public partial class DesktopPreviewCard : UserControl
{
    public static readonly DependencyProperty ProtocolBaseViewModelProperty = DependencyProperty.Register(
        nameof(ProtocolBaseViewModel), typeof(ProtocolBaseViewModel), typeof(DesktopPreviewCard),
        new PropertyMetadata(null, (d, _) => ((DesktopPreviewCard)d).Refresh()));
    public ProtocolBaseViewModel? ProtocolBaseViewModel
    {
        get => (ProtocolBaseViewModel?)GetValue(ProtocolBaseViewModelProperty);
        set => SetValue(ProtocolBaseViewModelProperty, value);
    }
    private readonly DispatcherTimer _timer;
    private string _path = "";
    private DateTime _modified;
    private bool _selectionGesture;
    public DesktopPreviewCard()
    {
        InitializeComponent();
        DefaultWallpaper.Source = WindowsDesktopWallpaper.Image;
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                _selectionGesture = DesktopCardSelection.IsSelectionGesture(Keyboard.Modifiers);
        }), true);
        PreviewArea.SizeChanged += (_, _) => UpdatePreviewHeight();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { DeviceAliasSettings.Current.Changed += AliasSettingsChanged; Refresh(); _timer.Start(); };
        Unloaded += (_, _) => { DeviceAliasSettings.Current.Changed -= AliasSettingsChanged; _timer.Stop(); };
    }
    private void AliasSettingsChanged() => Dispatcher.InvokeAsync(Refresh);
    private void Refresh()
    {
        if (DeviceName == null || ProtocolBaseViewModel is not { } vm) return;
        DeviceName.Text = DeviceAliasSettings.Current.GetVisibleDeviceName(vm.DisplayName);
        DeviceName.ToolTip = DeviceAliasSettings.Current.PrivacyMode ? DeviceName.Text : vm.DisplayName + "\n" + vm.SubTitle;
        ConnectionDetails.Text = vm.Server is _1RM.Model.Protocol.Base.ProtocolBaseWithAddressPort network
            ? (network.Address.Contains(':') ? $"[{network.Address}]:{network.Port}" : $"{network.Address}:{network.Port}") : "";
        ConnectionDetails.Visibility = DeviceAliasSettings.Current.EnhancedMode ? Visibility.Visible : Visibility.Collapsed;
        AliasName.Text = DeviceAliasSettings.Current.GetVisibleAlias(DesktopPreviewStore.Key(vm.Server), vm.DisplayName);
        AliasName.Visibility = string.IsNullOrEmpty(AliasName.Text) ? Visibility.Collapsed : Visibility.Visible;
        var path = DesktopPreviewStore.ImagePath(vm.Server);
        var time = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.MinValue;
        if (path != _path || time != _modified)
        {
            PreviewImage.Source = DesktopPreviewStore.Load(vm.Server); _path = path; _modified = time;
            UpdatePreviewHeight();
        }
        DefaultWallpaper.Visibility = PreviewImage.Source == null ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = PreviewImage.Source == null && DefaultWallpaper.Source == null ? Visibility.Visible : Visibility.Collapsed;
        UpdateFooter();
        var hosts = IoC.Get<SessionControlService>().ConnectionId2Hosts.Values.Where(x =>
            x.ProtocolServer.Id == vm.Id && x.ProtocolServer.DataSource?.DataSourceName == vm.DataSourceName).ToArray();
        var connected = hosts.Any(x => x.Status == ProtocolHostStatus.Connected);
        var connecting = !connected && hosts.Any(x => x.Status != ProtocolHostStatus.Disconnected);
        StateDot.Foreground = new SolidColorBrush(connected ? Color.FromRgb(0, 177, 125) : connecting ? Color.FromRgb(208, 145, 37) : Color.FromRgb(157, 174, 189));
        string hint = connected ? "已连接，单击返回远程桌面" : connecting ? "正在连接，单击查看连接窗口" : "未连接，单击连接远程桌面";
        StateDot.ToolTip = hint;
        PreviewArea.ToolTip = hint + (PreviewImage.Source == null ? "\n默认壁纸，尚未保存远程画面" : "\n预览是最后保存的画面，不是实时画面") + "\nCtrl / Shift 单击多选，回车打开所选设备";
        EmptyText.Text = connected ? "已连接 · 单击返回桌面" : connecting ? "正在连接…" : "单击连接并保存预览";
    }
    private void UpdatePreviewHeight()
    {
        if (PreviewArea.ActualWidth <= 0) return;
        PreviewArea.Height = PreviewArea.ActualWidth * 9.0 / 16;
        UpdateFooter();
    }
    private void UpdateFooter()
    {
        bool compact = PreviewArea.ActualWidth > 0 && PreviewArea.ActualWidth < 200;
        FilesButton.Width = LargeImageButton.Width = compact ? 22 : 38;
        More.Width = compact ? 22 : 25;
        FilesButton.Content = compact ? "文" : "文件";
        LargeImageButton.Content = compact ? "图" : "大图";
        if (ProtocolBaseViewModel is not { } vm) return;
        Timestamp.Text = vm.LastConnectTime == DateTime.MinValue ? "" : vm.LastConnectTime.ToString(compact ? "MM-dd" : "MM-dd HH:mm");
        Timestamp.ToolTip = vm.LastConnectTime == DateTime.MinValue ? null : $"最后连接时间：{vm.LastConnectTime:yyyy-MM-dd HH:mm:ss}";
    }
    private void Report(string text) => MessageBox.Show(Window.GetWindow(this), text, "1Remote Preview", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_selectionGesture || DesktopCardSelection.IsSelectionGesture(Keyboard.Modifiers)) return;
        ProtocolBaseViewModel?.CmdConnServer?.Execute(null);
    }
    private void Selection_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _1RM.View.ServerView.ServerListPageView.ItemsCheckBox_OnClick_Static(sender, e);
    }
    private void Files_Click(object sender, RoutedEventArgs e) { e.Handled = true; if (ProtocolBaseViewModel is { } vm) DesktopDeviceActions.OpenFiles(vm, Report); }
    private void Image_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (PreviewImage.Source == null) { Report("还没有桌面预览，请先连接一次并保持桌面可见至少 5 秒。"); return; }
        new Window { Title = "最后画面 · " + DeviceName.Text, Width = 1000, Height = 680, Background = Brushes.Black,
            Content = new Image { Source = PreviewImage.Source, Stretch = Stretch.Uniform }, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ProtocolBaseViewModel is { } vm) DesktopDeviceActions.ShowMenu(vm, More, Window.GetWindow(this), Report, Refresh);
    }
}
