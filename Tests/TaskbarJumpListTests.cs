using System;
using System.Linq;
using System.Windows.Shell;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using Xunit;

namespace Tests;

public class TaskbarJumpListTests
{
    [Fact]
    public void DefaultRecentListContainsOnlyFiveNewestDevices()
    {
        var now = DateTime.Now;
        var history = Enumerable.Range(0, 8).Select(i =>
            ((ProtocolBase)new RDP { Id = "device-" + i, DisplayName = "Device " + i }, now.AddMinutes(i)));
        Assert.Equal(new[] { "Device 7", "Device 6", "Device 5", "Device 4", "Device 3" },
            TaskbarJumpListService.BuildRecentTasks(history, @"C:\Apps\1Remote.exe").Select(x => x.Title));
    }

    [Fact]
    public void ShellRejectionRetriesAreBoundedAndRespectUserRemoval()
    {
        Assert.True(TaskbarJumpListService.ShouldRetry(new[] { JumpItemRejectionReason.InvalidItem }, 0));
        Assert.True(TaskbarJumpListService.ShouldRetry(new[] { JumpItemRejectionReason.InvalidItem }, 1));
        Assert.False(TaskbarJumpListService.ShouldRetry(new[] { JumpItemRejectionReason.InvalidItem }, 2));
        Assert.False(TaskbarJumpListService.ShouldRetry(new[] { JumpItemRejectionReason.RemovedByUser }, 0));
        Assert.False(TaskbarJumpListService.ShouldRetry(Array.Empty<JumpItemRejectionReason>(), 0));
    }

    [Fact]
    public void RecentListExcludesUnusedAndTemporaryConnectionsAndOrdersNewestFirst()
    {
        var old = new RDP { Id = "saved-old", DisplayName = "Old" };
        var recent = new RDP { Id = "saved-new", DisplayName = "New" };
        var unused = new RDP { Id = "unused", DisplayName = "Unused" };
        var temporary = new RDP { DisplayName = "Temporary" };
        var now = DateTime.Now;
        var tasks = TaskbarJumpListService.BuildRecentTasks(new (ProtocolBase, DateTime)[]
        {
            (old, now.AddDays(-1)), (unused, DateTime.MinValue),
            (recent, now), (temporary, now.AddMinutes(1)), (old, now.AddDays(-2))
        }, @"C:\Apps\1Remote.exe");
        Assert.Equal(new[] { "New", "Old" }, tasks.Select(x => x.Title));
        Assert.All(tasks, x => Assert.Equal("最近连接", x.CustomCategory));
        Assert.Single(TaskbarJumpListService.BuildRecentTasks(new[] { ((ProtocolBase)old, now), ((ProtocolBase)recent, now.AddMinutes(1)) }, @"C:\Apps\1Remote.exe", 1));
    }

    [Fact]
    public void PinnedLinkSurvivesRenameAndNeverEmbedsCredentials()
    {
        var server = new RDP { Id = "saved-id", DisplayName = "Device", Address = "192.0.2.4", Password = "private-test-password" };
        var task = TaskbarJumpListService.CreateTask(server, @"C:\Apps With Spaces\1Remote.exe");
        Assert.Matches("^SESSION:[0-9A-F]{64}$", task.Arguments);
        Assert.DoesNotContain(server.Password, task.Arguments);
        Assert.DoesNotContain(server.Address, task.Arguments);
        server.DisplayName = "Renamed device";
        Assert.Same(server, TaskbarJumpListService.Resolve(task.Arguments, new[] { server }));
        Assert.Null(TaskbarJumpListService.Resolve(task.Arguments, Array.Empty<ProtocolBase>()));
        Assert.Null(TaskbarJumpListService.Resolve("SESSION:bad --install", new[] { server }));
        Assert.Equal(@"C:\Apps With Spaces", task.WorkingDirectory);
    }
}
