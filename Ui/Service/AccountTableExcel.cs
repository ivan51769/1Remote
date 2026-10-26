using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using _1RM.Model.Protocol.Base;
using _1RM.View;

namespace _1RM.Service;

public sealed record AccountExcelRecord(int RowNumber, string Name, string Alias, string Protocol,
    string Address, string Port, string UserName, string? Password, string Domain, string SourceName);

public sealed record AccountExcelImportResult(IReadOnlyList<AccountExcelRecord> Accounts, IReadOnlyList<string> Errors);

/// <summary>Small, bounded Open XML workbook exchange. Reading never changes a connection or database.</summary>
public static class AccountTableExcel
{
    private const int MaxRows = 10000;
    private const int MaxCellLength = 32767;
    private const long MaxFileBytes = 32 * 1024 * 1024;
    private const long MaxPartBytes = 16 * 1024 * 1024;
    private const long MaxTotalBytes = 64 * 1024 * 1024;
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly string[] Headers = { "设备名", "备注名", "协议", "地址", "端口", "用户名", "密码", "域", "数据源", "密码状态", "认证方式" };
    private static readonly Dictionary<string, string> ProtocolPorts = new(StringComparer.OrdinalIgnoreCase)
    { ["RDP"] = "3389", ["SSH"] = "22", ["VNC"] = "5900", ["SFTP"] = "22", ["FTP"] = "21", ["Telnet"] = "23" };
    private static string HeaderName(string value) => value.Trim().Replace(" ", "").ToLowerInvariant() switch
    {
        "设备名" or "设备名称" or "名称" or "name" or "devicename" or "device_name" => "设备名",
        "备注名" or "备注" or "alias" => "备注名",
        "协议" or "protocol" => "协议",
        "地址" or "ip" or "ip地址" or "ip/地址" or "address" or "host" or "hostname" => "地址",
        "端口" or "port" => "端口",
        "用户名" or "账号" or "username" or "user" or "user_name" => "用户名",
        "密码" or "password" => "密码",
        "域" or "domain" => "域",
        "数据源" or "datasource" or "source" => "数据源",
        "密码状态" or "passwordstate" => "密码状态",
        "认证方式" or "authentication" => "认证方式",
        _ => value.Trim()
    };

    public static void Export(string path, IEnumerable<AccountTableRow> rows, bool includePasswords = false, bool privacyMode = false)
    {
        var values = new List<string[]> { Headers };
        foreach (var row in rows)
        {
            if (values.Count > MaxRows) throw new InvalidOperationException("一次最多导出 10000 行账号。");
            var credential = row.Draft as ProtocolBaseWithAddressPortUserPwd;
            string authentication = credential == null ? "无" : !string.IsNullOrEmpty(credential.InheritedCredentialName)
                ? "凭据库" : credential.UsePrivateKeyForConnect == true ? "密钥" : "密码";
            bool exportPassword = includePasswords && authentication == "密码";
            values.Add(new[]
            {
                privacyMode ? DeviceAliasSettings.PrivateDeviceName(row.Draft.DisplayName) : row.Name,
                privacyMode ? "" : row.Alias, row.Draft.Protocol, row.Address, row.Port,
                credential?.UserName ?? "", exportPassword ? row.Password : "", row.Domain, row.SourceName,
                exportPassword ? "已包含" : "未导出", authentication
            });
        }
        var fullPath = Path.GetFullPath(path);
        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
                WritePart(zip, "[Content_Types].xml", new XElement(ct + "Types",
                    new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(ct + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    Enumerable.Range(1, 2).Select(i => new XElement(ct + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
                WritePart(zip, "_rels/.rels", new XElement(P + "Relationships", Relationship("rId1", "officeDocument", "xl/workbook.xml")));
                WritePart(zip, "xl/workbook.xml", new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R),
                    new XElement(S + "sheets", new XElement(S + "sheet", new XAttribute("name", "账号信息"), new XAttribute("sheetId", "1"), new XAttribute(R + "id", "rId1")),
                        new XElement(S + "sheet", new XAttribute("name", "说明"), new XAttribute("sheetId", "2"), new XAttribute(R + "id", "rId2")))));
                WritePart(zip, "xl/_rels/workbook.xml.rels", new XElement(P + "Relationships",
                    Relationship("rId1", "worksheet", "worksheets/sheet1.xml"), Relationship("rId2", "worksheet", "worksheets/sheet2.xml")));
                WritePart(zip, "xl/worksheets/sheet1.xml", Worksheet(values, true));
                WritePart(zip, "xl/worksheets/sheet2.xml", Worksheet(new[]
                {
                    new[] { "1Remote 账号表交换说明" },
                    new[] { includePasswords ? "本次已选择包含密码：密码单元格以明文保存，请妥善保管此文件。" : "本次未导出密码；导入后请按需填写密码。未导出、遮罩不会作为真实密码写入。" },
                    new[] { privacyMode ? "隐私模式已开启：设备名已简化，备注名留空。" : "设备名与备注名按当前账号表导出。" },
                    new[] { "导入仅生成新账号草稿，不覆盖已有设备。检查后点击保存修改，主页才会新增设备卡片。" },
                    new[] { "支持 RDP、SSH、VNC、SFTP、FTP、Telnet 的基础账号字段。凭据库、密钥及高级连接配置请使用连接设置或原有 JSON 导入导出。" },
                    new[] { "设备名、地址、用户名、密码请在 Excel 中设置为文本，避免 Excel 改写前导零。公式单元格不支持导入。" },
                    new[] { "普通工作簿需包含设备名、地址表头。协议缺省为 RDP，端口缺省采用该协议默认端口。密码列缺省表示未提供密码。" }
                }, false));
            }
            File.Move(temp, fullPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static XElement Relationship(string id, string type, string target) => new(P + "Relationship",
        new XAttribute("Id", id), new XAttribute("Type", R.NamespaceName + "/" + type), new XAttribute("Target", target));

    private static XElement Worksheet(IEnumerable<string[]> records, bool accounts)
    {
        var rows = records.ToArray();
        var root = new XElement(S + "worksheet");
        if (accounts) root.Add(new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", "0"),
            new XElement(S + "pane", new XAttribute("ySplit", "1"), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))));
        root.Add(new XElement(S + "cols", new XElement(S + "col", new XAttribute("min", "1"), new XAttribute("max", accounts ? Headers.Length : 1), new XAttribute("width", accounts ? 21 : 110), new XAttribute("customWidth", "1"))));
        root.Add(new XElement(S + "sheetData", rows.Select((row, i) => new XElement(S + "row", new XAttribute("r", i + 1), row.Select((value, column) =>
        {
            if ((value ?? "").Length > MaxCellLength) throw new InvalidOperationException("单元格内容超过 Excel 的 32767 字符限制。");
            return new XElement(S + "c", new XAttribute("r", ColumnName(column) + (i + 1)), new XAttribute("t", "inlineStr"),
                new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? "")));
        })))));
        if (accounts) root.Add(new XElement(S + "autoFilter", new XAttribute("ref", $"A1:{ColumnName(Headers.Length - 1)}{rows.Length}")));
        return root;
    }
    private static void WritePart(ZipArchive zip, string path, XElement root)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).Save(writer);
    }
    private static string ColumnName(int column)
    {
        string name = "";
        for (int n = column + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    public static AccountExcelImportResult Read(string path)
    {
        var accounts = new List<AccountExcelRecord>();
        var errors = new List<string>();
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaxFileBytes) throw new InvalidDataException("Excel 文件不能超过 32 MB。");
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            if (zip.Entries.Count > 128 || zip.Entries.Sum(entry => entry.Length) > MaxTotalBytes || zip.Entries.Any(entry => entry.Length > MaxPartBytes))
                throw new InvalidDataException("Excel 文件过大或结构过于复杂，请只保留账号表后重试。");
            if (zip.Entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                throw new InvalidDataException("Excel 文件包含重复的内部路径。");
            var workbook = ReadPart(zip, "xl/workbook.xml");
            var relationships = ReadPart(zip, "xl/_rels/workbook.xml.rels");
            var sheets = workbook.Descendants().Where(e => e.Name.LocalName == "sheet").ToArray();
            var sheet = sheets.FirstOrDefault(e => (string?)e.Attribute("name") == "账号信息")
                ?? sheets.FirstOrDefault(e => (string?)e.Attribute("state") != "hidden" && (string?)e.Attribute("state") != "veryHidden")
                ?? throw new InvalidDataException("Excel 文件没有可读取的工作表。");
            string? relationshipId = sheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
            var relationship = relationships.Descendants().FirstOrDefault(e => e.Name.LocalName == "Relationship" && (string?)e.Attribute("Id") == relationshipId)
                ?? throw new InvalidDataException("工作表关系不完整。");
            if (!((string?)relationship.Attribute("Type") ?? "").EndsWith("/worksheet", StringComparison.Ordinal)) throw new InvalidDataException("首个工作表不是普通账号表。");
            var worksheet = ReadPart(zip, ResolvePart(relationship));
            var sharedRelationship = relationships.Descendants().FirstOrDefault(e => ((string?)e.Attribute("Type") ?? "").EndsWith("/sharedStrings", StringComparison.Ordinal));
            string sharedPath = sharedRelationship == null ? "xl/sharedStrings.xml" : ResolvePart(sharedRelationship);
            var shared = zip.GetEntry(sharedPath) == null ? Array.Empty<string>() : ReadPart(zip, sharedPath).Descendants()
                .Where(e => e.Name.LocalName == "si").Select(TextValue).ToArray();
            if (shared.Length > 200000) throw new InvalidDataException("共享文本数量超过限制。");
            ParseRows(worksheet, shared, accounts, errors);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or XmlException or ArgumentException or FormatException or OverflowException)
        {
            // An invalid package is never partially imported. Do not echo XML contents or account values.
            accounts.Clear(); errors.Clear();
            errors.Add(ex is InvalidDataException ? ex.Message : "无法读取 Excel 文件，请确认它是未加密的 .xlsx 文件且未被独占使用。");
        }
        return new AccountExcelImportResult(accounts, errors);
    }
    private static string ResolvePart(XElement relationship)
    {
        if (string.Equals((string?)relationship.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("不支持外部工作表链接。");
        string target = Uri.UnescapeDataString((string?)relationship.Attribute("Target") ?? "").Replace('\\', '/');
        if (target.Length == 0 || target.Contains(':') || target.Contains('?') || target.Contains('#')) throw new InvalidDataException("工作表路径无效。");
        var parts = new List<string>();
        foreach (var segment in (target.StartsWith('/') ? target[1..] : "xl/" + target).Split('/'))
        {
            if (segment == "." || segment.Length == 0) continue;
            if (segment == "..")
            {
                if (parts.Count == 0) throw new InvalidDataException("工作表路径超出文件范围。");
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(segment);
        }
        return string.Join("/", parts);
    }
    private static XDocument ReadPart(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Excel 文件缺少必要的内部工作表。");
        if (entry.Length > MaxPartBytes) throw new InvalidDataException("工作表内容超过大小限制。");
        using var input = entry.Open();
        using var bounded = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (bounded.Length + count > MaxPartBytes) throw new InvalidDataException("工作表解压内容超过大小限制。");
            bounded.Write(buffer, 0, count);
        }
        bounded.Position = 0;
        using var reader = XmlReader.Create(bounded, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxPartBytes, IgnoreComments = true });
        return XDocument.Load(reader);
    }
    private static string TextValue(XElement container)
    {
        var text = string.Concat(container.Descendants().Where(e => e.Name.LocalName == "t" && !e.Ancestors().Any(a => a.Name.LocalName == "rPh")).Select(e => e.Value));
        if (text.Length > MaxCellLength) throw new InvalidDataException("单元格内容超过 Excel 长度限制。");
        return text;
    }
    private static string CellValue(XElement cell, string[] shared)
    {
        var value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
        string type = (string?)cell.Attribute("t") ?? "";
        if (type == "inlineStr") return TextValue(cell);
        if (type == "s")
        {
            if (!int.TryParse(value, out int index) || index < 0 || index >= shared.Length) throw new InvalidDataException("共享文本索引无效。");
            return shared[index];
        }
        if (value.Length > MaxCellLength) throw new InvalidDataException("单元格内容超过 Excel 长度限制。");
        return value;
    }
    private static int ColumnIndex(string? reference, int fallback)
    {
        if (string.IsNullOrEmpty(reference)) return fallback;
        int index = 0, letters = 0;
        foreach (char ch in reference.ToUpperInvariant())
        {
            if (ch < 'A' || ch > 'Z') break;
            if (++letters > 3) throw new InvalidDataException("单元格地址无效。");
            index = checked(index * 26 + ch - 'A' + 1);
        }
        if (index == 0 || index > 16384) throw new InvalidDataException("单元格地址无效。");
        return index - 1;
    }
    private static void ParseRows(XDocument worksheet, string[] shared, List<AccountExcelRecord> accounts, List<string> errors)
    {
        Dictionary<string, int>? headers = null;
        int sequential = 0, dataRows = 0;
        foreach (var xmlRow in worksheet.Descendants().Where(e => e.Name.LocalName == "row"))
        {
            sequential++;
            int rowNumber = int.TryParse((string?)xmlRow.Attribute("r"), out int number) && number > 0 ? number : sequential;
            var cells = xmlRow.Elements().Where(e => e.Name.LocalName == "c").ToArray();
            if (cells.Length > 16384) throw new InvalidDataException("工作表列数超出限制。");
            bool formula = cells.Any(c => c.Elements().Any(e => e.Name.LocalName == "f"));
            if (formula)
            {
                if (headers == null) throw new InvalidDataException("表头区域含公式，请改为纯文本。");
                if (++dataRows > MaxRows) throw new InvalidDataException("一次最多导入 10000 行账号。");
                errors.Add($"第 {rowNumber} 行：包含公式，未导入。请粘贴为值后重试。"); continue;
            }
            var values = new Dictionary<int, string>();
            int nextColumn = 0;
            foreach (var cell in cells)
            {
                int index = ColumnIndex((string?)cell.Attribute("r"), nextColumn); nextColumn = index + 1;
                values[index] = CellValue(cell, shared);
            }
            if (values.Values.All(string.IsNullOrWhiteSpace)) continue;
            if (headers == null)
            {
                if (sequential > 20) throw new InvalidDataException("前 20 行未找到账号表头。");
                var candidates = values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).Select(v => (Name: HeaderName(v.Value), Column: v.Key)).ToArray();
                if (!candidates.Any(v => v.Name == "设备名")) continue;
                if (candidates.GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("账号表包含重复表头。");
                headers = candidates.ToDictionary(v => v.Name, v => v.Column, StringComparer.OrdinalIgnoreCase);
                if (!headers.ContainsKey("地址")) throw new InvalidDataException("账号表缺少“地址”表头。");
                continue;
            }
            if (++dataRows > MaxRows) throw new InvalidDataException("一次最多导入 10000 行账号。");
            string Get(string header) => headers.TryGetValue(header, out int column) && values.TryGetValue(column, out string? value) ? value : "";
            string name = Get("设备名").Trim(), protocol = Get("协议").Trim();
            if (protocol.Length == 0) protocol = "RDP";
            if (name.Length == 0) { errors.Add($"第 {rowNumber} 行：设备名不能为空。"); continue; }
            if (!ProtocolPorts.TryGetValue(protocol, out string? defaultPort)) { errors.Add($"第 {rowNumber} 行：协议不支持简化账号表导入。"); continue; }
            protocol = ProtocolPorts.Keys.First(p => p.Equals(protocol, StringComparison.OrdinalIgnoreCase));
            if (Get("认证方式").Trim() is "凭据库" or "密钥") { errors.Add($"第 {rowNumber} 行：凭据库或密钥账号请使用原有连接导入方式，未转换为密码账号。"); continue; }
            string address = Get("地址").Trim(), port = Get("端口").Trim();
            if (address.Length == 0) { errors.Add($"第 {rowNumber} 行：地址不能为空。"); continue; }
            if (port.Length == 0) port = defaultPort;
            if (!decimal.TryParse(port, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal numericPort) || numericPort != decimal.Truncate(numericPort) || numericPort < 1 || numericPort > 65535)
            { errors.Add($"第 {rowNumber} 行：端口应为 1–65535 的整数。"); continue; }
            port = numericPort.ToString("0", CultureInfo.InvariantCulture);
            string passwordState = Get("密码状态").Trim(), rawPassword = Get("密码");
            bool mask = rawPassword.Length >= 3 && rawPassword.All(c => c is '*' or '•' or '●' or '·');
            string? password = !headers.ContainsKey("密码") ||
                (passwordState == "未导出" && (rawPassword.Length == 0 || mask)) ||
                (passwordState != "已包含" && mask) ? null : rawPassword;
            accounts.Add(new AccountExcelRecord(rowNumber, name, Get("备注名").Trim(), protocol, address, port,
                Get("用户名"), password, Get("域"), Get("数据源").Trim()));
        }
        if (headers == null) throw new InvalidDataException("未找到“设备名”和“地址”账号表头。");
    }
}
