namespace TradingBot.Infrastructure.AI
{
    /// <summary>
    /// Configuration section "AI" in appsettings.json / user-secrets.
    /// Keys should be stored in user-secrets, not appsettings.json.
    ///
    /// Setup via CLI:
    ///   dotnet user-secrets set "AI:GeminiApiKey"     "your-key"
    ///   dotnet user-secrets set "AI:GroqApiKey"       "your-key"
    ///   dotnet user-secrets set "AI:OpenRouterApiKey" "your-key"
    ///   dotnet user-secrets set "AI:CohereApiKey"     "your-key"
    /// </summary>
    public class AiOptions
    {
        // ── API Keys (from user-secrets) ─────────────────────────────────

        public string GeminiApiKey { get; set; } = string.Empty;
        public string GroqApiKey { get; set; } = string.Empty;
        public string OpenRouterApiKey { get; set; } = string.Empty;
        public string CohereApiKey { get; set; } = string.Empty;

        // ── Model names (overridable in appsettings.json) ────────────────

        /// <summary>Free Gemini model. gemini-2.0-flash has the most generous free quota.</summary>
        public string GeminiModel { get; set; } = "gemini-2.0-flash";

        /// <summary>Groq free-tier model. llama-3.1-70b is the strongest free option.</summary>
        public string GroqModel { get; set; } = "llama-3.1-70b-versatile";

        /// <summary>OpenRouter free model (must end with :free to use free quota).</summary>
        public string OpenRouterModel { get; set; } = "meta-llama/llama-3.1-8b-instruct:free";

        /// <summary>Cohere free-tier model.</summary>
        public string CohereModel { get; set; } = "command-r";

        // ── Behaviour ────────────────────────────────────────────────────

        /// <summary>
        /// How long (seconds) to back off from a provider after a 429 rate-limit error
        /// before trying it again. Default 90 seconds (Gemini resets in 60 s, others vary).
        /// </summary>
        public int RateLimitBackoffSeconds { get; set; } = 90;

        /// <summary>Minimum AI confidence required to open a trade. Default 70.</summary>
        public int MinConfidenceToTrade { get; set; } = 70;

        /// <summary>
        /// Ordered list of providers to try. First in list is primary.
        /// Override in appsettings.json to change priority.
        /// </summary>
        public List<string> ProviderPriority { get; set; } = new()
        {
            "Gemini", "Groq", "OpenRouter", "Cohere"
        };
    }
}