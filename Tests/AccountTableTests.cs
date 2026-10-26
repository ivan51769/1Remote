using System;
using System.Collections.ObjectModel;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Service.DataSource.Model;
using _1RM.View;
using Xunit;

namespace Tests;

public class AccountTableTests
{
    private static RDP Example() => new()
    {
        Id = "existing-device", DisplayName = "Test-amz88", Address = "192.0.2.12", Port = "3389",
        UserName = "example-user", Password = "test-secret", Domain = "example-domain",
        GatewayHostName = "gateway.example.invalid", GatewayPassword = "gateway-secret",
        Tags = new() { "example-tag" }, TreeNodes = new() { "example-folder" },
        AlternateCredentials = new ObservableCollection<Credential> { new() { Name = "alternate", Password = "alternate-secret" } },
        DataSource = new SqliteSource("AccountTableTests")
    };

    [Fact]
    public void DuplicateIsDetachedAndRetainsAdvancedSettingsWithoutChangingOriginal()
    {
        var original = Example();
        var row = new AccountTableRow(original, "test alias");
        var copy = row.Duplicate(new[] { original.DisplayName, original.DisplayName + " - 副本" }, original.DataSource!);
        Assert.True(copy.IsNew);
        Assert.True(copy.Draft.IsTmpSession());
        Assert.NotEqual(original.Id, copy.Draft.Id);
        Assert.Equal("Test-amz88 - 副本 2", copy.Name);
        var rdp = Assert.IsType<RDP>(copy.Draft);
        Assert.Equal(original.GatewayHostName, rdp.GatewayHostName);
        Assert.Equal(original.GatewayPassword, rdp.GatewayPassword);
        Assert.Equal(original.Domain, rdp.Domain);
        Assert.Equal(original.Password, rdp.Password);
        Assert.Equal(original.TreeNodes, rdp.TreeNodes);
        rdp.Tags.Clear(); rdp.AlternateCredentials[0].Password = "changed";
        copy.Address = "192.0.2.13"; copy.Password = "new-secret";
        Assert.Single(original.Tags);
        Assert.Equal("alternate-secret", original.AlternateCredentials[0].Password);
        Assert.Equal("192.0.2.12", original.Address);
        Assert.Equal("test-secret", original.Password);
        Assert.False(row.IsDirty);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("bad")]
    public void InvalidDraftPortsAreRejectedBeforeSave(string port)
    {
        var row = new AccountTableRow(Example(), "");
        row.Port = port;
        Assert.NotNull(row.Validate());
        row.Port = "3390";
        Assert.Null(row.Validate());
        row.Name = " ";
        Assert.NotNull(row.Validate());
    }

    [Fact]
    public void AssignedIdIsRetainedIfAliasSaveMustBeRetried()
    {
        var row = new AccountTableRow(Example(), "test alias", isNew: true);
        Assert.True(row.HasProtocolChanges);
        Assert.True(row.HasAliasChanges);
        var saved = row.CreateSaveCopy(); saved.Id = "generated-id";
        row.AcceptServerSaved(saved);
        Assert.False(row.IsNew);
        Assert.False(row.HasProtocolChanges);
        Assert.True(row.HasAliasChanges);
        Assert.Equal("generated-id", row.CreateSaveCopy().Id);
        row.AcceptAliasSaved();
        Assert.False(row.IsDirty);
    }

    [Fact]
    public void PrivacyNeverWritesMaskedDeviceNameOrAliasBackToConnection()
    {
        var row = new AccountTableRow(Example(), "private alias", privacy: true);
        Assert.Equal("Server-amz88", row.Name);
        Assert.Empty(row.Alias);
        Assert.NotEqual(row.Password, row.PasswordSummary);
        row.Name = "Server-wrong"; row.Alias = "";
        Assert.Equal("Test-amz88", row.Draft.DisplayName);
        Assert.Equal("private alias", row.AliasToSave);
        Assert.False(row.IsDirty);
    }

    [Fact]
    public void VaultAndKeyCredentialsCannotBeOverwrittenInline()
    {
        var source = Example(); source.InheritedCredentialName = "test-vault";
        var vault = new AccountTableRow(source, "");
        Assert.False(vault.CanEditUserName); Assert.False(vault.CanEditPassword);
        vault.UserName = "overwrite"; vault.Password = "overwrite";
        Assert.False(vault.IsDirty);
        source.InheritedCredentialName = ""; source.UsePrivateKeyForConnect = true; source.PrivateKey = "test-key";
        var key = new AccountTableRow(source, "");
        Assert.False(key.CanEditPassword);
        key.Password = "overwrite";
        Assert.False(key.IsDirty);
        Assert.Equal("test-key", Assert.IsType<RDP>(key.Draft).PrivateKey);
    }

    [Fact]
    public void ConcurrentConfigurationChangeIsDetectedAndAliasSurvivesRename()
    {
        var source = Example();
        var row = new AccountTableRow(source, "test alias");
        Assert.True(row.MatchesOriginal(source));
        source.Port = "3391";
        Assert.False(row.MatchesOriginal(source));
        row.Name = "Renamed device";
        Assert.True(row.HasAliasChanges);
        Assert.Equal("test alias", row.AliasToSave);
    }

    [Fact]
    public void EmptyAliasIsExplicitlyPreservedForCopiesAndRenames()
    {
        var original = Example();
        var copy = new AccountTableRow(original, "", isNew: true);
        Assert.True(copy.HasAliasChanges);
        var row = new AccountTableRow(original, "");
        row.Name = "Other-amz89";
        Assert.True(row.HasAliasChanges);
        Assert.Empty(row.AliasToSave);
    }

    [Fact]
    public void EnteringPasswordStopsPromptingButOtherEditsKeepTheAuthSetting()
    {
        var original = Example(); original.AskPasswordWhenConnect = true;
        var row = new AccountTableRow(original, "");
        row.Port = "3390";
        Assert.True(Assert.IsType<RDP>(row.Draft).AskPasswordWhenConnect);
        row.Password = "updated-secret";
        Assert.False(Assert.IsType<RDP>(row.Draft).AskPasswordWhenConnect);
        Assert.True(original.AskPasswordWhenConnect);
    }
}
