using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using _1RM.Service;
using _1RM.View;
using Shawn.Utils.Wpf;
using Xunit;

namespace Tests;

public class DesktopCardSelectionTests
{
    private static ProtocolBaseViewModel Item(Action connect)
    {
        var vm = (ProtocolBaseViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ProtocolBaseViewModel));
        vm.SetIsVisible(true);
        typeof(ProtocolBaseViewModel).GetField("_cmdConnServer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, new RelayCommand(_ => connect()));
        return vm;
    }

    [Fact]
    public void CtrlTogglesAndShiftSelectsRangeWithoutConnecting()
    {
        int calls = 0;
        var items = Enumerable.Range(0, 5).Select(_ => Item(() => calls++)).ToArray();
        var selection = new DesktopCardSelection();
        selection.Select(items, items[1], ModifierKeys.Control);
        selection.Select(items, items[3], ModifierKeys.Shift);
        Assert.Equal(new[] { false, true, true, true, false }, items.Select(x => x.IsSelected));
        selection.Select(items, items[2], ModifierKeys.Control);
        Assert.False(items[2].IsSelected);
        Assert.Equal(0, calls);
        Assert.True(DesktopCardSelection.IsSelectionGesture(ModifierKeys.Control));
        Assert.True(DesktopCardSelection.IsSelectionGesture(ModifierKeys.Shift));
        Assert.False(DesktopCardSelection.IsSelectionGesture(ModifierKeys.None));
    }

    [Fact]
    public void MissingOrFilteredAnchorSelectsOnlyClickedItem()
    {
        var items = Enumerable.Range(0, 4).Select(_ => Item(() => { })).ToArray();
        var selection = new DesktopCardSelection();
        selection.Select(items, items[2], ModifierKeys.Shift);
        Assert.Equal(new[] { false, false, true, false }, items.Select(x => x.IsSelected));
        selection.Select(items, items[0], ModifierKeys.None);
        selection.Select(items.Skip(1).ToArray(), items[3], ModifierKeys.Shift);
        Assert.Equal(new[] { false, false, false, true }, items.Select(x => x.IsSelected));
    }

    [Fact]
    public void EnterOpensOnlyVisibleSelectedDevicesAndIgnoresKeyRepeat()
    {
        var calls = new int[4];
        var items = Enumerable.Range(0, 4).Select(i => Item(() => calls[i]++)).ToArray();
        int batches = 0;
        void Open(ProtocolBaseViewModel[] batch)
        {
            batches++;
            foreach (var vm in batch) vm.CmdConnServer!.Execute(null);
        }
        Assert.False(DesktopCardSelection.OpenSelected(items, false, Open));
        items[0].IsSelected = items[1].IsSelected = items[3].IsSelected = true;
        items[3].SetIsVisible(false);
        Assert.True(DesktopCardSelection.OpenSelected(items, false, Open));
        Assert.True(DesktopCardSelection.OpenSelected(items, true, Open));
        Assert.Equal(1, batches);
        Assert.Equal(new[] { 1, 1, 0, 0 }, calls);
    }
}
