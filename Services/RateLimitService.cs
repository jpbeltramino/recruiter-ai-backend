using System.Collections.Concurrent;

namespace RecruiterAI.Services;

public class RateLimitService(IConfiguration config, ILogger<RateLimitService> logger)
{
    private readonly ILogger<RateLimitService> _logger = logger;
    private readonly int _dailyLimit = config.GetValue<int>("RateLimit:DailyLimit", 50);

    // Diccionario thread-safe para tracking de uso por token
    private readonly ConcurrentDictionary<string, UsageEntry> _usage = new();

    /// <summary>
    /// Verifica si el token puede hacer otra request. Si puede, incrementa el contador.
    /// Devuelve true si está permitido, false si superó el límite.
    /// </summary>
    public RateLimitResult CheckAndIncrement(string token)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        var entry = _usage.AddOrUpdate(
            token,
            
            _ => new UsageEntry { Date = today, Count = 1 },
            
            (_, existing) =>
            {
                if (existing.Date < today)
                    return new UsageEntry { Date = today, Count = 1 };
                    
                existing.Count++;
                return existing;
            }
        );

        if (entry.Count > _dailyLimit)
        {
            _logger.LogWarning("Rate limit exceeded for token {TokenPrefix}... Count: {Count}",
                token[..Math.Min(8, token.Length)], entry.Count);

            return new RateLimitResult(
                Allowed: false,
                Used: entry.Count - 1,
                Limit: _dailyLimit,
                ResetAt: today.AddDays(1)
            );
        }

        return new RateLimitResult(
            Allowed: true,
            Used: entry.Count,
            Limit: _dailyLimit,
            ResetAt: today.AddDays(1)
        );
    }

    public RateLimitResult GetStatus(string token)
    {
        var today = DateTime.UtcNow.Date;
        if (_usage.TryGetValue(token, out var entry) && entry.Date == today)
        {
            return new RateLimitResult(
                Allowed: entry.Count < _dailyLimit,
                Used: entry.Count,
                Limit: _dailyLimit,
                ResetAt: today.AddDays(1)
            );
        }

        return new RateLimitResult(
            Allowed: true,
            Used: 0,
            Limit: _dailyLimit,
            ResetAt: today.AddDays(1)
        );
    }

    private class UsageEntry
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }
}

public record RateLimitResult(
    bool Allowed,
    int Used,
    int Limit,
    DateTime ResetAt
);