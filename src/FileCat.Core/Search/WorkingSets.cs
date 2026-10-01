using System.Text.Json.Serialization;
using FileCat.Core.Diagnostics;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Core.Search;

public sealed class WorkingSetState : IVersionedState
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public List<WorkingSetRecord> Sets { get; set; } = [];
}

public sealed class WorkingSetRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public List<WorkingSetMember> Items { get; set; } = [];
}

/// <summary>One reference: where the item is and what it was when added. The item itself is never stored.</summary>
public sealed class WorkingSetMember
{
    public Location? Parent { get; set; }
    public string Name { get; set; } = string.Empty;
    public EntryKind Kind { get; set; }
    public long Size { get; set; } = -1;
    public long Modified { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Ordinal { get; set; }
}

/// <summary>
/// Named, persistent working sets (plan §11, P7; FAR's Temporary Panel): references collected by hand from many
/// folders. Membership is not ownership: removing members or deleting a set never touches the items themselves.
/// Changes are saved atomically a moment after they happen and when FileCat exits.
/// </summary>
public sealed class WorkingSets : IDisposable
{
    public const int MaxNameLength = 80;
    /// <summary>Members per set: enough for any hand-collected task while keeping revalidation and saving quick.</summary>
    public const int MaxMembers = 100_000;
    private const string Provenance = "Working set: references to items in other places";

    private readonly string _file;
    private readonly ResultSetProvider _provider;
    private readonly object _lock = new();
    private readonly object _saveLock = new();
    private readonly List<ResultSet> _sets = [];
    private readonly Timer _saveTimer;
    private bool _dirty, _disposed;

    public WorkingSets(string file, ResultSetProvider provider)
    {
        _file = file;
        _provider = provider;
        var state = JsonFileStore.Load(file, StateJsonContext.Default.WorkingSetState, WorkingSetState.CurrentSchema, () => new WorkingSetState(), out var status);
        Status = status;
        foreach (var record in state.Sets ?? [])
        {
            // A damaged or hand-edited file loses only the records it damaged.
            if (record is null || string.IsNullOrWhiteSpace(record.Id) || _sets.Any(s => s.Id == record.Id)) continue;
            var set = new ResultSet(record.Id, UniqueName(Clean(record.Name)), Provenance)
            {
                IsWorkingSet = true,
                IsComplete = true,
                CreatedUtc = record.CreatedUtc,
            };
            set.AddRange((record.Items ?? []).Where(m => m?.Parent is not null && m.Name.Length > 0 && m.Parent.Scheme != Schemes.ResultSet)
                .Select(m => (new ItemRef(m.Parent!, m.Name, m.Kind, m.Size, m.Modified) { Ordinal = m.Ordinal }, string.Empty)), MaxMembers);
            set.ModifiedUtc = record.ModifiedUtc;
            Attach(set);
        }
        _saveTimer = new Timer(_ => Flush());
        provider.WorkingSetsSource = () => All;
    }

    public StateLoadStatus Status { get; }

    /// <summary>Written by a newer FileCat: shown and usable, but never saved over (plan §19.1).</summary>
    public bool IsReadOnly => Status == StateLoadStatus.NewerSchemaReadOnly;

    /// <summary>Raised after a set is created, renamed, or deleted, or its members change.</summary>
    public event Action? Changed;

    /// <summary>Raised when the sets could not be saved (on the saving thread); they are tried again at the next change.</summary>
    public event Action<Exception>? SaveFailed;

    /// <summary>The sets by name.</summary>
    public IReadOnlyList<ResultSet> All
    {
        get
        {
            lock (_lock) return _sets.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public ResultSet? Find(string name)
    {
        var n = name.Trim();
        lock (_lock) return _sets.FirstOrDefault(s => string.Equals(s.Title, n, StringComparison.OrdinalIgnoreCase));
    }

    public ResultSet? Get(Location location) =>
        ResultSetProvider.IsWorkingSet(location) && _provider.Get(location) is { IsWorkingSet: true } set ? set : null;

    /// <summary>Why a name cannot be used, or null. Names are unique regardless of letter case.</summary>
    public string? ValidateName(string name, ResultSet? renaming = null)
    {
        var n = name.Trim();
        if (n.Length == 0) return "The name cannot be empty.";
        if (n.Length > MaxNameLength) return $"Use at most {MaxNameLength} characters.";
        if (n.Any(char.IsControl)) return "The name cannot contain control characters.";
        var existing = Find(n);
        return existing is not null && existing != renaming ? "A working set with this name already exists." : null;
    }

    /// <summary>A name not yet used: "Working set", then "Working set 2", …</summary>
    public string SuggestName(string stem = "Working set")
    {
        var name = Clean(stem);
        for (int i = 2; Find(name) is not null; i++) name = $"{Clean(stem)} {i}";
        return name;
    }

    public ResultSet Create(string name)
    {
        if (ValidateName(name) is { } problem) throw new ArgumentException(problem, nameof(name));
        var set = new ResultSet(Guid.NewGuid().ToString("N")[..12], name.Trim(), Provenance) { IsWorkingSet = true, IsComplete = true };
        lock (_lock) Attach(set);
        OnChanged();
        return set;
    }

    public void Rename(ResultSet set, string name)
    {
        if (ValidateName(name, set) is { } problem) throw new ArgumentException(problem, nameof(name));
        set.Title = name.Trim();
        OnChanged();
    }

    /// <summary>Forgets the sets; the items they refer to stay where they are.</summary>
    public int Delete(IEnumerable<ResultSet> sets)
    {
        int n = 0;
        lock (_lock)
        {
            foreach (var s in sets.ToList())
            {
                if (!_sets.Remove(s)) continue;
                s.Changed -= OnChanged;
                _provider.Forget(s);
                n++;
            }
        }
        if (n > 0) OnChanged();
        return n;
    }

    /// <summary>
    /// Adds references (nothing is copied). Items inside other result sets are added by where they really are; the
    /// list of sets itself cannot be a member. Returns how many were new; the set stops at <see cref="MaxMembers"/>.
    /// </summary>
    public int Add(ResultSet set, IEnumerable<ItemRef> items) =>
        set.AddRange(items.Where(i => i.Parent.Scheme != Schemes.ResultSet && i.Kind != EntryKind.Parent)
            .Select(i => (new ItemRef(i.Parent, i.Name, i.Kind, i.Size, i.Modified) { Ordinal = i.Ordinal, Flags = i.Flags }, string.Empty)), MaxMembers);

    private void Attach(ResultSet set)
    {
        _sets.Add(set);
        _provider.Adopt(set);
        set.Changed += OnChanged;
    }

    private void OnChanged()
    {
        lock (_lock)
        {
            _dirty = true;
            if (!_disposed) _saveTimer?.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
        }
        Changed?.Invoke();
    }

    /// <summary>Saves pending changes now. Writers are serialized and each writes the newest state.</summary>
    public void Flush()
    {
        lock (_saveLock)
        {
            WorkingSetState state;
            lock (_lock)
            {
                if (!_dirty || IsReadOnly) return;
                _dirty = false;
                state = new WorkingSetState
                {
                    Sets = _sets.Select(s => new WorkingSetRecord
                    {
                        Id = s.Id,
                        Name = s.Title,
                        CreatedUtc = s.CreatedUtc,
                        ModifiedUtc = s.ModifiedUtc,
                        Items = s.Snapshot().Select(m => new WorkingSetMember
                        {
                            Parent = m.Item.Parent,
                            Name = m.Item.Name,
                            Kind = m.Item.Kind,
                            Size = m.Item.Size,
                            Modified = m.Item.Modified,
                            Ordinal = m.Item.Ordinal,
                        }).ToList(),
                    }).ToList(),
                };
            }
            try { JsonFileStore.Save(_file, state, StateJsonContext.Default.WorkingSetState); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_lock) _dirty = true; // kept for the next change or exit
                AppLog.Error("Saving working sets failed", ex);
                SaveFailed?.Invoke(ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock) _disposed = true;
        _saveTimer.Dispose();
        Flush();
    }

    private static string Clean(string? name)
    {
        var n = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (n.Length > MaxNameLength) n = n[..MaxNameLength].TrimEnd();
        return n.Length == 0 ? "Working set" : n;
    }

    private string UniqueName(string name)
    {
        var candidate = name;
        for (int i = 2; _sets.Any(s => string.Equals(s.Title, candidate, StringComparison.OrdinalIgnoreCase)); i++) candidate = $"{name} ({i})";
        return candidate;
    }
}
