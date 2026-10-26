using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using _1RM.Model;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Service.DataSource;
using _1RM.Service.DataSource.Model;

namespace _1RM.View;

public partial class AccountTableWindow : Window
{
    private readonly GlobalData _data;
    private readonly DeviceAliasSettings _aliases;
    private readonly DataSourceService _sources;
    private readonly ObservableCollection<AccountTableRow> _rows = new();
    private readonly HashSet<AccountTableRow> _dirtyRows = new();
    private readonly AccountTableHistory _history = new();
    private AccountTableRow? _editingRow;
    private AccountTableRow.Snapshot? _editBefore;
    private bool _editChanged;
    private int _editVersion;
    private readonly bool _privacy;
    private bool _ready;
    private bool _saving;

    public AccountTableWindow() : this(IoC.Get<GlobalData>(), IoC.Get<DataSourceService>(), DeviceAliasSettings.Current) { }

    public AccountTableWindow(GlobalData data, DataSourceService sources, DeviceAliasSettings aliases)
    {
        _data = data;
        _aliases = aliases;
        _sources = sources;
        _privacy = aliases.PrivacyMode;
        InitializeComponent();
        _history.Changed += UpdateHistoryButtons;
        Width = Math.Min(1320, Math.Max(900, SystemParameters.WorkArea.Width - 40));
        Height = Math.Min(760, Math.Max(460, SystemParameters.WorkArea.Height - 40));
        SourcePicker.ItemsSource = sources.AllSources.Where(x => x.IsWritable).ToArray();
        SourcePicker.SelectedIndex = 0;
        foreach (var vm in data.VmItemList.Where(x => !x.Server.IsTmpSession()).OrderBy(x => x.DisplayName))
            AddRow(new AccountTableRow(vm.Server, aliases.GetAlias(DesktopPreviewStore.Key(vm.Server), vm.DisplayName), privacy: _privacy));
        AccountsGrid.ItemsSource = _rows;
        CollectionViewSource.GetDefaultView(_rows).Filter = Filter;
        AliasColumn.Visibility = _privacy ? Visibility.Collapsed : Visibility.Visible;
        NameColumn.IsReadOnly = _privacy;
        if (_privacy) HelpText.Text = "隐私模式：设备名已简化，备注隐藏；关闭隐私模式后可编辑设备名和备注。";
        _ready = true;
        UpdateStatus();
        UpdateHistoryButtons();
    }

    private void AddRow(AccountTableRow row)
    {
        InsertRow(_rows.Count, row);
    }
    private void InsertRow(int index, AccountTableRow row)
    {
        if (_rows.Contains(row)) return;
        _rows.Insert(Math.Min(index, _rows.Count), row);
        row.PropertyChanged += Row_Changed;
        if (row.IsDirty) _dirtyRows.Add(row);
    }
    private void RemoveRow(AccountTableRow row)
    {
        row.PropertyChanged -= Row_Changed;
        _rows.Remove(row);
        _dirtyRows.Remove(row);
    }
    private void Row_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AccountTableRow row)
        {
            if (row.IsDirty) _dirtyRows.Add(row); else _dirtyRows.Remove(row);
            if (ReferenceEquals(row, _editingRow)) _editChanged = true;
        }
        if (!_saving) UpdateStatus();
        UpdateHistoryButtons();
    }
    private void UpdateStatus(string? message = null)
    {
        if (StatusText == null) return;
        int dirty = _dirtyRows.Count;
        StatusText.Text = message ?? $"共 {_rows.Count} 台设备 · 待保存 {dirty} 项。Ctrl / Shift 可选择多行。密码默认隐藏。";
    }
    private bool Filter(object obj)
    {
        if (obj is not AccountTableRow row) return false;
        string query = SearchBox.Text.Trim();
        return query.Length == 0 || new[] { row.Name, row.Alias, row.Address, row.Port, row.UserName, row.Protocol, row.SourceName }
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready && !_saving && Commit()) CollectionViewSource.GetDefaultView(_rows).Refresh();
    }
    private bool Commit()
    {
        if (!AccountsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !AccountsGrid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        FinishCellEdit();
        return true;
    }
    private void UpdateHistoryButtons()
    {
        if (UndoButton == null || RedoButton == null) return;
        UndoButton.IsEnabled = !_saving && (_history.CanUndo || _editChanged);
        RedoButton.IsEnabled = !_saving && _history.CanRedo && !_editChanged;
    }
    private void ClearCellEdit()
    {
        _editingRow = null; _editBefore = null; _editChanged = false; _editVersion++;
        UpdateHistoryButtons();
    }
    private void FinishCellEdit()
    {
        if (_editingRow is not { } row || _editBefore is not { } before) return;
        var after = row.CaptureSnapshot();
        ClearCellEdit();
        if (!before.StateEquals(after))
            _history.Record(() => row.RestoreSnapshot(before), () => row.RestoreSnapshot(after));
    }
    private void RecordAddedRows(IEnumerable<AccountTableRow> rows)
    {
        var added = rows.Select(row => (Row: row, Index: _rows.IndexOf(row))).Where(x => x.Index >= 0).OrderBy(x => x.Index).ToArray();
        if (added.Length == 0) return;
        _history.Record(
            () => { foreach (var entry in added) RemoveRow(entry.Row); },
            () => { foreach (var entry in added) InsertRow(entry.Index, entry.Row); });
    }
    private void Undo_Click(object sender, RoutedEventArgs e) => ApplyHistory(false);
    private void Redo_Click(object sender, RoutedEventArgs e) => ApplyHistory(true);
    private void ApplyHistory(bool redo)
    {
        if (_saving || !Commit()) return;
        bool applied = redo ? _history.Redo() : _history.Undo();
        if (!applied) return;
        CollectionViewSource.GetDefaultView(_rows).Refresh();
        UpdateStatus((redo ? "已重做" : "已撤销") + $"。当前 {_rows.Count} 台设备，待保存 {_dirtyRows.Count} 项。");
        UpdateHistoryButtons();
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_saving || SearchBox.IsKeyboardFocusWithin || SourcePicker.IsKeyboardFocusWithin) return;
        if ((Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.Z || e.Key == Key.Y)) ||
            (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z))
        {
            e.Handled = true;
            if (!e.IsRepeat) ApplyHistory(e.Key == Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        }
    }
    private DataSourceBase? NewSource()
    {
        if (SourcePicker.SelectedItem is DataSourceBase { IsWritable: true } source) return source;
        UpdateStatus("没有可写的数据源，请先检查数据源设置。");
        return null;
    }
    private void SelectRow(AccountTableRow row)
    {
        SearchBox.Clear();
        AccountsGrid.SelectedItem = row;
        AccountsGrid.ScrollIntoView(row);
        AccountsGrid.Focus();
        UpdateStatus();
    }
    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || !Commit() || NewSource() is not { } source) return;
        string name = "新设备";
        for (int n = 2; _rows.Any(x => x.Draft.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)); n++) name = "新设备 " + n;
        var row = new AccountTableRow(new RDP { DisplayName = name, Port = "3389", DataSource = source }, "", true, _privacy);
        AddRow(row); RecordAddedRows(new[] { row }); SelectRow(row);
    }
    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || !Commit() || NewSource() is not { } source) return;
        var selected = AccountsGrid.SelectedItems.OfType<AccountTableRow>().ToArray();
        if (selected.Length == 0) { UpdateStatus("请先选择要复制的账号行。"); return; }
        AccountTableRow? last = null;
        var added = new List<AccountTableRow>();
        foreach (var row in selected)
        {
            last = row.Duplicate(_rows.Select(x => x.Draft.DisplayName), source);
            AddRow(last);
            added.Add(last);
        }
        RecordAddedRows(added);
        if (last != null) SelectRow(last);
    }
    private void RemoveDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || !Commit()) return;
        var drafts = AccountsGrid.SelectedItems.OfType<AccountTableRow>().Where(x => x.IsNew).ToArray();
        var removed = drafts.Select(row => (Row: row, Index: _rows.IndexOf(row))).OrderBy(x => x.Index).ToArray();
        foreach (var row in drafts) RemoveRow(row);
        if (removed.Length > 0)
            _history.Record(
                () => { foreach (var entry in removed) InsertRow(entry.Index, entry.Row); },
                () => { foreach (var entry in removed) RemoveRow(entry.Row); });
        UpdateStatus(drafts.Length == 0 ? "只可移除尚未保存的新行；已有设备不会被删除。" : $"已移除 {drafts.Length} 行草稿。");
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || !Commit()) return;
        var rows = CollectionViewSource.GetDefaultView(_rows).Cast<AccountTableRow>().ToArray();
        if (rows.Length == 0) { UpdateStatus("当前筛选结果没有账号可导出。"); return; }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出账号表", Filter = "Excel 工作簿 (*.xlsx)|*.xlsx", AddExtension = true,
            DefaultExt = ".xlsx", FileName = "1Remote-账号表-" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            AccountTableExcel.Export(dialog.FileName, rows, ExportPasswords.IsChecked == true, _privacy);
            UpdateStatus($"已导出 {rows.Length} 行" + (ExportPasswords.IsChecked == true ? "（包含密码）。" : "（不含密码）。"));
        }
        catch (Exception) { UpdateStatus("导出失败，请检查目标文件是否被占用及目录是否可写。"); }
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || !Commit() || NewSource() is not { } defaultSource) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "导入账号表", Filter = "Excel 工作簿 (*.xlsx)|*.xlsx", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        var added = new List<AccountTableRow>();
        try
        {
            var result = AccountTableExcel.Read(dialog.FileName);
            var messages = new List<string>(result.Errors);
            int imported = 0;
            AccountTableRow? last = null;
            foreach (var record in result.Accounts)
            {
                ProtocolBase? server = record.Protocol.ToUpperInvariant() switch
                {
                    "RDP" => new RDP(), "SSH" => new SSH(), "VNC" => new VNC(),
                    "SFTP" => new SFTP(), "FTP" => new FTP(), "TELNET" => new Telnet(), _ => null
                };
                if (server is not ProtocolBaseWithAddressPort network)
                { messages.Add($"第 {record.RowNumber} 行：此协议无法通过简化账号表导入，请使用原生 JSON。"); continue; }
                var matchingSource = _sources.AllSources.FirstOrDefault(x => x.DataSourceName.Equals(record.SourceName, StringComparison.OrdinalIgnoreCase));
                var source = matchingSource ?? defaultSource;
                if (matchingSource == null && !string.IsNullOrWhiteSpace(record.SourceName))
                    messages.Add($"第 {record.RowNumber} 行：原数据源不可用，已暂存到当前选择的数据源。");
                if (!source.IsWritable) { messages.Add($"第 {record.RowNumber} 行：目标数据源不可写。"); continue; }
                server.DataSource = source;
                network.Address = record.Address; network.Port = record.Port;
                string name = record.Name, stem = name + " - 导入";
                for (int n = 1; _rows.Any(x => x.Draft.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)); n++) name = stem + (n == 1 ? "" : " " + n);
                server.DisplayName = name;
                if (server is ProtocolBaseWithAddressPortUserPwd credential)
                {
                    credential.UserName = record.UserName;
                    credential.Password = record.Password ?? "";
                    credential.AskPasswordWhenConnect = record.Password == null;
                }
                if (server is RDP rdp) rdp.Domain = record.Domain;
                var row = new AccountTableRow(server, record.Alias, true, _privacy);
                if (row.Validate() is { } error) { messages.Add($"第 {record.RowNumber} 行：{error}。"); continue; }
                AddRow(row); added.Add(row); last = row; imported++;
            }
            if (last != null) SelectRow(last);
            UpdateStatus($"已导入 {imported} 行草稿，点击“保存修改”后才会新增设备。" + (messages.Count > 0 ? $" 有 {messages.Count} 条导入提示。" : ""));
            if (messages.Count > 0)
                MessageBox.Show(this, string.Join("\n", messages.Take(25)) + (messages.Count > 25 ? $"\n另有 {messages.Count - 25} 条。" : ""), "账号表导入提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception) { UpdateStatus("导入未完成，请检查 .xlsx 文件和列标题。已经读取的行仍是未保存的草稿。"); }
        finally { RecordAddedRows(added); }
    }
    private void Grid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not AccountTableRow row) { e.Cancel = true; return; }
        bool allowed = e.Column.SortMemberPath switch
        {
            "Name" => row.CanEditName,
            "Alias" => row.CanEditAlias,
            "Address" => row.CanEditAddress,
            "Port" => row.CanEditPort,
            "UserName" => row.CanEditUserName,
            "Password" => row.CanEditPassword,
            "Domain" => row.CanEditDomain,
            _ => false
        };
        e.Cancel = _saving || !allowed;
        if (!allowed) UpdateStatus("此字段不可在账号表修改。凭据库、密钥和高级参数请使用设备的连接设置。");
        if (!e.Cancel)
        {
            FinishCellEdit();
            _editingRow = row; _editBefore = row.CaptureSnapshot(); _editChanged = false; _editVersion++;
            UpdateHistoryButtons();
        }
    }
    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_editingRow is not { } row || _editBefore is not { } before || !ReferenceEquals(e.Row.Item, row)) return;
        if (e.EditAction == DataGridEditAction.Cancel)
        {
            ClearCellEdit();
            row.RestoreSnapshot(before);
            return;
        }
        // Flush the TextBox binding before recording; PasswordBox already writes through its guarded event.
        if (e.EditingElement is TextBox textBox) textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        int version = _editVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            if (version == _editVersion) FinishCellEdit();
        }));
    }
    private void Password_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && box.DataContext is AccountTableRow row)
        { box.Tag = true; box.Password = row.Password; box.Tag = null; box.Focus(); }
    }
    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { Tag: null, DataContext: AccountTableRow row } box && row.CanEditPassword && ReferenceEquals(_editingRow, row))
            row.Password = box.Password;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || !Commit()) return;
        var dirty = _rows.Where(x => x.IsDirty).ToArray();
        if (dirty.Length == 0) { UpdateStatus("没有需要保存的修改。"); return; }
        // Validate every draft before writing the first one.
        foreach (var row in dirty)
        {
            string? error = !row.IsWritable ? "数据源不可写" : row.Validate();
            if (error == null && !row.IsNew && row.HasProtocolChanges)
            {
                var current = _data.GetItemById(row.SourceName, row.Draft.Id)?.Server;
                if (current == null) error = "原设备已被删除，请重新打开账号表";
                else if (!row.MatchesOriginal(current)) error = "设备已在别处修改，请重新打开账号表";
            }
            if (error != null)
            {
                row.SetError(error); AccountsGrid.SelectedItem = row; AccountsGrid.ScrollIntoView(row);
                UpdateStatus($"{row.Name}：{error}。尚未保存任何修改。"); return;
            }
        }
        _saving = true;
        UpdateHistoryButtons();
        AccountsGrid.IsEnabled = Toolbar.IsEnabled = SearchBox.IsEnabled = SaveButton.IsEnabled = CloseButton.IsEnabled = false;
        int completed = 0;
        try
        {
            foreach (var row in dirty)
            {
                UpdateStatus($"正在保存 {completed + 1} / {dirty.Length}…");
                try
                {
                    if (row.HasProtocolChanges)
                    {
                        var save = row.CreateSaveCopy();
                        bool insert = row.IsNew;
                        var result = await Task.Run(() => insert ? _data.AddServer(save, save.DataSource!) : _data.UpdateServer(save));
                        if (!result.IsSuccess) throw new InvalidOperationException("连接保存失败，请检查数据源连接和写入权限");
                        _history.Clear();
                        row.AcceptServerSaved(save);
                    }
                    if (row.HasAliasChanges)
                    {
                        _aliases.SetConnectionAlias(DesktopPreviewStore.Key(row.Draft), row.AliasToSave);
                        _history.Clear();
                        row.AcceptAliasSaved();
                    }
                    completed++;
                }
                catch (Exception)
                {
                    row.SetError("保存失败");
                    AccountsGrid.SelectedItem = row; AccountsGrid.ScrollIntoView(row);
                    UpdateStatus($"已保存 {completed} 项；“{row.Name}”保存失败，其余修改仍保留。请检查数据源和本地目录的写入权限后重试。");
                    return;
                }
            }
            UpdateStatus($"已保存 {completed} 项，设备卡片已同步。关闭账号表后可单击卡片连接。");
        }
        finally
        {
            _saving = false;
            AccountsGrid.IsEnabled = Toolbar.IsEnabled = SearchBox.IsEnabled = SaveButton.IsEnabled = CloseButton.IsEnabled = true;
            UpdateHistoryButtons();
        }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_saving) { e.Cancel = true; return; }
        if (!_ready) return;
        Commit();
        if (_rows.Any(x => x.IsDirty) && MessageBox.Show(this, "还有未保存的修改，是否放弃并关闭账号表？", "账号表", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            e.Cancel = true;
    }
}
