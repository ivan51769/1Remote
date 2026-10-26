using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Service.DataSource.Model;

namespace _1RM.View;

/// <summary>A detached editing copy. No property change mutates a live connection.</summary>
public sealed class AccountTableRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public ProtocolBase Draft { get; private set; }
    private string _savedJson;
    private string _alias;
    private string _savedAlias;
    private bool _aliasNeedsSave;
    private bool _privacy;
    private string _error = "";
    public bool IsNew { get; private set; }
    public bool HasProtocolChanges => IsNew || Draft.ToJsonString() != _savedJson;
    public bool HasAliasChanges => _aliasNeedsSave || _alias != _savedAlias;
    public bool IsDirty => HasProtocolChanges || HasAliasChanges;
    public bool IsWritable => Draft.DataSource?.IsWritable == true;
    public string SourceName => Draft.DataSource?.DataSourceName ?? "未选择数据源";
    public string Protocol => Draft.ProtocolDisplayName;
    public bool CanEditName => IsWritable && !_privacy;
    public bool CanEditAlias => IsWritable && !_privacy;
    public bool CanEditAddress => IsWritable && Draft is ProtocolBaseWithAddressPort p && p.ShowAddressInput();
    public bool CanEditPort => IsWritable && Draft is ProtocolBaseWithAddressPort p && p.ShowPortInput();
    public bool CanEditUserName => IsWritable && Draft is ProtocolBaseWithAddressPortUserPwd p && p.ShowUserNameInput() && string.IsNullOrEmpty(p.InheritedCredentialName);
    public bool CanEditPassword => IsWritable && Draft is ProtocolBaseWithAddressPortUserPwd p && p.ShowPasswordInput() && string.IsNullOrEmpty(p.InheritedCredentialName) && p.UsePrivateKeyForConnect != true;
    public bool CanEditDomain => IsWritable && Draft is RDP;
    public string State => !string.IsNullOrEmpty(_error) ? _error : !IsWritable ? "只读" : IsNew ? "新增" : IsDirty ? "已修改" : "已保存";

    public AccountTableRow(ProtocolBase original, string alias, bool isNew = false, bool privacy = false)
    {
        Draft = CopyServer(original);
        if (isNew) Draft.Id = "";
        IsNew = isNew;
        _privacy = privacy;
        _savedJson = Draft.ToJsonString();
        _alias = _savedAlias = alias;
        _aliasNeedsSave = isNew;
    }

    public static ProtocolBase CopyServer(ProtocolBase original)
    {
        var copy = original.CreateFromJsonString(original.ToJsonString())
            ?? throw new InvalidOperationException("无法复制此连接类型。");
        copy.Id = original.Id;
        copy.DataSource = original.DataSource;
        copy.DecryptToConnectLevel();
        return copy;
    }

    public string Name
    {
        get => _privacy ? DeviceAliasSettings.PrivateDeviceName(Draft.DisplayName) : Draft.DisplayName;
        set { if (!_privacy && Draft.DisplayName != value) { Draft.DisplayName = value; _aliasNeedsSave = true; Changed(); } }
    }
    public string Alias
    {
        get => _privacy ? "" : _alias;
        set { if (!_privacy && _alias != value) { _alias = value ?? ""; Changed(); } }
    }
    public string Address
    {
        get => (Draft as ProtocolBaseWithAddressPort)?.Address ?? "";
        set { if (Draft is ProtocolBaseWithAddressPort p && p.Address != value) { var name = Draft.DisplayName; p.Address = value; Draft.DisplayName = name; Changed(); } }
    }
    public string Port
    {
        get => (Draft as ProtocolBaseWithAddressPort)?.Port ?? "";
        set { if (Draft is ProtocolBaseWithAddressPort p && p.Port != value) { p.Port = value; Changed(); } }
    }
    public string UserName
    {
        get => Draft is ProtocolBaseWithAddressPortUserPwd p ? (string.IsNullOrEmpty(p.InheritedCredentialName) ? p.UserName : "凭据库：" + p.InheritedCredentialName) : "";
        set { if (Draft is ProtocolBaseWithAddressPortUserPwd p && string.IsNullOrEmpty(p.InheritedCredentialName) && p.UserName != value) { p.UserName = value; Changed(); } }
    }
    // Never bind this property to a display/clipboard cell; only PasswordBox accesses it.
    public string Password
    {
        get => (Draft as ProtocolBaseWithAddressPortUserPwd)?.Password ?? "";
        set
        {
            if (Draft is ProtocolBaseWithAddressPortUserPwd p && string.IsNullOrEmpty(p.InheritedCredentialName) && p.UsePrivateKeyForConnect != true && p.Password != value)
            { p.Password = value; if (!string.IsNullOrEmpty(value)) p.AskPasswordWhenConnect = false; Changed(); }
        }
    }
    public string PasswordSummary => Draft is not ProtocolBaseWithAddressPortUserPwd p ? "—" :
        !string.IsNullOrEmpty(p.InheritedCredentialName) ? "凭据库" : p.UsePrivateKeyForConnect == true ? "密钥" : string.IsNullOrEmpty(p.Password) ? "空" : "••••••••";
    public string Domain
    {
        get => (Draft as RDP)?.Domain ?? "";
        set { if (Draft is RDP p && p.Domain != value) { p.Domain = value; Changed(); } }
    }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Draft.DisplayName)) return "设备名不能为空";
        if (_alias.Trim().Length > 64) return "备注最多 64 个字符";
        if (Draft is ProtocolBaseWithAddressPort p)
        {
            if (p.ShowAddressInput() && string.IsNullOrWhiteSpace(p.Address)) return "地址不能为空";
            if (p.ShowPortInput() && (!int.TryParse(p.Port, out var port) || port < 1 || port > 65535)) return "端口应为 1–65535";
        }
        return null;
    }
    public bool MatchesOriginal(ProtocolBase current) => CopyServer(current).ToJsonString() == _savedJson;
    public string AliasToSave => _alias.Trim();
    public ProtocolBase CreateSaveCopy() => CopyServer(Draft);
    public void AcceptServerSaved(ProtocolBase saved)
    {
        // Retain the assigned id immediately; an alias-write failure must never reinsert this row.
        Draft.Id = saved.Id;
        IsNew = false;
        _savedJson = Draft.ToJsonString();
        Changed();
    }
    public void AcceptAliasSaved() { _alias = _savedAlias = _alias.Trim(); _aliasNeedsSave = false; Changed(); }
    public void SetError(string message) { _error = message; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State))); }
    private void Changed() { _error = ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }

    public AccountTableRow Duplicate(IEnumerable<string> existingNames, DataSourceBase source)
    {
        var names = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        string stem = Draft.DisplayName + " - 副本", name = stem;
        for (int n = 2; names.Contains(name); n++) name = stem + " " + n;
        var copy = CopyServer(Draft);
        copy.Id = ""; copy.DisplayName = name; copy.DataSource = source;
        return new AccountTableRow(copy, _alias, true, _privacy);
    }

    /// <summary>One row's detached edit state; retained only in this window's bounded history.</summary>
    public sealed class Snapshot
    {
        internal readonly ProtocolBase Draft;
        internal readonly string DraftJson;
        internal readonly string SavedJson;
        internal readonly string Alias;
        internal readonly string SavedAlias;
        internal readonly bool AliasNeedsSave;
        internal readonly bool IsNew;
        internal readonly bool Privacy;
        internal readonly string Error;
        internal Snapshot(AccountTableRow row)
        {
            Draft = CopyServer(row.Draft);
            DraftJson = Draft.ToJsonString();
            SavedJson = row._savedJson;
            Alias = row._alias; SavedAlias = row._savedAlias;
            AliasNeedsSave = row._aliasNeedsSave; IsNew = row.IsNew;
            Privacy = row._privacy; Error = row._error;
        }
        public bool StateEquals(Snapshot other) => other != null && DraftJson == other.DraftJson &&
            Draft.Id == other.Draft.Id && ReferenceEquals(Draft.DataSource, other.Draft.DataSource) &&
            SavedJson == other.SavedJson && Alias == other.Alias && SavedAlias == other.SavedAlias &&
            AliasNeedsSave == other.AliasNeedsSave && IsNew == other.IsNew && Privacy == other.Privacy && Error == other.Error;
    }
    public Snapshot CaptureSnapshot() => new(this);
    public void RestoreSnapshot(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Draft = CopyServer(snapshot.Draft);
        _savedJson = snapshot.SavedJson;
        _alias = snapshot.Alias; _savedAlias = snapshot.SavedAlias;
        _aliasNeedsSave = snapshot.AliasNeedsSave; IsNew = snapshot.IsNew;
        _privacy = snapshot.Privacy; _error = snapshot.Error;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
