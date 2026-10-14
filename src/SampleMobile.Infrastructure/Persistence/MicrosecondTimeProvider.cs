namespace SampleMobile.Infrastructure.Persistence;

/// <summary>
/// The application clock, rounded down to PostgreSQL's microsecond timestamp precision (.NET ticks are
/// 100 ns). Without this, a value returned straight after a save (e.g. <c>readAt</c> from "mark read")
/// differs from the same value read back later, which breaks idempotent responses and equality checks.
/// </summary>
internal sealed class MicrosecondTimeProvider : TimeProvider {
    public override DateTimeOffset GetUtcNow() {
        var now = System.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
