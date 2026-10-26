using System;
using System.Collections.Generic;

namespace _1RM.View;

/// <summary>Bounded, in-memory history of unapplied account-table changes.</summary>
public sealed class AccountTableHistory
{
    private sealed record Entry(Action Undo, Action Redo);
    private readonly List<Entry> _undo = new();
    private readonly List<Entry> _redo = new();
    private readonly int _capacity;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsReplaying { get; private set; }
    public event Action? Changed;

    public AccountTableHistory(int capacity = 100)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }
    public void Record(Action undo, Action redo)
    {
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(redo);
        if (IsReplaying) return;
        _undo.Add(new Entry(undo, redo));
        if (_undo.Count > _capacity) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }
    public bool Undo() => Apply(_undo, _redo, true);
    public bool Redo() => Apply(_redo, _undo, false);
    private bool Apply(List<Entry> source, List<Entry> target, bool undo)
    {
        if (IsReplaying || source.Count == 0) return false;
        var entry = source[^1];
        IsReplaying = true;
        try
        {
            if (undo) entry.Undo(); else entry.Redo();
            source.RemoveAt(source.Count - 1);
            target.Add(entry);
        }
        finally { IsReplaying = false; }
        Changed?.Invoke();
        return true;
    }
    public void Clear()
    {
        _undo.Clear(); _redo.Clear();
        Changed?.Invoke();
    }
}
