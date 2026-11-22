using System;
using System.Collections.Generic;
using System.Linq;

namespace Challenge.Caching
{
    /// <summary>
    /// Thread-safe in-memory cache that is cleaned up by an <see cref="ICacheEvictionStrategy"/> on every update.
    /// </summary>
    /// <remarks>
    /// A single lock guards all state. Eviction needs a consistent view of all entries (e.g. to find the oldest one),
    /// which a ConcurrentDictionary alone cannot provide atomically, and the critical sections are very short.
    /// </remarks>
    public sealed class QueryCache<TKey, TValue>
    {
        private readonly object _syncRoot = new object();
        private readonly Dictionary<TKey, Entry> _entries = new Dictionary<TKey, Entry>();
        private readonly ICacheEvictionStrategy _evictionStrategy;
        private long _sequenceNumber;

        public QueryCache(ICacheEvictionStrategy evictionStrategy)
        {
            _evictionStrategy = evictionStrategy ?? throw new ArgumentNullException(nameof(evictionStrategy));
        }

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _entries.Count;
                }
            }
        }

        public bool TryGet(TKey key, out TValue value)
        {
            lock (_syncRoot)
            {
                if (_entries.TryGetValue(key, out var entry))
                {
                    value = entry.Value;
                    return true;
                }
            }

            value = default(TValue);
            return false;
        }

        /// <summary>
        /// Adds or replaces the element for <paramref name="key"/> and cleans up the cache afterwards.
        /// </summary>
        public void Set(TKey key, TValue value)
        {
            lock (_syncRoot)
            {
                _entries[key] = new Entry(value, DateTime.UtcNow, ++_sequenceNumber);
                EvictEntries();
            }
        }

        // Must be called while holding _syncRoot.
        private void EvictEntries()
        {
            var entryInfos = _entries
                .Select(pair => new CacheEntryInfo<TKey>(pair.Key, pair.Value.CreatedAtUtc, pair.Value.SequenceNumber))
                .ToList();

            // Materialize before removing, as the strategy may lazily enumerate the snapshot.
            var keysToEvict = _evictionStrategy.SelectKeysToEvict(entryInfos, DateTime.UtcNow).ToList();
            foreach (var key in keysToEvict)
            {
                _entries.Remove(key);
            }
        }

        private sealed class Entry
        {
            public Entry(TValue value, DateTime createdAtUtc, long sequenceNumber)
            {
                Value = value;
                CreatedAtUtc = createdAtUtc;
                SequenceNumber = sequenceNumber;
            }

            public TValue Value { get; }

            public DateTime CreatedAtUtc { get; }

            public long SequenceNumber { get; }
        }
    }
}
