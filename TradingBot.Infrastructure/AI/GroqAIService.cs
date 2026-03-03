using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Domain.Models;

namespace TradingBot.Infrastructure.AI
{
    /// <summary>
    /// Groq AI provider — OpenAI-compatible, ultra-fast free inference.
    /// Free tier: 30 requests/min, 14,400 req/day.
    /// Best free model: llama-3.1-70b-versatile (strong reasoning).
    /// Docs: https://console.groq.com/docs/openai
    /// </summary>
    public class GroqAIService : IAISignalService
    {
        private readonly HttpClient _http;
        private readonly AiOptions _opts;
        private readonly ILogger<GroqAIService> _logger;

        private const string BaseUrl = "https://api.groq.com/openai/v1/chat/completions";

        public GroqAIService(HttpClient http, IOptions<AiOptions> opts, ILogger<GroqAIService> logger)
        {
            _http = http;
            _opts = opts.Value;
            _logger = logger;
        }

        public async Task<AISignalResult> ValidateSignalAsync(
            IndicatorSnapshot snapshot, string recentTradeContext)
        {
            var raw = await CallOpenAICompatAsync(
                BuildSignalPrompt(snapshot, recentTradeContext));
            return ParseSignalResponse(raw);
        }

        public async Task<string> AnalyzeMarketRegimeAsync(string symbol, IndicatorSnapshot snapshot)
        {
            var raw = await CallOpenAICompatAsync(BuildRegimePrompt(symbol, snapshot));
            return ExtractRegime(raw);
        }

        private async Task<string> CallOpenAICompatAsync(string userPrompt)
        {
            if (string.IsNullOrWhiteSpace(_opts.GroqApiKey))
                throw new InvalidOperationException("Groq API key not configured.");

            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.GroqApiKey);

            var body = JsonSerializer.Serialize(new
            {
                model = _opts.GroqModel,
                messages = new[]
                {
                    new { role = "system",
                          content = "You are a professional crypto trading analyst. Always respond with valid JSON only, no markdown, no extra text." },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.2,
                max_tokens = 512,
                response_format = new { type = "json_object" }
            });

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new AiRateLimitException("Groq rate limit hit.");

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";
        }

        private static string BuildSignalPrompt(IndicatorSnapshot s, string tradeContext) => $$"""
            Evaluate this crypto BUY signal for {s.Symbol}.
            RSI={s.RSI:F2}, EMA20={s.EMA20:F4}, EMA50={s.EMA50:F4}, MACD={s.MACD:F6},
            ATR={s.ATR:F4}, VolumeSpike={s.VolumeSpike}, Trend={s.Trend},
            Support={s.SupportLevel:F4}, Resistance={s.ResistanceLevel:F4}.
            Recent trades: {tradeContext}
            Respond with JSON: {"action":"BUY"|"HOLD"|"SELL","confidence":0-100,"reasoning":"<sentence>","riskLevel":"LOW"|"MEDIUM"|"HIGH"}
            """;

        private static string BuildRegimePrompt(string symbol, IndicatorSnapshot s) => $$"""
            Classify market regime for {symbol}. RSI={s.RSI:F1}, Trend={s.Trend}, MACD={s.MACD:F6}, ATR={s.ATR:F4}.
            Respond: {"regime":"Trending"|"Ranging"|"Volatile"|"Bearish"}
            """;

        private AISignalResult ParseSignalResponse(string raw)
        {
            try
            {
                var doc = JsonDocument.Parse(raw.Trim());
                return new AISignalResult
                {
                    Action = doc.RootElement.GetProperty("action").GetString() ?? "HOLD",
                    Confidence = doc.RootElement.GetProperty("confidence").GetInt32(),
                    Reasoning = doc.RootElement.GetProperty("reasoning").GetString() ?? "",
                    RiskLevel = doc.RootElement.GetProperty("riskLevel").GetString() ?? "MEDIUM",
                    Provider = AiProviderType.Groq,
                    ModelUsed = _opts.GroqModel
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Groq: failed to parse: {Raw}", raw);
                return new AISignalResult { Action = "HOLD", Confidence = 0, Provider = AiProviderType.Groq, ModelUsed = _opts.GroqModel };
            }
        }

        private static string ExtractRegime(string raw)
        {
            try { return JsonDocument.Parse(raw).RootElement.GetProperty("regime").GetString() ?? "Ranging"; }
            catch { return "Ranging"; }
        }
    }
}