using SampleMobile.Domain.Entities;
using SampleMobile.Domain.Services;

namespace SampleMobile.Tests.Domain;

public class DeliveryJobTests {
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    [Fact]
    public void A_new_job_is_pending_and_due_immediately() {
        var job = CreateJob();

        Assert.Equal(DeliveryJobStatus.Pending, job.Status);
        Assert.True(job.IsDue(Now));
    }

    [Fact]
    public void Claiming_leases_the_job_and_opens_an_attempt() {
        var job = CreateJob();

        var attempt = job.Claim(Now, Lease);

        Assert.Equal(DeliveryJobStatus.Processing, job.Status);
        Assert.Equal(Now + Lease, job.LockedUntil);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal(DeliveryAttemptStatus.InProgress, attempt.Status);
        Assert.False(job.IsDue(Now.AddSeconds(30)));
    }

    [Fact]
    public void An_expired_lease_lets_another_worker_reclaim_and_marks_the_lost_attempt_abandoned() {
        var job = CreateJob();
        var first = job.Claim(Now, Lease);

        var second = job.Claim(Now + Lease, Lease);

        Assert.Equal(DeliveryAttemptStatus.Abandoned, first.Status);
        Assert.Equal(2, second.AttemptNumber);
        Assert.True(job.IsClaimedFor(2));
        Assert.False(job.IsClaimedFor(1));
    }

    [Fact]
    public void A_job_that_is_not_due_cannot_be_claimed() {
        var job = CreateJob();
        job.Claim(Now, Lease);

        Assert.Throws<InvalidOperationException>(() => job.Claim(Now.AddSeconds(10), Lease));
    }

    [Fact]
    public void A_failure_with_a_retry_time_schedules_a_retry() {
        var job = CreateJob();
        job.Claim(Now, Lease);

        job.RecordFailure(DispatchOutcome.TransientFailure, "unavailable", "try later", false, Now.AddSeconds(10), Now);

        Assert.Equal(DeliveryJobStatus.RetryScheduled, job.Status);
        Assert.Equal(Now.AddSeconds(10), job.NextAttemptAt);
        Assert.False(job.IsDue(Now.AddSeconds(9)));
        Assert.True(job.IsDue(Now.AddSeconds(10)));
    }

    [Fact]
    public void Requeueing_a_failed_job_restores_the_retry_budget_but_keeps_attempt_numbers() {
        var job = CreateJob();
        job.Claim(Now, Lease);
        job.RecordFailure(DispatchOutcome.AuthenticationFailure, "auth", "bad credentials", false, retryAt: null, Now);

        job.Requeue(Now.AddMinutes(1));
        var attempt = job.Claim(Now.AddMinutes(1), Lease);

        Assert.Equal(2, attempt.AttemptNumber);
        Assert.Equal(1, job.AttemptsInCurrentRun);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 60)]
    public void Retry_delays_grow_exponentially_up_to_the_cap(int attemptsMade, int expectedSeconds) {
        var policy = new DeliveryRetryPolicy(maxAttempts: 10, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), policy.GetDelayAfter(attemptsMade));
    }

    [Fact]
    public void No_retry_is_offered_once_the_attempt_budget_is_spent() {
        var policy = new DeliveryRetryPolicy(maxAttempts: 3, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));

        Assert.NotNull(policy.GetDelayAfter(2));
        Assert.Null(policy.GetDelayAfter(3));
    }

    private static DeliveryJob CreateJob() =>
        DeliveryJob.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "simulated", Now);
}
