using HarborOps.Modules.Identity.Application;
using Microsoft.Extensions.Options;

namespace HarborOps.UnitTests.Identity;

public sealed class LoginAttemptThrottleTests
{
    [Fact]
    public void Failed_attempts_below_the_account_limit_are_allowed()
    {
        var throttle = CreateThrottle();

        for (var index = 0; index < 5; index++)
        {
            Assert.True(throttle.TryBegin("operator@harborops.test", out var lease, out _));
            lease!.Complete(succeeded: false);
        }
    }

    [Fact]
    public void Sixth_failed_attempt_for_the_same_normalized_email_is_throttled()
    {
        var throttle = CreateThrottle();

        for (var index = 0; index < 5; index++)
        {
            Assert.True(throttle.TryBegin("operator@harborops.test", out var lease, out _));
            lease!.Complete(succeeded: false);
        }

        Assert.False(throttle.TryBegin(" OPERATOR@harborops.test ", out var blockedLease, out var retryAfter));
        Assert.Null(blockedLease);
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task Parallel_attempts_cannot_bypass_the_account_limit()
    {
        var throttle = CreateThrottle();
        using var gate = new Barrier(7);

        var results = await Task.WhenAll(Enumerable.Range(0, 7).Select(attempt => Task.Run(() =>
        {
            gate.SignalAndWait();
            var allowed = throttle.TryBegin("operator@harborops.test", out var lease, out _);
            return (allowed, lease);
        })));

        Assert.Equal(5, results.Count(result => result.allowed));
        foreach (var result in results.Where(result => result.allowed))
        {
            result.lease!.Complete(succeeded: false);
        }
    }

    [Fact]
    public void Successful_authentication_clears_previous_failed_attempts()
    {
        var throttle = CreateThrottle();
        for (var index = 0; index < 4; index++)
        {
            Assert.True(throttle.TryBegin("operator@harborops.test", out var failedLease, out _));
            failedLease!.Complete(succeeded: false);
        }

        Assert.True(throttle.TryBegin("operator@harborops.test", out var successLease, out _));
        successLease!.Complete(succeeded: true);

        Assert.True(throttle.TryBegin("operator@harborops.test", out var nextLease, out _));
        nextLease!.Complete(succeeded: false);
    }

    private static LoginAttemptThrottle CreateThrottle() => new(
        Options.Create(new LoginThrottlingOptions
        {
            AccountFailedAttemptLimit = 5,
            AccountFailureWindow = TimeSpan.FromMinutes(5),
        }),
        TimeProvider.System);
}
