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
    public DesktopPreviewCard()
    {
        InitializeComponent();
        PreviewArea.SizeChanged += (_, _) => UpdatePreviewHeight();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }
    private void Refresh()
    {
        if (DeviceName == null || ProtocolBaseViewModel is not { } vm) return;
        DeviceName.Text = vm.DisplayName; DeviceName.ToolTip = vm.DisplayName + "\n" + vm.SubTitle;
        var path = DesktopPreviewStore.ImagePath(vm.Server);
        var time = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.MinValue;
        if (path != _path || time != _modified)
        {
            PreviewImage.Source = DesktopPreviewStore.Load(vm.Server); _path = path; _modified = time;
            UpdatePreviewHeight();
        }
        Empty.Visibility = PreviewImage.Source == null ? Visibility.Visible : Visibility.Collapsed;
        Timestamp.Text = !DesktopPreviewStore.IsEnabled(vm.Server) ? "预览保存已关闭" : PreviewImage.Source == null ? "尚无预览" : $"最后画面  {time:MM-dd HH:mm:ss}";
        var connected = IoC.Get<SessionControlService>().ConnectionId2Hosts.Values.Any(x =>
            x.ProtocolServer.Id == vm.Id && x.ProtocolServer.DataSource?.DataSourceName == vm.DataSourceName && x.Status == ProtocolHostStatus.Connected);
        StateDot.Foreground = new SolidColorBrush(connected ? Color.FromRgb(0, 177, 125) : Color.FromRgb(157, 174, 189));
        StateDot.ToolTip = connected ? "已连接" : "未连接（缩略图为历史画面）";
    }
    private void UpdatePreviewHeight()
    {
        if (PreviewArea.ActualWidth <= 0) return;
        double ratio = PreviewImage.Source is BitmapSource image && image.PixelWidth > 0
            ? (double)image.PixelHeight / image.PixelWidth : 9.0 / 16;
        PreviewArea.Height = PreviewArea.ActualWidth * ratio;
    }
    private void Report(string text) => MessageBox.Show(Window.GetWindow(this), text, "1Remote Preview", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ProtocolBaseViewModel?.CmdConnServer?.Execute(null);
    }
    private void Files_Click(object sender, RoutedEventArgs e) { e.Handled = true; if (ProtocolBaseViewModel is { } vm) DesktopDeviceActions.OpenFiles(vm, Report); }
    private void Image_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (PreviewImage.Source == null) { Report("还没有桌面预览，请先连接一次并保持桌面可见至少 5 秒。"); return; }
        new Window { Title = "最后画面 · " + ProtocolBaseViewModel?.DisplayName, Width = 1000, Height = 680, Background = Brushes.Black,
            Content = new Image { Source = PreviewImage.Source, Stretch = Stretch.Uniform }, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ProtocolBaseViewModel is { } vm) DesktopDeviceActions.ShowMenu(vm, More, Window.GetWindow(this), Report, Refresh);
    }
}
