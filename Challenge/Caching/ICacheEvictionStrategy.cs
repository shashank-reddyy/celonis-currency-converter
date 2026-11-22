using System;
using System.Collections.Generic;

namespace Challenge.Caching
{
    /// <summary>
    /// Decides which elements to remove when the cache is cleaned up (strategy pattern).
    /// </summary>
    public interface ICacheEvictionStrategy
    {
        IEnumerable<TKey> SelectKeysToEvict<TKey>(IReadOnlyCollection<CacheEntryInfo<TKey>> entries, DateTime utcNow);
    }

    public sealed class CacheEntryInfo<TKey>
    {
        public CacheEntryInfo(TKey key, DateTime createdAtUtc, long sequenceNumber)
        {
            Key = key;
            CreatedAtUtc = createdAtUtc;
            SequenceNumber = sequenceNumber;
        }

        public TKey Key { get; }

        public DateTime CreatedAtUtc { get; }

        public long SequenceNumber { get; }
    }
}
