using System.Diagnostics;

namespace NetThrottle.Core.RateLimiting;

/// <summary>
/// Classic token-bucket rate limiter. Tokens represent bytes. Refills
/// continuously at <see cref="RatePerSec"/> up to a burst capacity.
///
/// Thread-safe. Designed to be called from the packet pump: ask
/// <see cref="TryConsume"/> for the packet length; if it returns false the
/// caller should delay or queue the packet until tokens are available.
/// </summary>
public sealed class TokenBucket
{
    private readonly object _gate = new();
    private double _tokens;
    private long _lastTicks;

    public TokenBucket(long ratePerSec, double burstSeconds = 0.25)
    {
        RatePerSec = ratePerSec;
        BurstSeconds = burstSeconds;
        _tokens = Capacity;
        _lastTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>Refill rate in tokens (bytes) per second.</summary>
    public long RatePerSec { get; private set; }

    /// <summary>How many seconds worth of traffic can burst at once.</summary>
    public double BurstSeconds { get; private set; }

    /// <summary>Maximum tokens the bucket can hold.</summary>
    public double Capacity => Math.Max(RatePerSec * BurstSeconds, 1);

    public void UpdateRate(long ratePerSec)
    {
        lock (_gate)
        {
            Refill();
            RatePerSec = ratePerSec;
            if (_tokens > Capacity) _tokens = Capacity;
        }
    }

    /// <summary>
    /// Try to consume <paramref name="bytes"/> tokens. Returns true and debits
    /// the bucket when enough tokens exist; otherwise returns false and reports
    /// how long until the request could be satisfied.
    /// </summary>
    public bool TryConsume(long bytes, out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            Refill();
            if (_tokens >= bytes)
            {
                _tokens -= bytes;
                retryAfter = TimeSpan.Zero;
                return true;
            }

            double deficit = bytes - _tokens;
            double seconds = RatePerSec > 0 ? deficit / RatePerSec : double.MaxValue;
            retryAfter = TimeSpan.FromSeconds(Math.Min(seconds, 1.0));
            return false;
        }
    }

    /// <summary>
    /// Admit <paramref name="bytes"/> unconditionally, letting the bucket go
    /// negative, and return how long the caller should delay before releasing
    /// the packet so the average rate is honored. This models a shaping queue:
    /// packets are never dropped, only spread out in time.
    /// </summary>
    public TimeSpan Reserve(long bytes)
    {
        lock (_gate)
        {
            Refill();
            _tokens -= bytes;
            if (_tokens >= 0 || RatePerSec <= 0) return TimeSpan.Zero;
            return TimeSpan.FromSeconds(-_tokens / RatePerSec);
        }
    }

    private void Refill()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;
        _tokens = Math.Min(Capacity, _tokens + elapsed * RatePerSec);
    }
}
