namespace TradingBot.Domain.Enums
{
    /// <summary>
    /// All supported AI providers, in default priority order.
    /// The MultiProviderAIService rotates through these when rate limits are hit.
    /// </summary>
    public enum AiProviderType
    {
        Gemini = 0,   // Google — 15 req/min free
        Groq = 1,   // Groq   — 30 req/min free, very fast
        OpenRouter = 2,   // OpenRouter — free models with :free suffix
        Cohere = 3    // Cohere — free tier, command-r models
    }
}