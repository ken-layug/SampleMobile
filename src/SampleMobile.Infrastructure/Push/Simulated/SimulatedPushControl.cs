using System.Collections.Concurrent;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Infrastructure.Push.Simulated;

/// <summary>
/// The simulator's in-memory state: outcomes scripted per address and a ring buffer of handled messages.
/// Singleton, and deliberately not persisted — a restart clears scripts (the simulator then accepts
/// everything) while the delivery attempts it produced remain in PostgreSQL.
/// </summary>
public sealed class SimulatedPushControl : ISimulatedPushControl {
    private const int Capacity = 200;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<DispatchOutcome>> _scripts = new(StringComparer.Ordinal);
    private readonly LinkedList<SimulatedPushRecord> _log = new();
    private readonly Lock _logLock = new();

    public string ProviderId => PushProviderIds.Simulated;

    public void ScriptOutcomes(string address, IReadOnlyList<DispatchOutcome> outcomes) {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        _scripts[address] = new ConcurrentQueue<DispatchOutcome>(outcomes);
    }

    public IReadOnlyList<SimulatedPushRecord> GetRecentMessages(int maxCount) {
        lock (_logLock) {
            return _log.Take(maxCount).ToList();
        }
    }

    internal DispatchOutcome NextOutcomeFor(string address) =>
        _scripts.TryGetValue(address, out var queue) && queue.TryDequeue(out var outcome)
            ? outcome
            : DispatchOutcome.Accepted;

    internal void Record(SimulatedPushRecord record) {
        lock (_logLock) {
            _log.AddFirst(record);
            while (_log.Count > Capacity) {
                _log.RemoveLast();
            }
        }
    }
}
