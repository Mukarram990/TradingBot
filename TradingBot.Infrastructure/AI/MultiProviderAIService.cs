using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Domain.Models;

namespace TradingBot.Infrastructure.AI
{
    /// <summary>
    /// Orchestrates all configured AI providers with intelligent rotation.
    ///
    /// ROTATION STRATEGY:
    /// 1. Try providers in priority order (default: Gemini → Groq → OpenRouter → Cohere)
    /// 2. On HTTP 429 / AiRateLimitException: mark that provider as "cooling down"
    ///    and immediately try the next one
    /// 3. Backoff tracking is in-memory (resets on app restart, which is fine —
    ///    rate-limit windows are typically 60-90 seconds)
    /// 4. If ALL providers are exhausted: throw AiAllProvidersExhaustedException
    ///    so the SignalGenerationWorker can skip the tick gracefully
    ///
    /// PROVIDER PRIORITY (configurable via AiOptions.ProviderPriority):
    ///   Gemini (15 req/min) → Groq (30 req/min) → OpenRouter (free tier) → Cohere (1k/month)
    ///
    /// This class is registered as the primary IAISignalService in DI.
    /// </summary>
    public class MultiProviderAIService : IAISignalService
    {
        private readonly Dictionary<AiProviderType, IAISignalService> _providers;
        private readonly AiOptions _opts;
        private readonly ILogger<MultiProviderAIService> _logger;

        // In-memory rate-limit cooldown tracker: provider → when it can be retried
        private readonly Dictionary<AiProviderType, DateTime> _cooldowns = new();
        private readonly object _cooldownLock = new();

        public MultiProviderAIService(
            GeminiAIService gemini,
            GroqAIService groq,
            OpenRouterAIService openRouter,
            CohereAIService cohere,
            IOptions<AiOptions> opts,
            ILogger<MultiProviderAIService> logger)
        {
            _opts = opts.Value;
            _logger = logger;

            // Map enum to concrete service
            _providers = new()
            {
                [AiProviderType.Gemini] = gemini,
                [AiProviderType.Groq] = groq,
                [AiProviderType.OpenRouter] = openRouter,
                [AiProviderType.Cohere] = cohere,
            };
        }

        // ── IAISignalService ─────────────────────────────────────────────

        public async Task<AISignalResult> ValidateSignalAsync(
            IndicatorSnapshot snapshot,
            string recentTradeContext)
        {
            return await ExecuteWithFallback(
                provider => provider.ValidateSignalAsync(snapshot, recentTradeContext),
                "ValidateSignal");
        }

        public async Task<string> AnalyzeMarketRegimeAsync(
            string symbol, IndicatorSnapshot snapshot)
        {
            var result = await ExecuteWithFallback(
                provider => provider.AnalyzeMarketRegimeAsync(symbol, snapshot)
                    .ContinueWith(t => new AISignalResult { Reasoning = t.Result }),
                "AnalyzeRegime",
                returnStringMode: true,
                fallbackString: "Ranging");

            return result.Reasoning;
        }

        // ── Core rotation engine ─────────────────────────────────────────

        private async Task<AISignalResult> ExecuteWithFallback(
            Func<IAISignalService, Task<AISignalResult>> operation,
            string operationName,
            bool returnStringMode = false,
            string fallbackString = "")
        {
            var priorityOrder = BuildPriorityList();
            var exhausted = new List<string>();

            foreach (var providerType in priorityOrder)
            {
                if (IsOnCooldown(providerType))
                {
                    var remaining = GetCooldownRemaining(providerType);
                    _logger.LogDebug("AI [{Provider}] on cooldown for {Sec}s — skipping.",
                        providerType, remaining);
                    exhausted.Add($"{providerType}(cooling {remaining}s)");
                    continue;
                }

                if (!_providers.TryGetValue(providerType, out var service))
                    continue;

                try
                {
                    _logger.LogDebug("AI [{Op}] trying provider {Provider}.", operationName, providerType);

                    AISignalResult result;

                    if (returnStringMode)
                    {
                        // For regime analysis, call the regime method and wrap result
                        var regime = await service.AnalyzeMarketRegimeAsync("", null!);
                        result = new AISignalResult { Reasoning = regime };
                    }
                    else
                    {
                        result = await operation(service);
                    }

                    _logger.LogInformation(
                        "AI [{Op}] success via {Provider} ({Model}) — action={Action} confidence={Conf}",
                        operationName, providerType, result.ModelUsed, result.Action, result.Confidence);

                    return result;
                }
                catch (AiRateLimitException ex)
                {
                    _logger.LogWarning(
                        "AI [{Provider}] rate-limited: {Msg}. Cooling down {Sec}s, trying next provider.",
                        providerType, ex.Message, _opts.RateLimitBackoffSeconds);

                    SetCooldown(providerType);
                    exhausted.Add($"{providerType}(429)");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
                {
                    _logger.LogDebug("AI [{Provider}] skipped — API key not configured.", providerType);
                    exhausted.Add($"{providerType}(no-key)");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI [{Provider}] unexpected error during {Op}.",
                        providerType, operationName);
                    exhausted.Add($"{providerType}(error)");
                }
            }

            throw new AiAllProvidersExhaustedException(
                $"All AI providers exhausted during {operationName}: [{string.Join(", ", exhausted)}]. " +
                "Will retry on next tick.");
        }

        // ── Priority list builder ────────────────────────────────────────

        private List<AiProviderType> BuildPriorityList()
        {
            var ordered = new List<AiProviderType>();

            foreach (var name in _opts.ProviderPriority)
            {
                if (Enum.TryParse<AiProviderType>(name, ignoreCase: true, out var pt))
                    ordered.Add(pt);
            }

            // Append any not in the explicit priority list so nothing is lost
            foreach (AiProviderType pt in Enum.GetValues<AiProviderType>())
                if (!ordered.Contains(pt)) ordered.Add(pt);

            return ordered;
        }

        // ── Cooldown helpers ─────────────────────────────────────────────

        private bool IsOnCooldown(AiProviderType provider)
        {
            lock (_cooldownLock)
            {
                return _cooldowns.TryGetValue(provider, out var until)
                    && DateTime.UtcNow < until;
            }
        }

        private int GetCooldownRemaining(AiProviderType provider)
        {
            lock (_cooldownLock)
            {
                if (_cooldowns.TryGetValue(provider, out var until))
                    return Math.Max(0, (int)(until - DateTime.UtcNow).TotalSeconds);
                return 0;
            }
        }

        private void SetCooldown(AiProviderType provider)
        {
            lock (_cooldownLock)
            {
                _cooldowns[provider] = DateTime.UtcNow.AddSeconds(_opts.RateLimitBackoffSeconds);
            }
        }

        /// <summary>Returns current status of all providers (for /api/ai/status endpoint).</summary>
        public Dictionary<string, object> GetProviderStatus()
        {
            var status = new Dictionary<string, object>();
            foreach (AiProviderType pt in Enum.GetValues<AiProviderType>())
            {
                status[pt.ToString()] = new
                {
                    available = !IsOnCooldown(pt),
                    cooldownSeconds = GetCooldownRemaining(pt)
                };
            }
            return status;
        }
    }
}