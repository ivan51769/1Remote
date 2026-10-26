using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using _1RM.Model;
using _1RM.Model.Protocol.Base;
using _1RM.Service.Locality;
using Shawn.Utils;

namespace _1RM.Service;

/// <summary>Native destinations, so Windows owns pinning, unpinning and removal.</summary>
public static class TaskbarJumpListService
{
    public const string ArgumentPrefix = "SESSION:";
    private static bool _initialized;
    private static DispatcherTimer? _retryTimer;

    public static bool ShouldRetry(IEnumerable<JumpItemRejectionReason> reasons, int attempt) =>
        attempt < 2 && reasons.Contains(JumpItemRejectionReason.InvalidItem);

    public static JumpTask CreateTask(ProtocolBase server, string executable) => new()
    {
        // Keep aliases and credentials out of the shell's persistent (including pinned) cache.
        Title = server.DisplayName,
        Description = "连接设备；已连接时返回远程窗口",
        CustomCategory = "最近连接",
        ApplicationPath = executable,
        WorkingDirectory = Path.GetDirectoryName(executable),
        Arguments = ArgumentPrefix + DesktopPreviewStore.Key(server),
        IconResourcePath = executable,
        IconResourceIndex = 0
    };

    public static List<JumpTask> BuildRecentTasks(IEnumerable<(ProtocolBase Server, DateTime LastConnection)> history,
        string executable, int limit = 5) => history
        .Where(x => !x.Server.IsTmpSession() && x.LastConnection > DateTime.MinValue)
        .OrderByDescending(x => x.LastConnection)
        .DistinctBy(x => DesktopPreviewStore.Key(x.Server))
        .Take(Math.Max(0, limit))
        .Select(x => CreateTask(x.Server, executable)).ToList();

    public static ProtocolBase? Resolve(string argument, IEnumerable<ProtocolBase> servers)
    {
        if (!argument.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var key = argument.Substring(ArgumentPrefix.Length);
        if (key.Length != 64 || key.Any(c => !Uri.IsHexDigit(c))) return null;
        return servers.FirstOrDefault(x => !x.IsTmpSession() &&
            string.Equals(DesktopPreviewStore.Key(x), key, StringComparison.OrdinalIgnoreCase));
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        IoC.Get<GlobalData>().OnReloadAll += Refresh;
        // Also refresh after Explorer or the system's recent-items preference changes.
        Application.Current.Activated += (_, _) => Refresh();
        Refresh();
    }

    public static void Refresh() => Update(null);
    public static void RecordConnection(ProtocolBase server) => Update(server);

    private static void Update(ProtocolBase? opened, int attempt = 0)
    {
        if (!_initialized || Application.Current is not { } app || app.Dispatcher.HasShutdownStarted) return;
        // Calls also arrive from the connection worker. Shell COM and WPF must run on the UI STA.
        app.Dispatcher.InvokeAsync(() =>
        {
            _retryTimer?.Stop();
            try
            {
                var executable = Environment.ProcessPath!;
                var history = IoC.Get<GlobalData>().VmItemList
                    .Select(x => (x.Server, LocalityConnectRecorder.ConnectTimeGet(x.Server)));
                var items = BuildRecentTasks(history, executable).Cast<JumpItem>().ToList();
                items.Add(new JumpTask
                {
                    Title = "打开设备列表", Description = "显示 1Remote 主窗口",
                    ApplicationPath = executable, Arguments = "activate",
                    WorkingDirectory = Path.GetDirectoryName(executable),
                    IconResourcePath = executable, IconResourceIndex = 0
                });
                var list = new JumpList(items, false, false);
                list.JumpItemsRejected += (_, e) =>
                {
                    var failures = e.RejectionReasons.Where(x => x != JumpItemRejectionReason.RemovedByUser).ToArray();
                    if (failures.Length > 0)
                    {
                        SimpleLogHelper.Warning("Taskbar destinations rejected by Windows: " + string.Join(", ", failures));
                        if (ShouldRetry(failures, attempt))
                        {
                            var timer = new DispatcherTimer(DispatcherPriority.Background, app.Dispatcher)
                            { Interval = TimeSpan.FromSeconds(attempt == 0 ? 2 : 5) };
                            _retryTimer = timer;
                            timer.Tick += (_, _) =>
                            {
                                timer.Stop();
                                if (ReferenceEquals(_retryTimer, timer)) Update(null, attempt + 1);
                            };
                            timer.Start();
                        }
                    }
                };
                if (opened != null && !opened.IsTmpSession())
                    JumpList.AddToRecentCategory(CreateTask(opened, executable));
                JumpList.SetJumpList(app, list);
            }
            catch (Exception ex)
            {
                // Shell integration must never prevent startup or a remote connection.
                SimpleLogHelper.Warning("Taskbar destination update failed: " + ex.GetType().Name);
            }
        }, DispatcherPriority.ContextIdle);
    }
}
