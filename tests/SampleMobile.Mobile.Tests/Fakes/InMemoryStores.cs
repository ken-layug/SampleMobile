using System.Collections.Concurrent;
using SampleMobile.Mobile.Core.Platform;

namespace SampleMobile.Mobile.Tests.Fakes;

public sealed class InMemorySecureStore : ISecureStore {
    public ConcurrentDictionary<string, string> Values { get; } = new();

    public Task<string?> GetAsync(string key) => Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string value) {
        Values[key] = value;
        return Task.CompletedTask;
    }

    public void Remove(string key) => Values.TryRemove(key, out _);
}

public sealed class InMemoryPreferences : IAppPreferences {
    public ConcurrentDictionary<string, string> Values { get; } = new();

    public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value) => Values[key] = value;

    public void Remove(string key) => Values.TryRemove(key, out _);
}

/// <summary>Runs "UI" work immediately on the calling thread.</summary>
public sealed class InlineDispatcher : IMainThreadDispatcher {
    public void Dispatch(Action action) => action();
}
