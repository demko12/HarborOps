using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace HarborOps.Modules.Identity.Application;

/// <summary>
/// Keeps short-lived, normalized email attempt state in memory. It deliberately
/// contains neither passwords nor account look-up results.
/// </summary>
public sealed class LoginAttemptThrottle(IOptions<LoginThrottlingOptions> options, TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, AttemptState> attempts = new(StringComparer.Ordinal);

    public bool TryBegin(string? email, out LoginAttemptLease? lease, out TimeSpan retryAfter)
    {
        lease = null;
        retryAfter = TimeSpan.Zero;
        var key = NormalizeEmail(email);
        if (key is null)
        {
            return true;
        }

        var state = attempts.GetOrAdd(key, static _ => new AttemptState());
        var now = timeProvider.GetUtcNow();
        var settings = options.Value;

        lock (state.Gate)
        {
            RemoveExpiredFailures(state, now, settings.AccountFailureWindow);
            if (state.FailureTimes.Count + state.PendingAttempts >= settings.AccountFailedAttemptLimit)
            {
                var earliest = state.FailureTimes.Count > 0 ? state.FailureTimes.Peek() : now;
                retryAfter = earliest.Add(settings.AccountFailureWindow) - now;
                return false;
            }

            state.PendingAttempts++;
            lease = new LoginAttemptLease(this, key, state);
            return true;
        }
    }

    private void Complete(string key, AttemptState state, bool succeeded)
    {
        var now = timeProvider.GetUtcNow();
        var settings = options.Value;
        lock (state.Gate)
        {
            state.PendingAttempts = Math.Max(0, state.PendingAttempts - 1);
            RemoveExpiredFailures(state, now, settings.AccountFailureWindow);
            if (succeeded)
            {
                state.FailureTimes.Clear();
            }
            else
            {
                state.FailureTimes.Enqueue(now);
            }

            if (state.PendingAttempts == 0 && state.FailureTimes.Count == 0)
            {
                attempts.TryRemove(new KeyValuePair<string, AttemptState>(key, state));
            }
        }
    }

    private static void RemoveExpiredFailures(AttemptState state, DateTimeOffset now, TimeSpan window)
    {
        while (state.FailureTimes.TryPeek(out var failedAt) && failedAt.Add(window) <= now)
        {
            state.FailureTimes.Dequeue();
        }
    }

    internal static string? NormalizeEmail(string? email)
    {
        var normalized = email?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    internal sealed class AttemptState
    {
        internal object Gate { get; } = new();
        internal Queue<DateTimeOffset> FailureTimes { get; } = [];
        internal int PendingAttempts { get; set; }
    }

    public sealed class LoginAttemptLease : IDisposable
    {
        private readonly LoginAttemptThrottle owner;
        private readonly string key;
        private readonly AttemptState state;
        private int completed;

        internal LoginAttemptLease(LoginAttemptThrottle owner, string key, AttemptState state)
        {
            this.owner = owner;
            this.key = key;
            this.state = state;
        }

        public void Complete(bool succeeded)
        {
            if (Interlocked.Exchange(ref completed, 1) == 0)
            {
                owner.Complete(key, state, succeeded);
            }
        }

        public void Dispose() => Complete(succeeded: false);
    }
}
