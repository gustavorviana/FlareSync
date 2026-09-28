using FlareSync.Core.Config;
using FlareSync.Core.Models;

namespace FlareSync.Core.Sync;

/// <summary>Last address applied to each record (<c>state.json</c>).</summary>
public sealed class SyncStateStore(ConfigPaths paths, JsonConfigStore store, TimeProvider timeProvider)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<StateEntry?> GetAsync(string provider, string hostname, IpFamily family, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(cancellationToken);
        return state.Entries.GetValueOrDefault(Key(provider, hostname, family));
    }

    public async Task<IReadOnlyDictionary<string, StateEntry>> GetAllAsync(CancellationToken cancellationToken)
        => (await LoadAsync(cancellationToken)).Entries;

    public async Task SetAsync(string provider, string hostname, IpFamily family, string address, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            state.Entries[Key(provider, hostname, family)] = new StateEntry
            {
                Address = address,
                UpdatedAt = timeProvider.GetUtcNow(),
            };
            await store.WriteAsync(paths.StateFile, state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets every family of a record (e.g. after it was removed or its settings changed).</summary>
    public async Task RemoveAsync(string provider, string hostname, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var removed = false;
            foreach (var family in Enum.GetValues<IpFamily>())
            {
                removed |= state.Entries.Remove(Key(provider, hostname, family));
            }

            if (removed)
            {
                await store.WriteAsync(paths.StateFile, state, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string Key(string provider, string hostname, IpFamily family)
        => $"{provider}|{hostname.ToLowerInvariant()}|{family}";

    private async Task<SyncState> LoadAsync(CancellationToken cancellationToken)
        => await store.ReadAsync<SyncState>(paths.StateFile, cancellationToken) ?? new SyncState();

    private sealed class SyncState
    {
        public Dictionary<string, StateEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class StateEntry
{
    public string Address { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}
