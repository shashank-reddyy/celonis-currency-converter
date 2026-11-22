using System;

namespace Challenge.Services
{
    /// <summary>
    /// Raised when exchange rate data could not be retrieved. The message is meant to be shown to the user.
    /// </summary>
    public class ExchangeRateServiceException : Exception
    {
        public ExchangeRateServiceException(string message)
            : base(message)
        {
        }

        public ExchangeRateServiceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
