using System;
using System.Configuration;

namespace Challenge.Caching
{
    public enum CacheEvictionStrategyType
    {
        TimeBased,
        SizeBased,
    }

    /// <summary>
    /// Validated cache configuration, read from the application settings (App.config).
    /// </summary>
    public sealed class CacheSettings
    {
        private CacheSettings(CacheEvictionStrategyType strategy, int maxAgeMinutes, int maxElements)
        {
            Strategy = strategy;
            MaxAgeMinutes = maxAgeMinutes;
            MaxElements = maxElements;
        }

        public CacheEvictionStrategyType Strategy { get; }

        public int MaxAgeMinutes { get; }

        public int MaxElements { get; }

        public static CacheSettings FromApplicationSettings()
        {
            var settings = Properties.Settings.Default;
            return Create(settings.CacheEvictionStrategy, settings.CacheMaxAgeMinutes, settings.CacheMaxElements);
        }

        public static CacheSettings Create(string strategy, int maxAgeMinutes, int maxElements)
        {
            if (!Enum.TryParse(strategy, ignoreCase: true, result: out CacheEvictionStrategyType strategyType)
                || !Enum.IsDefined(typeof(CacheEvictionStrategyType), strategyType))
            {
                throw new ConfigurationErrorsException(
                    $"Invalid cache eviction strategy '{strategy}'. Supported values: {string.Join(", ", Enum.GetNames(typeof(CacheEvictionStrategyType)))}.");
            }

            if (strategyType == CacheEvictionStrategyType.TimeBased && maxAgeMinutes <= 0)
            {
                throw new ConfigurationErrorsException($"CacheMaxAgeMinutes must be greater than zero, but was {maxAgeMinutes}.");
            }

            if (strategyType == CacheEvictionStrategyType.SizeBased && maxElements <= 0)
            {
                throw new ConfigurationErrorsException($"CacheMaxElements must be greater than zero, but was {maxElements}.");
            }

            return new CacheSettings(strategyType, maxAgeMinutes, maxElements);
        }

        public ICacheEvictionStrategy CreateEvictionStrategy()
        {
            switch (Strategy)
            {
                case CacheEvictionStrategyType.TimeBased:
                    return new TimeBasedEvictionStrategy(TimeSpan.FromMinutes(MaxAgeMinutes));
                case CacheEvictionStrategyType.SizeBased:
                    return new SizeBasedEvictionStrategy(MaxElements);
                default:
                    throw new InvalidOperationException($"Unsupported cache eviction strategy {Strategy}.");
            }
        }
    }
}
