using System;
using System.Collections.Generic;
using System.Linq;

namespace Challenge.Caching
{
    /// <summary>
    /// Evicts the oldest elements while the cache holds more than the configured number of elements (MAX_ELEMENTS).
    /// </summary>
    public sealed class SizeBasedEvictionStrategy : ICacheEvictionStrategy
    {
        public SizeBasedEvictionStrategy(int maxElements)
        {
            if (maxElements <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxElements), maxElements, "The maximum number of elements must be positive.");
            }

            MaxElements = maxElements;
        }

        public int MaxElements { get; }

        public IEnumerable<TKey> SelectKeysToEvict<TKey>(IReadOnlyCollection<CacheEntryInfo<TKey>> entries, DateTime utcNow)
        {
            // As the cache is cleaned up on every update, this normally removes exactly one (the oldest) element.
            var excess = entries.Count - MaxElements;
            if (excess <= 0)
            {
                return Enumerable.Empty<TKey>();
            }

            return entries
                .OrderBy(entry => entry.CreatedAtUtc)
                .ThenBy(entry => entry.SequenceNumber)
                .Take(excess)
                .Select(entry => entry.Key);
        }
    }
}
