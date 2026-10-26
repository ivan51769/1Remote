using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using _1RM.Model.Protocol;
using _1RM.Service;
using _1RM.View;
using Xunit;

namespace Tests;

public class AccountTableExcelTests
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static AccountTableRow Row(string name = "示例设备-gqamz88", string password = "00123-private-password") =>
        new(new RDP { Id = "excel-test-original", DisplayName = name, Address = "192.0.2.10", Port = "3389", UserName = "001-user", Password = password, Domain = "example" }, "示例备注");

    [Fact]
    public void DefaultExportIsRealXlsxAndNeverContainsPassword()
    {
        WithFile(path =>
        {
            var row = Row();
            AccountTableExcel.Export(path, new[] { row });
            using (var zip = ZipFile.OpenRead(path))
            {
                Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
                Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
                Assert.All(zip.Entries, entry => Assert.DoesNotContain(row.Password, ReadText(entry)));
            }
            var result = AccountTableExcel.Read(path);
            Assert.Empty(result.Errors);
            var imported = Assert.Single(result.Accounts);
            Assert.Equal(row.Name, imported.Name);
            Assert.Equal(row.Alias, imported.Alias);
            Assert.Equal("001-user", imported.UserName);
            Assert.Equal("3389", imported.Port);
            Assert.Null(imported.Password);
        });
    }

    [Theory]
    [InlineData("000123")]
    [InlineData("******")]
    [InlineData("=HYPERLINK(\"https://example.invalid\",\"text\")")]
    public void ExplicitPasswordExportPreservesTextIncludingZerosAndFormulaLikeValues(string password)
    {
        WithFile(path =>
        {
            AccountTableExcel.Export(path, new[] { Row("=1+1", password) }, includePasswords: true);
            using (var zip = ZipFile.OpenRead(path))
            {
                var sheet = XDocument.Parse(ReadText(zip.GetEntry("xl/worksheets/sheet1.xml")!));
                Assert.Empty(sheet.Descendants(S + "f"));
                Assert.All(sheet.Descendants(S + "c"), cell => Assert.Equal("inlineStr", (string?)cell.Attribute("t")));
            }
            var result = AccountTableExcel.Read(path);
            Assert.Empty(result.Errors);
            var imported = Assert.Single(result.Accounts);
            Assert.Equal("=1+1", imported.Name);
            Assert.Equal(password, imported.Password);
        });
    }

    [Fact]
    public void PasswordFilledIntoDefaultExportIsImportedWithoutEditingMetadata()
    {
        WithFile(path =>
        {
            AccountTableExcel.Export(path, new[] { Row() });
            EditSheet(path, sheet => sheet.Descendants(S + "c").Single(c => (string?)c.Attribute("r") == "G2")
                .Descendants(S + "t").Single().Value = "0007-new-password");
            var result = AccountTableExcel.Read(path);
            Assert.Empty(result.Errors);
            Assert.Equal("0007-new-password", Assert.Single(result.Accounts).Password);
        });
    }

    [Fact]
    public void PrivacyExportMasksEvenRowsConstructedOutsidePrivacyMode()
    {
        WithFile(path =>
        {
            var row = Row("Private-Server-gqamz88");
            AccountTableExcel.Export(path, new[] { row }, privacyMode: true);
            using (var zip = ZipFile.OpenRead(path))
                Assert.All(zip.Entries, entry =>
                {
                    string xml = ReadText(entry);
                    Assert.DoesNotContain(row.Draft.DisplayName, xml);
                    Assert.DoesNotContain(row.Alias, xml);
                });
            var imported = Assert.Single(AccountTableExcel.Read(path).Accounts);
            Assert.Equal("Server-amz88", imported.Name);
            Assert.Equal("", imported.Alias);
        });
    }

    [Fact]
    public void OrdinaryExcelSharedStringsNumericPortsAndCommonHeaderAliasesAreSupported()
    {
        WithFile(path =>
        {
            AccountTableExcel.Export(path, Array.Empty<AccountTableRow>());
            var strings = new[] { "名称", "IP / 地址", "协议", "端口", "账号", "密码", "示例 SSH", "192.0.2.20", "SSH", "000123", "001-account" };
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                SetPart(zip, "xl/sharedStrings.xml", new XElement(S + "sst", strings.Select(text => new XElement(S + "si", new XElement(S + "t", text)))).ToString());
                SetPart(zip, "xl/worksheets/sheet1.xml", new XElement(S + "worksheet", new XElement(S + "sheetData",
                    new XElement(S + "row", new XAttribute("r", 1), Enumerable.Range(0, 6).Select(i => SharedCell(((char)('A' + i)) + "1", i))),
                    new XElement(S + "row", new XAttribute("r", 2), SharedCell("A2", 6), SharedCell("B2", 7), SharedCell("C2", 8),
                        new XElement(S + "c", new XAttribute("r", "D2"), new XElement(S + "v", "2.2E1")), SharedCell("E2", 10), SharedCell("F2", 9)))).ToString());
            }
            var result = AccountTableExcel.Read(path);
            Assert.Empty(result.Errors);
            var imported = Assert.Single(result.Accounts);
            Assert.Equal("示例 SSH", imported.Name);
            Assert.Equal("22", imported.Port);
            Assert.Equal("001-account", imported.UserName);
            Assert.Equal("000123", imported.Password);
        });
    }

    [Fact]
    public void DuplicateCanonicalHeadersAndFormulasCannotBeImportedSilently()
    {
        WithFile(path =>
        {
            AccountTableExcel.Export(path, new[] { Row() });
            EditSheet(path, sheet => sheet.Descendants(S + "c").Single(c => (string?)c.Attribute("r") == "B1")
                .Descendants(S + "t").Single().Value = "名称");
            var duplicate = AccountTableExcel.Read(path);
            Assert.Empty(duplicate.Accounts);
            Assert.Contains(duplicate.Errors, error => error.Contains("重复表头"));

            AccountTableExcel.Export(path, new[] { Row() });
            EditSheet(path, sheet => sheet.Descendants(S + "c").Single(c => (string?)c.Attribute("r") == "G2")
                .Add(new XElement(S + "f", "1+1")));
            var formula = AccountTableExcel.Read(path);
            Assert.Empty(formula.Accounts);
            Assert.Contains(formula.Errors, error => error.Contains("第 2 行") && error.Contains("公式"));
        });
    }

    [Fact]
    public void OmittedMasksAndUnsupportedAuthenticationAreNotConvertedToPasswords()
    {
        WithFile(path =>
        {
            AccountTableExcel.Export(path, new[] { Row() });
            EditSheet(path, sheet => sheet.Descendants(S + "c").Single(c => (string?)c.Attribute("r") == "G2")
                .Descendants(S + "t").Single().Value = "••••••••");
            Assert.Null(Assert.Single(AccountTableExcel.Read(path).Accounts).Password);

            EditSheet(path, sheet => sheet.Descendants(S + "c").Single(c => (string?)c.Attribute("r") == "K2")
                .Descendants(S + "t").Single().Value = "密钥");
            var result = AccountTableExcel.Read(path);
            Assert.Empty(result.Accounts);
            Assert.Contains(result.Errors, error => error.Contains("第 2 行") && error.Contains("密钥"));
        });
    }

    [Fact]
    public void ExternalEntitiesAndOversizedPartsAreRejected()
    {
        WithFile(path =>
        {
            AccountTableExcel.Export(path, Array.Empty<AccountTableRow>());
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
                SetPart(zip, "xl/workbook.xml", "<!DOCTYPE workbook [<!ENTITY external SYSTEM 'file:///does-not-exist'>]><workbook>&external;</workbook>");
            Assert.NotEmpty(AccountTableExcel.Read(path).Errors);

            AccountTableExcel.Export(path, Array.Empty<AccountTableRow>());
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            using (var stream = zip.CreateEntry("oversized.xml").Open())
            {
                var block = new byte[1024 * 1024];
                for (int i = 0; i < 17; i++) stream.Write(block);
            }
            var oversized = AccountTableExcel.Read(path);
            Assert.Empty(oversized.Accounts);
            Assert.Contains(oversized.Errors, error => error.Contains("过大"));
        });
    }

    [Fact]
    public void FailedExportPreservesExistingFile()
    {
        WithFile(path =>
        {
            File.WriteAllText(path, "existing-file");
            Assert.Throws<InvalidOperationException>(() => AccountTableExcel.Export(path, new[] { Row(new string('a', 32768)) }));
            Assert.Equal("existing-file", File.ReadAllText(path));
        });
    }

    private static XElement SharedCell(string reference, int index) => new(S + "c", new XAttribute("r", reference), new XAttribute("t", "s"), new XElement(S + "v", index));
    private static string ReadText(ZipArchiveEntry entry) { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }
    private static void SetPart(ZipArchive zip, string name, string xml)
    {
        zip.GetEntry(name)?.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }
    private static void EditSheet(string path, Action<XDocument> edit)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var sheet = XDocument.Parse(ReadText(zip.GetEntry("xl/worksheets/sheet1.xml")!));
        edit(sheet); SetPart(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
    }
    private static void WithFile(Action<string> test)
    {
        string path = Path.Combine(Path.GetTempPath(), "1remote-excel-" + Guid.NewGuid() + ".xlsx");
        try { test(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
