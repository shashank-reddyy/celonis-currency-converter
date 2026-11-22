using System;
using System.Collections.Generic;
using System.Linq;

namespace Challenge.Caching
{
    /// <summary>
    /// Evicts every element that is older than the configured maximum age (MAX_AGE).
    /// </summary>
    public sealed class TimeBasedEvictionStrategy : ICacheEvictionStrategy
    {
        public TimeBasedEvictionStrategy(TimeSpan maxAge)
        {
            if (maxAge <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAge), maxAge, "The maximum age must be positive.");
            }

            MaxAge = maxAge;
        }

        public TimeSpan MaxAge { get; }

        public IEnumerable<TKey> SelectKeysToEvict<TKey>(IReadOnlyCollection<CacheEntryInfo<TKey>> entries, DateTime utcNow)
        {
            return entries
                .Where(entry => utcNow - entry.CreatedAtUtc > MaxAge)
                .Select(entry => entry.Key);
        }
    }
}
