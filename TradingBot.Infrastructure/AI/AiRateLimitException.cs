namespace TradingBot.Infrastructure.AI
{
    /// <summary>
    /// Thrown by a single provider when it receives HTTP 429 / rate-limit response.
    /// MultiProviderAIService catches this and rotates to the next provider.
    /// </summary>
    public class AiRateLimitException : Exception
    {
        public AiRateLimitException(string message) : base(message) { }
    }

    /// <summary>
    /// Thrown by MultiProviderAIService when every configured provider is
    /// currently rate-limited or has an invalid API key.
    /// The SignalGenerationWorker catches this and skips the current tick.
    /// </summary>
    public class AiAllProvidersExhaustedException : Exception
    {
        public AiAllProvidersExhaustedException(string message) : base(message) { }
    }
}