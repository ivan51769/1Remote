using System;
using System.IO;
using System.Linq;
using _1RM.Model.Protocol;
using _1RM.Service;
using _1RM.Service.DataSource.DAO;
using _1RM.Service.DataSource.DAO.Dapper;
using _1RM.Service.DataSource.Model;
using _1RM.View;
using Xunit;

namespace Tests;

public class AccountTablePersistenceTests
{
    [Fact]
    public void DuplicateAndEditRoundTripThroughSqliteWithoutChangingOriginalOrStoringPlaintextPasswords()
    {
        // Every database operation stays in a new, isolated temporary directory.
        var directory = Path.Combine(Path.GetTempPath(), "1remote-account-table-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "accounts.sqlite");
        File.WriteAllBytes(file, Array.Empty<byte>());
        var source = new SqliteSource("AccountTablePersistenceTests")
        {
            Path = file,
            DataSourceName = "Isolated account table test"
        };
        var database = source.GetDataBase();
        try
        {
            Success(database.OpenNewConnection($"Data Source={file};Version=3;Pooling=False;"));
            Success(database.InitTables());

            var original = new RDP
            {
                DisplayName = "Test-device01",
                Address = "192.0.2.20",
                Port = "3390",
                UserName = "test-account",
                Password = "test-only-original-password",
                Domain = "test-domain",
                GatewayHostName = "gateway.example.invalid",
                GatewayUserName = "test-gateway-user",
                GatewayPassword = "test-only-gateway-password",
                GatewayMode = EGatewayMode.UseTheseGatewayServerSettings,
                IsAdministrativePurposes = true,
                Tags = new() { "test-tag" },
                TreeNodes = new() { "test-folder" },
                DataSource = source
            };
            Success(source.Database_InsertServer(original));
            Assert.False(original.IsTmpSession());
            Assert.NotEmpty(original.Id);

            var firstRead = database.GetServers();
            Success(firstRead);
            var storedOriginal = Assert.IsType<RDP>(Assert.Single(firstRead.Items));
            Assert.NotEqual(original.Password, storedOriginal.Password);
            Assert.NotEqual(original.GatewayPassword, storedOriginal.GatewayPassword);
            storedOriginal.DataSource = source;

            var originalRow = new AccountTableRow(storedOriginal, "");
            var duplicate = originalRow.Duplicate(new[] { original.DisplayName }, source);
            Assert.True(duplicate.Draft.IsTmpSession());
            Assert.NotEqual(original.Id, duplicate.Draft.Id);
            Assert.True(duplicate.IsNew);
            Assert.Equal(original.Password, duplicate.Password);
            // Constructing the table row decrypts its detached draft, not the loaded source.
            Assert.NotEqual(original.Password, storedOriginal.Password);

            var insertedCopy = duplicate.CreateSaveCopy();
            Success(source.Database_InsertServer(insertedCopy));
            duplicate.AcceptServerSaved(insertedCopy);
            duplicate.AcceptAliasSaved();
            Assert.False(duplicate.IsNew);
            Assert.False(duplicate.HasProtocolChanges);
            Assert.False(insertedCopy.IsTmpSession());
            Assert.NotEqual(original.Id, insertedCopy.Id);
            Assert.Equal(insertedCopy.Id, duplicate.Draft.Id);

            var afterInsert = database.GetServers();
            Success(afterInsert);
            Assert.Equal(2, afterInsert.Items.Count);
            var storedCopy = Assert.IsType<RDP>(afterInsert.Items.Single(x => x.Id == insertedCopy.Id));
            Assert.NotEqual(original.Password, storedCopy.Password);
            Assert.NotEqual(original.GatewayPassword, storedCopy.GatewayPassword);
            storedCopy.DecryptToConnectLevel();
            Assert.Equal(original.Password, storedCopy.Password);
            Assert.Equal(original.GatewayPassword, storedCopy.GatewayPassword);
            Assert.Equal(original.GatewayHostName, storedCopy.GatewayHostName);
            Assert.Equal(original.GatewayMode, storedCopy.GatewayMode);
            Assert.Equal(original.Domain, storedCopy.Domain);
            Assert.Equal(original.IsAdministrativePurposes, storedCopy.IsAdministrativePurposes);
            Assert.Equal(original.Tags, storedCopy.Tags);
            Assert.Equal(original.TreeNodes, storedCopy.TreeNodes);

            duplicate.Address = "192.0.2.21";
            duplicate.Port = "3391";
            duplicate.UserName = "changed-test-account";
            duplicate.Password = "test-only-updated-password";
            var editedCopy = duplicate.CreateSaveCopy();
            Success(source.Database_UpdateServer(editedCopy));
            duplicate.AcceptServerSaved(editedCopy);
            Assert.False(duplicate.IsDirty);
            Assert.Equal(insertedCopy.Id, editedCopy.Id);

            var afterUpdate = database.GetServers();
            Success(afterUpdate);
            Assert.Equal(2, afterUpdate.Items.Count);
            var persistedOriginal = Assert.IsType<RDP>(afterUpdate.Items.Single(x => x.Id == original.Id));
            var persistedCopy = Assert.IsType<RDP>(afterUpdate.Items.Single(x => x.Id == insertedCopy.Id));
            Assert.NotEqual(duplicate.Password, persistedCopy.Password);
            persistedOriginal.DecryptToConnectLevel();
            persistedCopy.DecryptToConnectLevel();
            Assert.Equal(original.Address, persistedOriginal.Address);
            Assert.Equal(original.Port, persistedOriginal.Port);
            Assert.Equal(original.UserName, persistedOriginal.UserName);
            Assert.Equal(original.Password, persistedOriginal.Password);
            Assert.Equal("192.0.2.21", persistedCopy.Address);
            Assert.Equal("3391", persistedCopy.Port);
            Assert.Equal("changed-test-account", persistedCopy.UserName);
            Assert.Equal("test-only-updated-password", persistedCopy.Password);
            Assert.Equal(original.GatewayPassword, persistedCopy.GatewayPassword);
            Assert.Equal(original.Domain, persistedCopy.Domain);
        }
        finally
        {
            database.CloseConnection();
            if (database is DapperDatabase dapper) dapper.Connection?.Dispose();
            // Delete only known SQLite files; never recursively delete a computed directory.
            foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
                if (File.Exists(file + suffix)) File.Delete(file + suffix);
            Directory.Delete(directory);
        }
    }

    private static void Success(Result result) => Assert.True(result.IsSuccess, result.ErrorInfo);
}
