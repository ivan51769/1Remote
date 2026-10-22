using System;
using System.Windows;
using System.Windows.Threading;
using _1RM.Service;
using Shawn.Utils;

namespace _1RM.View.Host.ProtocolHosts;

public sealed partial class AxMsRdpClient09Host
{
    private DispatcherTimer? _desktopPreviewTimer;
    private bool _desktopPreviewLoggedIn;
    private void StartDesktopPreview()
    {
        _desktopPreviewLoggedIn = true;
        _desktopPreviewTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            (_, _) => CaptureDesktopPreview(), Dispatcher);
        _desktopPreviewTimer.Start();
    }
    private void StopDesktopPreview(bool captureLast)
    {
        if (captureLast) CaptureDesktopPreview();
        _desktopPreviewLoggedIn = false;
        _desktopPreviewTimer?.Stop();
    }
    public bool CaptureDesktopPreview()
    {
        if (!Dispatcher.CheckAccess()) return false;
        try
        {
            if (!_desktopPreviewLoggedIn || _rdpClient == null || _rdpClient.IsDisposed || _rdpClient.Connected != 1
                || !IsVisible || ParentWindow?.WindowState == WindowState.Minimized || !_rdpClient.Visible) return false;
            return DesktopPreviewStore.Capture(ProtocolServer, _rdpClient.Handle, _rdpClient.ClientSize.Width, _rdpClient.ClientSize.Height);
        }
        catch (Exception ex) { SimpleLogHelper.Warning("Desktop preview capture failed: " + ex.Message); return false; }
    }
}
