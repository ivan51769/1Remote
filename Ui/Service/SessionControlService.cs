using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using _1RM.Model;
using _1RM.Model.Protocol.Base;
using _1RM.Utils;
using _1RM.View.Host;
using _1RM.View.Host.ProtocolHosts;
using Shawn.Utils;
using Stylet;
using ProtocolHostStatus = _1RM.View.Host.ProtocolHosts.ProtocolHostStatus;
using _1RM.Service.DataSource;
using _1RM.Service.Locality;
using _1RM.Service.DataSource.DAO.Dapper;

namespace _1RM.Service
{
    public partial class SessionControlService
    {
        private readonly DataSourceService _sourceService;
        private readonly ConfigurationService _configurationService;
        private readonly GlobalData _appData;

        public SessionControlService(DataSourceService sourceService, ConfigurationService configurationService, GlobalData appData)
        {
            _sourceService = sourceService;
            _configurationService = configurationService;
            _appData = appData;
            GlobalEventHelper.OnRequestServerConnect += this.OnRequestOpenConnection;
            GlobalEventHelper.OnRequestQuickConnect += this.OnRequestOpenConnection;
            GlobalEventHelper.OnRequestServersConnect += this.OnRequestOpenConnection;
        }

        public void Release()
        {
            // Unsubscribe from static events to prevent memory leaks
            GlobalEventHelper.OnRequestServerConnect -= this.OnRequestOpenConnection;
            GlobalEventHelper.OnRequestQuickConnect -= this.OnRequestOpenConnection;
            GlobalEventHelper.OnRequestServersConnect -= this.OnRequestOpenConnection;

            lock (_dictLock)
            {
                foreach (var tabWindow in _token2TabWindows.ToArray())
                {
                    Execute.OnUIThreadSync(() => tabWindow.Value.Hide());
                }
                foreach (var kv in _connectionId2FullScreenWindows.ToArray())
                {
                    Execute.OnUIThreadSync(() => kv.Value.Hide());
                }
            }
            this.CloseProtocolHostAsync(_connectionId2Hosts.Keys.ToArray());
        }

        private string _lastTabToken = "";

        private readonly object _dictLock = new object();
        private readonly ConcurrentDictionary<string, TabWindowView> _token2TabWindows = new ConcurrentDictionary<string, TabWindowView>();
        private readonly ConcurrentDictionary<string, HostBase> _connectionId2Hosts = new ConcurrentDictionary<string, HostBase>();
        private readonly ConcurrentDictionary<string, FullScreenWindowView> _connectionId2FullScreenWindows = new ConcurrentDictionary<string, FullScreenWindowView>();
        private readonly ConcurrentQueue<HostBase> _hostToBeDispose = new ConcurrentQueue<HostBase>();
        private readonly ConcurrentQueue<Window> _windowToBeDispose = new ConcurrentQueue<Window>();

        public int TabWindowCount
        {
            get
            {
                lock (_dictLock)
                {
                    return _token2TabWindows.Count;
                }
            }
        }

        public ConcurrentDictionary<string, HostBase> ConnectionId2Hosts => _connectionId2Hosts;


        private void OnRequestOpenConnection(in ProtocolBase serverOrg, in string fromView, in string assignTabToken = "", in string assignRunnerName = "", in string assignCredentialName = "")
        {
            CleanupProtocolsAndWindows();

            var org = serverOrg;
            var view = fromView;
            var tabToken = assignTabToken;
            var runnerName = assignRunnerName;
            var credentialName = assignCredentialName;
            Task.Factory.StartNew(async () =>
            {
                await Connect(org, view, tabToken, runnerName, credentialName);
            })
            .Unwrap() // observe the inner task: exceptions thrown by the async lambda land on it, not on the outer Task<Task>, so the ContinueWith below would never see them without this
            .ContinueWith(t =>
            {
                SimpleLogHelper.Fatal(t.Exception!);
                Execute.OnUIThread(() =>
                    MessageBoxHelper.ErrorAlert($"未能打开远程桌面。\n{t.Exception!.GetBaseException().Message}\n\n请重试；如果仍然失败，可在日志中查看详细原因。"));
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void OnRequestOpenConnection(IEnumerable<ProtocolBase> protocolBases, in string fromView, in string assignTabToken = "", in string assignRunnerName = "", in string assignCredentialName = "")
        {
            CleanupProtocolsAndWindows();
            var view = fromView;
            var tabToken = assignTabToken;
            var runnerName = assignRunnerName;
            var credentialName = assignCredentialName;
            Task.Factory.StartNew(async () =>
            {
                foreach (var org in protocolBases)
                {
                    tabToken = await Connect(org, view, tabToken, runnerName, credentialName);
                }
            })
            .Unwrap() // observe the inner task: exceptions thrown by the async lambda land on it, not on the outer Task<Task>, so the ContinueWith below would never see them without this
            .ContinueWith(t =>
            {
                SimpleLogHelper.Fatal(t.Exception!);
                Execute.OnUIThread(() =>
                    MessageBoxHelper.ErrorAlert($"未能打开远程桌面。\n{t.Exception!.GetBaseException().Message}\n\n请重试；如果仍然失败，可在日志中查看详细原因。"));
            }, TaskContinuationOptions.OnlyOnFaulted);
        }


        private void OnRequestCloseConnection(string connectionId)
        {
            this.CloseProtocolHostAsync(connectionId);
        }


        public bool TryActivateExistingSession(ProtocolBase server)
        {
            if (server.IsTmpSession() || !server.IsOnlyOneInstance()) return false;
            var host = _connectionId2Hosts.Values.FirstOrDefault(x =>
                x.ProtocolServer.Id == server.Id &&
                x.ProtocolServer.DataSource?.DataSourceName == server.DataSource?.DataSourceName &&
                x.Status != ProtocolHostStatus.Disconnected);
            return host != null && ActivateExistingHost(host, false);
        }

        private bool ActivateOrReConnIfServerSessionIsOpened(in ProtocolBase server)
        {
            if (!server.IsOnlyOneInstance()) return false;
            return _connectionId2Hosts.TryGetValue(server.BuildConnectionId(), out var host)
                && ActivateExistingHost(host, true);
        }

        private bool ActivateExistingHost(HostBase host, bool reconnect)
        {
            // All WPF reads and mutations, including tab selection and reconnection,
            // must run on the host's dispatcher, not the background connect task.
            bool stale = false;
            bool activated = SessionUiDispatcher.Invoke(host, () =>
            {
                var win = host.ParentWindow;
                if (win == null) return host.Status != ProtocolHostStatus.Disconnected;
                if (win.IsClosed || win.IsClosing) { stale = true; return false; }
                if (win is TabWindowView tab)
                {
                    var item = tab.GetViewModel().Items.FirstOrDefault(x => ReferenceEquals(x.Content, host));
                    if (item != null) tab.GetViewModel().SelectedItem = item;
                }
                if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
                win.Show();
                win.ShowInTaskbar = true;
                win.Activate();
                host.FocusOnMe();
                // A connection already in progress must not be restarted by another click.
                if (reconnect && host.Status == ProtocolHostStatus.Disconnected) host.ReConn();
                return true;
            });
            if (stale)
            {
                MarkProtocolHostToClose(new[] { host.ConnectionId });
                CleanupProtocolsAndWindows();
            }
            return activated;
        }

        #region CloseProtocol

        public void CloseProtocolHostAsync(string connectionId)
        {
            CloseProtocolHostAsync(new[] { connectionId });
        }
        public void CloseProtocolHostAsync(string[] connectionIds)
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    MarkProtocolHostToClose(connectionIds);
                    CleanupProtocolsAndWindows();
                }
                catch (Exception e)
                {
                    // a failure here used to vanish silently (no UI feedback, no log), leaving half-closed sessions behind
                    SimpleLogHelper.Fatal(e);
                }
            });
        }
        private void MarkProtocolHostToClose(string[] connectionIds)
        {
            var tabsToHide = new List<(string key, TabWindowView tab)>();
            // Collect tabs whose UI item removal must be deferred: calling TryRemoveItem dispatches
            // to the UI thread and blocks (OnUIThreadSync). Doing that while holding _dictLock
            // deadlocks whenever the UI thread is itself waiting on _dictLock (e.g. a concurrent
            // connect calling CleanupProtocolsAndWindows). Same reason as the tabsToHide fix below.
            var tabsToRemoveItem = new List<(string key, TabWindowView tab, string connectionId)>();
            // Full-screen windows to ShowOrHide after the lock (ShowOrHide must run on the UI thread,
            // and dispatching while holding _dictLock has the same deadlock risk).
            var fullScreensToShowOrHide = new List<FullScreenWindowView>();

            lock (_dictLock)
            {
                foreach (var connectionId in connectionIds)
                {
                    if (!_connectionId2Hosts.TryRemove(connectionId, out var host)) continue;

                    SimpleLogHelper.Debug($@"MarkProtocolHostToClose: marking to close: {host.GetType().Name}(id = {connectionId}, hash = {host.GetHashCode()})");

                    host.OnClosed -= OnRequestCloseConnection;
                    host.OnFullScreen2Window -= this.MoveSessionToTabWindow;
                    _hostToBeDispose.Enqueue(host);
                    host.ProtocolServer.RunScriptAfterDisconnected();
                    PrintCacheCount();

#if NETFRAMEWORK
                    foreach (var kv in _token2TabWindows.ToArray())
                    {
                        var key = kv.Key;
                        var tab = kv.Value;
#else
                    foreach (var (key, tab) in _token2TabWindows.ToArray())
                    {
#endif
                        // detect only, defer the UI round-trip to after the lock
                        if (tab.GetViewModel().Items.Any(x => x.Content.ConnectionId == connectionId))
                        {
                            tabsToRemoveItem.Add((key, tab, connectionId));
                        }
                    }

                    // hide full
#if NETFRAMEWORK
                    foreach (var kv in _connectionId2FullScreenWindows.Where(x => x.Key == connectionId).ToArray())
                    {
                        var key = kv.Key;
                        var full = kv.Value;
#else
                    foreach (var (key, full) in _connectionId2FullScreenWindows.Where(x => x.Key == connectionId).ToArray())
                    {
#endif
                        if (full.Host == null || _connectionId2Hosts.ContainsKey(full.Host.ConnectionId) == false)
                        {
                            _connectionId2FullScreenWindows.TryRemove(key, out _);
                            _windowToBeDispose.Enqueue(full);
                            // defer ShowOrHide to after the lock
                            fullScreensToShowOrHide.Add(full);
                        }
                    }
                }

                // Mark Unhandled Protocol To Close
                foreach (var id2Host in _connectionId2Hosts.ToArray())
                {
                    var id = id2Host.Key;
                    bool unhandledFlag = true;
                    // if host in the tab
                    foreach (var kv in _token2TabWindows)
                    {
                        var tab = kv.Value;
                        var items = tab.GetViewModel().Items.ToList();
                        if (items.Any(x => x.Content.ConnectionId == id))
                        {
                            unhandledFlag = false;
                            break;
                        }
                    }

                    // if host in the full-screen
                    if (unhandledFlag && _connectionId2FullScreenWindows.ContainsKey(id))
                    {
                        unhandledFlag = false;
                    }

                    // host not in either tab or full-screen
                    if (unhandledFlag && _connectionId2Hosts.TryRemove(id, out var host))
                    {
                        SimpleLogHelper.Warning($@"MarkUnhandledProtocolToClose: marking to close: {host.GetType().Name}(id = {id}, hash = {host.GetHashCode()})");
                        host.OnClosed -= OnRequestCloseConnection;
                        host.OnFullScreen2Window -= this.MoveSessionToTabWindow;
                        _hostToBeDispose.Enqueue(host);
                        host.ProtocolServer.RunScriptAfterDisconnected();
                        PrintCacheCount();
                    }
                }
            }

            // perform UI operations outside the lock
            foreach (var (key, tab, connectionId) in tabsToRemoveItem)
            {
                if (tab.GetViewModel().TryRemoveItem(connectionId))
                {
                    var items = tab.GetViewModel().Items.ToList();
                    if (items.Count == 0)
                    {
                        // retire the window from the routing dict under a short re-lock
                        // (dictionary operations only, no UI dispatch inside)
                        lock (_dictLock)
                        {
                            if (_token2TabWindows.TryGetValue(key, out var t) && ReferenceEquals(t, tab))
                            {
                                // move tab from dict to queue
                                _token2TabWindows.TryRemove(key, out _);
                                _windowToBeDispose.Enqueue(tab);
                                tabsToHide.Add((key, tab));
                            }
                        }
                    }
                }
            }

            foreach (var full in fullScreensToShowOrHide)
            {
                // execute ShowOrHide in UI thread
                Execute.OnUIThreadSync(() => full.ShowOrHide(null));
            }

            foreach (var (key, tab) in tabsToHide)
            {
                // execute Hide in UI thread
                Execute.OnUIThreadSync(() => tab.Hide());
            }
        }

        #endregion

        #region Clean up CloseProtocol
        private void CloseMarkedProtocolHost()
        {
            while (_hostToBeDispose.TryDequeue(out var host))
            {
                PrintCacheCount();
                host.OnClosed -= OnRequestCloseConnection;
                host.OnFullScreen2Window -= this.MoveSessionToTabWindow;
                // Dispose
                try
                {
                    if (host is IDisposable d)
                    {
                        d.Dispose();
                    }
                    else
                    {
                        host.Close();
                    }
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error(e);
                }
            }
        }

        private void CloseEmptyWindows()
        {
            int closeCount = 0;
            foreach (var kv in _token2TabWindows.ToArray())
            {
                var key = kv.Key;
                var tab = kv.Value;
                var items = tab.GetViewModel().Items.ToList();
                items = items.Where(x => x != null).ToList();
                if (items.Count == 0 || items.All(x => _connectionId2Hosts.ContainsKey(x?.Content?.ConnectionId ?? "****") == false))
                {
                    SimpleLogHelper.Debug($@"CloseEmptyWindows: closing tab({tab.GetHashCode()})");
                    ++closeCount;
                    _token2TabWindows.TryRemove(key, out _);
                    _windowToBeDispose.Enqueue(tab);
                }
            }

            foreach (var kv in _connectionId2FullScreenWindows.ToArray())
            {
                var key = kv.Key;
                var full = kv.Value;
                if (full.Host == null || _connectionId2Hosts.ContainsKey(full.Host.ConnectionId) == false)
                {
                    SimpleLogHelper.Debug($@"CloseEmptyWindows: closing full(hash = {full.GetHashCode()})");
                    ++closeCount;
                    _connectionId2FullScreenWindows.TryRemove(key, out _);
                    _windowToBeDispose.Enqueue(full);
                }
            }

            PrintCacheCount();
            // 在正常的逻辑中，在关闭session时就应该把空窗体移除，不应该有空窗体的存在
            if (closeCount > 0)
                SimpleLogHelper.DebugWarning($@"CloseEmptyWindows: {closeCount} Empty Host closed");

            if (_windowToBeDispose.IsEmpty == false)
            {
                SimpleLogHelper.Debug($@"Closing: {_windowToBeDispose.Count} Empty Host.");
                Execute.OnUIThread(() =>
                {
                    while (_windowToBeDispose.TryDequeue(out var window))
                    {
                        window.Close();
                    }
                });
            }
        }

        public void CleanupProtocolsAndWindows()
        {
            lock (_dictLock)
            {
                this.CloseEmptyWindows();
            }
            this.CloseMarkedProtocolHost();
        }
        #endregion

        private void PrintCacheCount([CallerMemberName] string callMember = "")
        {
            SimpleLogHelper.Debug($@"{callMember}: Current: Host = {_connectionId2Hosts.Count}, Full = {_connectionId2FullScreenWindows.Count}, Tab = {_token2TabWindows.Count}, HostToBeDispose = {_hostToBeDispose.Count}, WindowToBeDispose = {_windowToBeDispose.Count}");
        }
    }
}