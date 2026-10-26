using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace _1RM.Service;

public sealed class DeviceAliasSettings
{
    public sealed class Data
    {
        public bool PrivacyMode { get; set; }
        public bool EnhancedMode { get; set; }
        public Dictionary<string, string> DeviceNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Connections { get; set; } = new(StringComparer.Ordinal);
    }

    private readonly string _path;
    private readonly object _sync = new();
    private Data _data;
    private static readonly Lazy<DeviceAliasSettings> Shared = new(() => new DeviceAliasSettings(DefaultPath));
    public static DeviceAliasSettings Current => Shared.Value;
    public event Action? Changed;
    public static string DefaultPath
    {
        get
        {
            using var sha = SHA256.Create();
            var install = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\', '/').ToUpperInvariant();
            var key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(install))).Replace("-", "");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1Remote", "DeviceAliases", key + ".json");
        }
    }

    public DeviceAliasSettings(string path)
    {
        _path = path;
        _data = File.Exists(path) ? JsonConvert.DeserializeObject<Data>(File.ReadAllText(path)) ?? new Data() : new Data();
        _data.DeviceNames = new Dictionary<string, string>(_data.DeviceNames ?? new(), StringComparer.OrdinalIgnoreCase);
        _data.Connections ??= new Dictionary<string, string>();
    }

    public bool PrivacyMode
    {
        get { lock (_sync) return _data.PrivacyMode; }
        set
        {
            lock (_sync)
            {
                if (_data.PrivacyMode == value) return;
                var next = Copy(); next.PrivacyMode = value; Save(next);
            }
            Changed?.Invoke();
        }
    }
    private string? MatchDevice(string name) => _data.DeviceNames.Keys.OrderByDescending(x => x.Length)
        .FirstOrDefault(key => key.Length > 0 && Regex.IsMatch(name ?? "", @"(?<![\p{L}\p{N}])" + Regex.Escape(key) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase));
    private string ReadAlias(string key, string name) => _data.Connections.TryGetValue(key, out var alias)
        ? alias : MatchDevice(name) is { } device ? _data.DeviceNames[device] : "";
    public string GetAlias(string key, string deviceName) { lock (_sync) return ReadAlias(key, deviceName); }
    public string GetVisibleAlias(string key, string deviceName) { lock (_sync) return _data.PrivacyMode ? "" : ReadAlias(key, deviceName); }
    public bool EnhancedMode
    {
        get { lock (_sync) return _data.EnhancedMode; }
        set
        {
            lock (_sync)
            {
                if (_data.EnhancedMode == value) return;
                var next = Copy(); next.EnhancedMode = value; Save(next);
            }
            Changed?.Invoke();
        }
    }
    public string GetVisibleDeviceName(string name) => PrivacyMode ? PrivateDeviceName(name) : name;
    public static string PrivateDeviceName(string name)
    {
        var match = Regex.Match(name ?? "", @"(?<![a-z0-9])(?:gq)?amz\d+(?![a-z0-9])|(?<![a-z0-9])admin(?![a-z0-9])", RegexOptions.IgnoreCase);
        var code = match.Success ? Regex.Replace(match.Value.ToLowerInvariant(), "^gq", "")
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name ?? "")))[..6];
        var copySuffix = Regex.Match(name ?? "", @"\s-\s(副本|导入)(?:\s(\d+))?$");
        return "Server-" + code + ((name ?? "").Contains("外网") ? "-外网" : "") +
            (copySuffix.Success ? "-" + copySuffix.Groups[1].Value + copySuffix.Groups[2].Value : "");
    }
    public void SetConnectionAlias(string key, string alias)
    {
        alias = (alias ?? "").Trim();
        if (alias.Length > 64) throw new ArgumentException("备注名最多 64 个字符。");
        lock (_sync) { var next = Copy(); next.Connections[key] = alias; Save(next); }
        Changed?.Invoke();
    }
    public void SetAlias(string key, string deviceName, string alias)
    {
        alias = (alias ?? "").Trim();
        if (alias.Length > 64) throw new ArgumentException("备注名最多 64 个字符。");
        lock (_sync)
        {
            var next = Copy();
            if (MatchDevice(deviceName) is { } device) next.DeviceNames[device] = alias;
            else next.Connections[key] = alias;
            Save(next);
        }
        Changed?.Invoke();
    }
    private Data Copy() => new()
    {
        PrivacyMode = _data.PrivacyMode,
        EnhancedMode = _data.EnhancedMode,
        DeviceNames = new Dictionary<string, string>(_data.DeviceNames, StringComparer.OrdinalIgnoreCase),
        Connections = new Dictionary<string, string>(_data.Connections)
    };
    private void Save(Data next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        string temp = _path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonConvert.SerializeObject(next, Formatting.Indented), new UTF8Encoding(false));
            File.Move(temp, _path, true);
            _data = next;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
