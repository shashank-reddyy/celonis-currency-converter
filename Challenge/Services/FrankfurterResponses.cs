using System.Collections.Generic;
using Newtonsoft.Json;

namespace Challenge.Services
{
    // DTOs matching the JSON returned by the Frankfurter API (https://frankfurter.dev).

    /// <summary>Response of <c>GET /v1/latest?base=USD&amp;symbols=EUR</c>.</summary>
    internal sealed class LatestRatesResponse
    {
        [JsonProperty("base")]
        public string Base { get; set; }

        [JsonProperty("date")]
        public string Date { get; set; }

        [JsonProperty("rates")]
        public Dictionary<string, decimal> Rates { get; set; }
    }

    /// <summary>Response of <c>GET /v1/2024-01-01..2024-01-31?base=USD&amp;symbols=EUR</c>.</summary>
    internal sealed class TimeSeriesResponse
    {
        [JsonProperty("base")]
        public string Base { get; set; }

        [JsonProperty("start_date")]
        public string StartDate { get; set; }

        [JsonProperty("end_date")]
        public string EndDate { get; set; }

        /// <summary>Date (yyyy-MM-dd) -> (currency code -> rate).</summary>
        [JsonProperty("rates")]
        public Dictionary<string, Dictionary<string, decimal>> Rates { get; set; }
    }
}
