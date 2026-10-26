using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using _1RM.View;

namespace _1RM.Service;

public sealed class DesktopCardSelection
{
    private ProtocolBaseViewModel? _anchor;

    public void Select(IReadOnlyList<ProtocolBaseViewModel> items, ProtocolBaseViewModel item, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            int start = items.ToList().IndexOf(_anchor!), end = items.ToList().IndexOf(item);
            if (end < 0) return;
            if (start < 0) { start = end; _anchor = item; }
            for (int i = 0; i < items.Count; i++)
            {
                bool inRange = i >= System.Math.Min(start, end) && i <= System.Math.Max(start, end);
                items[i].IsSelected = inRange || ((modifiers & ModifierKeys.Control) != 0 && items[i].IsSelected);
            }
        }
        else
        {
            _anchor = item;
            if ((modifiers & ModifierKeys.Control) != 0) item.IsSelected = !item.IsSelected;
        }
    }

    public static bool IsSelectionGesture(ModifierKeys modifiers) =>
        (modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;

    public static bool OpenSelected(IEnumerable<ProtocolBaseViewModel> items, bool isRepeat,
        System.Action<ProtocolBaseViewModel[]> open)
    {
        var selected = items.Where(x => x.IsSelected && x.IsVisible).ToArray();
        if (selected.Length == 0) return false;
        if (!isRepeat) open(selected);
        return true;
    }
}
