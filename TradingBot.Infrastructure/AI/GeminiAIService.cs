using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using System.Net;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Domain.Models;

namespace TradingBot.Infrastructure.AI
{
    /// <summary>
    /// Google Gemini AI provider.
    /// Free tier: 15 requests/min on gemini-2.0-flash.
    /// Docs: https://ai.google.dev/gemini-api/docs
    /// </summary>
    public class GeminiAIService : IAISignalService
    {
        private readonly HttpClient _http;
        private readonly AiOptions _opts;
        private readonly ILogger<GeminiAIService> _logger;

        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

        public GeminiAIService(
            HttpClient http,
            IOptions<AiOptions> opts,
            ILogger<GeminiAIService> logger)
        {
            _http = http;
            _opts = opts.Value;
            _logger = logger;
        }

        public async Task<AISignalResult> ValidateSignalAsync(
            IndicatorSnapshot snapshot,
            string recentTradeContext)
        {
            var prompt = BuildSignalPrompt(snapshot, recentTradeContext);
            var raw = await CallGeminiAsync(prompt);
            return ParseSignalResponse(raw);
        }

        public async Task<string> AnalyzeMarketRegimeAsync(string symbol, IndicatorSnapshot snapshot)
        {
            var prompt = BuildRegimePrompt(symbol, snapshot);
            var raw = await CallGeminiAsync(prompt);
            return ExtractRegime(raw);
        }

        // ── Core HTTP call ───────────────────────────────────────────────

        private async Task<string> CallGeminiAsync(string prompt)
        {
            if (string.IsNullOrWhiteSpace(_opts.GeminiApiKey))
                throw new InvalidOperationException("Gemini API key not configured.");

            var url = $"{BaseUrl}/models/{_opts.GeminiModel}:generateContent?key={_opts.GeminiApiKey}";
            var body = JsonSerializer.Serialize(new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                generationConfig = new
                {
                    temperature = 0.2,   // low temperature = more deterministic trading decisions
                    maxOutputTokens = 512,
                    responseMimeType = "application/json"
                }
            });

            var response = await _http.PostAsync(url,
                new StringContent(body, Encoding.UTF8, "application/json"));

            if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                response.StatusCode == (HttpStatusCode)429)
                throw new AiRateLimitException("Gemini rate limit hit.");

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            // Gemini response: candidates[0].content.parts[0].text
            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "{}";
        }

        // ── Prompt builders ──────────────────────────────────────────────

        private static string BuildSignalPrompt(IndicatorSnapshot s, string tradeContext)
            => $$"""
                You are an expert crypto trading analyst. Evaluate this BUY signal for {s.Symbol}.

                TECHNICAL INDICATORS:
                - RSI: {s.RSI:F2} (oversold <30, overbought >70)
                - EMA20: {s.EMA20:F4} | EMA50: {s.EMA50:F4} (trend: EMA20>EMA50 = bullish)
                - MACD: {s.MACD:F6} (positive = bullish momentum)
                - ATR: {s.ATR:F4} (volatility measure)
                - Volume Spike: {s.VolumeSpike}
                - Trend: {s.Trend}
                - Support Level: {s.SupportLevel:F4}
                - Resistance Level: {s.ResistanceLevel:F4}

                RECENT TRADE HISTORY FOR THIS SYMBOL:
                {tradeContext}

                Respond ONLY with valid JSON (no markdown, no explanation outside JSON):
                {
                  "action": "BUY" | "HOLD" | "SELL",
                  "confidence": <integer 0-100>,
                  "reasoning": "<one sentence>",
                  "riskLevel": "LOW" | "MEDIUM" | "HIGH"
                }
                """;

        private static string BuildRegimePrompt(string symbol, IndicatorSnapshot s)
            => $$"""
                Classify the current market regime for {symbol} based on these indicators:
                RSI={s.RSI:F1}, EMA20={s.EMA20:F4}, EMA50={s.EMA50:F4},
                MACD={s.MACD:F6}, ATR={s.ATR:F4}, Trend={s.Trend}

                Respond ONLY with valid JSON:
                { "regime": "Trending" | "Ranging" | "Volatile" | "Bearish" }
                """;

        // ── Parsers ──────────────────────────────────────────────────────

        private AISignalResult ParseSignalResponse(string raw)
        {
            try
            {
                var clean = raw.Trim().TrimStart('`').TrimEnd('`');
                if (clean.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                    clean = clean[4..].Trim();

                var doc = JsonDocument.Parse(clean);
                return new AISignalResult
                {
                    Action = doc.RootElement.GetProperty("action").GetString() ?? "HOLD",
                    Confidence = doc.RootElement.GetProperty("confidence").GetInt32(),
                    Reasoning = doc.RootElement.GetProperty("reasoning").GetString() ?? "",
                    RiskLevel = doc.RootElement.GetProperty("riskLevel").GetString() ?? "MEDIUM",
                    Provider = AiProviderType.Gemini,
                    ModelUsed = _opts.GeminiModel
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini: failed to parse signal response: {Raw}", raw);
                return new AISignalResult { Action = "HOLD", Confidence = 0, Provider = AiProviderType.Gemini, ModelUsed = _opts.GeminiModel };
            }
        }

        private static string ExtractRegime(string raw)
        {
            try
            {
                var doc = JsonDocument.Parse(raw.Trim());
                return doc.RootElement.GetProperty("regime").GetString() ?? "Ranging";
            }
            catch { return "Ranging"; }
        }
    }
}