using System;
using System.Collections.ObjectModel;
using System.Linq;
using _1RM.Model.Protocol;
using _1RM.Service.DataSource.Model;
using _1RM.View;
using Xunit;

namespace Tests;

public class AccountTableHistoryTests
{
    private static RDP Original(string name = "Example-device") => new()
    {
        Id = "saved-" + name, DisplayName = name, Address = "192.0.2.10", Port = "3389",
        UserName = "original-user", Password = "original-password", AskPasswordWhenConnect = true,
        Domain = "original-domain", GatewayHostName = "gateway.example.invalid", GatewayPassword = "gateway-password",
        Tags = new() { "original-tag" }, DataSource = new SqliteSource("AccountHistoryTests")
    };

    private static void Edit(AccountTableHistory history, AccountTableRow row, Action change)
    {
        var before = row.CaptureSnapshot();
        change();
        var after = row.CaptureSnapshot();
        if (!before.StateEquals(after)) history.Record(() => row.RestoreSnapshot(before), () => row.RestoreSnapshot(after));
    }

    [Fact]
    public void UndoAndRedoRestoreDetachedFieldsAuthenticationAndOriginalDirtyBaseline()
    {
        var original = Original();
        var row = new AccountTableRow(original, "original alias");
        var history = new AccountTableHistory();
        Edit(history, row, () =>
        {
            row.Name = "Changed-device";
            row.Alias = "changed alias";
            row.Address = "192.0.2.11";
            row.Port = "3390";
            row.UserName = "changed-user";
            row.Password = "changed-password";
            row.Domain = "changed-domain";
        });
        Assert.True(row.IsDirty);
        Assert.False(Assert.IsType<RDP>(row.Draft).AskPasswordWhenConnect);
        Assert.Equal("original-password", original.Password);
        Assert.True(original.AskPasswordWhenConnect);

        Assert.True(history.Undo());
        Assert.Equal(original.DisplayName, row.Name);
        Assert.Equal("original alias", row.Alias);
        Assert.Equal(original.Address, row.Address);
        Assert.Equal(original.Port, row.Port);
        Assert.Equal(original.UserName, row.UserName);
        Assert.Equal(original.Password, row.Password);
        Assert.Equal(original.Domain, row.Domain);
        Assert.True(Assert.IsType<RDP>(row.Draft).AskPasswordWhenConnect);
        Assert.False(row.HasProtocolChanges);
        Assert.False(row.HasAliasChanges);
        Assert.True(row.MatchesOriginal(original));

        Assert.True(history.Redo());
        Assert.Equal("Changed-device", row.Name);
        Assert.Equal("changed alias", row.Alias);
        Assert.Equal("192.0.2.11", row.Address);
        Assert.Equal("3390", row.Port);
        Assert.Equal("changed-user", row.UserName);
        Assert.Equal("changed-password", row.Password);
        Assert.Equal("changed-domain", row.Domain);
        Assert.Equal(original.GatewayPassword, Assert.IsType<RDP>(row.Draft).GatewayPassword);
        Assert.False(Assert.IsType<RDP>(row.Draft).AskPasswordWhenConnect);
        Assert.True(row.IsDirty);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void SnapshotsRemainIndependentAfterRepeatedRestoresAndDraftMutation()
    {
        var row = new AccountTableRow(Original(), "saved alias");
        var before = row.CaptureSnapshot();
        row.Name = "Edited-name";
        var after = row.CaptureSnapshot();
        Assert.False(before.StateEquals(after));
        row.RestoreSnapshot(before);
        row.Draft.Tags.Add("later-mutation");
        row.RestoreSnapshot(after);
        Assert.DoesNotContain("later-mutation", row.Draft.Tags);
        row.RestoreSnapshot(before);
        Assert.Single(row.Draft.Tags);
        Assert.False(row.IsDirty);
        Assert.True(before.StateEquals(row.CaptureSnapshot()));
    }

    [Fact]
    public void NewEditAfterUndoDiscardsOnlyTheOldRedoBranch()
    {
        var row = new AccountTableRow(Original(), "");
        var history = new AccountTableHistory();
        Edit(history, row, () => row.Port = "3390");
        Edit(history, row, () => row.Port = "3391");
        Assert.True(history.Undo());
        Assert.Equal("3390", row.Port);
        Assert.True(history.CanRedo);
        Edit(history, row, () => row.Port = "3392");
        Assert.False(history.CanRedo);
        Assert.False(history.Redo());
        Assert.True(history.Undo());
        Assert.Equal("3390", row.Port);
        Assert.True(history.Undo());
        Assert.Equal("3389", row.Port);
        Assert.False(row.IsDirty);
    }

    [Fact]
    public void BatchedCopyImportAndRemovePreserveRowOrderAndObjectIdentityAcrossReplay()
    {
        var first = new AccountTableRow(Original("First"), "first alias");
        var second = new AccountTableRow(Original("Second"), "second alias");
        var rows = new ObservableCollection<AccountTableRow> { first, second };
        var copy = first.Duplicate(rows.Select(r => r.Name), first.Draft.DataSource!);
        var secondCopy = second.Duplicate(rows.Select(r => r.Name), second.Draft.DataSource!);
        var imported = new AccountTableRow(new RDP
        {
            DisplayName = "Imported account", Address = "192.0.2.50", Port = "3395", UserName = "import-user",
            DataSource = first.Draft.DataSource
        }, "import alias", isNew: true);
        var batch = new[] { copy, secondCopy, imported };
        var history = new AccountTableHistory();

        void AddBatch() { for (int i = 0; i < batch.Length; i++) rows.Insert(2 + i, batch[i]); }
        void RemoveBatch() { foreach (var row in batch) rows.Remove(row); }
        AddBatch(); history.Record(RemoveBatch, AddBatch);
        string copyName = copy.Name;
        Edit(history, copy, () => { copy.Name = "Edited copy"; copy.Password = "copy-password"; });
        var removed = new[] { (Index: 2, Row: copy), (Index: 4, Row: imported) };
        void RemoveDrafts() { foreach (var item in removed.Reverse()) rows.RemoveAt(item.Index); }
        void RestoreDrafts() { foreach (var item in removed) rows.Insert(item.Index, item.Row); }
        RemoveDrafts(); history.Record(RestoreDrafts, RemoveDrafts);
        Assert.Equal(new[] { first, second, secondCopy }, rows);

        Assert.True(history.Undo());
        Assert.Equal(new[] { first, second, copy, secondCopy, imported }, rows);
        Assert.Same(copy, rows[2]); Assert.Same(imported, rows[4]);
        Assert.Equal("Edited copy", copy.Name);
        Assert.True(history.Undo());
        Assert.Equal(copyName, copy.Name);
        Assert.Equal("original-password", copy.Password);
        Assert.True(history.Undo());
        Assert.Equal(new[] { first, second }, rows);
        Assert.False(first.IsDirty); Assert.False(second.IsDirty);

        Assert.True(history.Redo()); Assert.Same(copy, rows[2]); Assert.Same(imported, rows[4]);
        Assert.True(history.Redo()); Assert.Equal("copy-password", rows[2].Password);
        Assert.True(history.Redo()); Assert.Equal(new[] { first, second, secondCopy }, rows);
        Assert.True(secondCopy.IsNew);
    }

    [Fact]
    public void BoundedHistoryDropsOldCommandsWithoutChangingCurrentDraft()
    {
        var row = new AccountTableRow(Original(), "");
        var history = new AccountTableHistory(capacity: 2);
        Edit(history, row, () => row.Port = "3390");
        Edit(history, row, () => row.Port = "3391");
        Edit(history, row, () => row.Port = "3392");
        Assert.Equal("3392", row.Port);
        Assert.True(history.Undo()); Assert.Equal("3391", row.Port);
        Assert.True(history.Undo()); Assert.Equal("3390", row.Port);
        Assert.False(history.Undo());
        Assert.True(history.Redo()); Assert.True(history.Redo());
        Assert.Equal("3392", row.Port);
        Assert.False(history.Redo());
    }

    [Fact]
    public void SaveBoundaryClearsUndoAndRedoWithoutRevertingAssignedIdsOrUnsavedAlias()
    {
        var row = new AccountTableRow(Original(), "new alias", isNew: true);
        var history = new AccountTableHistory();
        Edit(history, row, () => row.Port = "3390");
        var saved = row.CreateSaveCopy(); saved.Id = "new-database-id";
        row.AcceptServerSaved(saved);
        history.Clear();
        Assert.False(history.CanUndo); Assert.False(history.CanRedo);
        Assert.False(history.Undo()); Assert.False(history.Redo());
        Assert.Equal("new-database-id", row.Draft.Id);
        Assert.False(row.IsNew);
        Assert.False(row.HasProtocolChanges);
        Assert.True(row.HasAliasChanges);

        row.AcceptAliasSaved();
        Edit(history, row, () => row.Domain = "changed-after-save");
        Assert.True(history.Undo());
        Assert.False(row.IsDirty);
        Assert.Equal("new-database-id", row.Draft.Id);
        history.Clear();
        Assert.False(history.Redo());
        Assert.Equal("original-domain", row.Domain);
    }
}
