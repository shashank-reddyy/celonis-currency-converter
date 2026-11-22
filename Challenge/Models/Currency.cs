using System;

namespace Challenge.Models
{
    public sealed class Currency
    {
        public Currency(string code, string name)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Currency code must not be empty.", nameof(code));
            }

            Code = code;
            Name = name ?? string.Empty;
        }

        public string Code { get; }

        public string Name { get; }

        public string DisplayName => string.IsNullOrEmpty(Name) ? Code : $"{Code} - {Name}";
    }
}
