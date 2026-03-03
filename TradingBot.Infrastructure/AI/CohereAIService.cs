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
    /// Cohere AI provider — free trial key gives generous quota (1,000 API calls/month free).
    /// Best free model: command-r (strong reasoning, instruction-following).
    /// API: https://docs.cohere.com/reference/chat
    /// Get key: https://dashboard.cohere.com/api-keys
    /// </summary>
    public class CohereAIService : IAISignalService
    {
        private readonly HttpClient _http;
        private readonly AiOptions _opts;
        private readonly ILogger<CohereAIService> _logger;

        private const string BaseUrl = "https://api.cohere.com/v2/chat";

        public CohereAIService(HttpClient http, IOptions<AiOptions> opts, ILogger<CohereAIService> logger)
        {
            _http = http;
            _opts = opts.Value;
            _logger = logger;
        }

        public async Task<AISignalResult> ValidateSignalAsync(
            IndicatorSnapshot snapshot, string recentTradeContext)
        {
            var raw = await CallCohereAsync(BuildSignalPrompt(snapshot, recentTradeContext));
            return ParseSignalResponse(raw);
        }

        public async Task<string> AnalyzeMarketRegimeAsync(string symbol, IndicatorSnapshot snapshot)
        {
            var raw = await CallCohereAsync(BuildRegimePrompt(symbol, snapshot));
            return ExtractRegime(raw);
        }

        private async Task<string> CallCohereAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(_opts.CohereApiKey))
                throw new InvalidOperationException("Cohere API key not configured.");

            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.CohereApiKey);
            request.Headers.Add("Accept", "application/json");

            // Cohere v2 Chat API format
            var body = JsonSerializer.Serialize(new
            {
                model = _opts.CohereModel,
                messages = new[]
                {
                    new { role = "system",
                          content = "You are a professional crypto trading analyst. Always respond with valid JSON only, no explanation outside the JSON object." },
                    new { role = "user", content = userMessage }
                },
                temperature = 0.2,
                max_tokens = 512
            });

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new AiRateLimitException("Cohere rate limit hit.");

            // 402 = trial quota exceeded
            if (response.StatusCode == HttpStatusCode.PaymentRequired)
                throw new AiRateLimitException("Cohere free quota exhausted.");

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            // Cohere v2: message.content[0].text
            return doc.RootElement
                .GetProperty("message")
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "{}";
        }

        private static string BuildSignalPrompt(IndicatorSnapshot s, string ctx)
            => string.Format(
                "Evaluate crypto BUY signal for {0}. " +
                "RSI={1:F2}, EMA20={2:F4}, EMA50={3:F4}, MACD={4:F6}, " +
                "ATR={5:F4}, VolumeSpike={6}, Trend={7}, " +
                "Support={8:F4}, Resistance={9:F4}. Recent trades: {10}. " +
                "Respond ONLY with JSON: " +
                "{{\"action\":\"BUY\",\"confidence\":75,\"reasoning\":\"brief reason\",\"riskLevel\":\"LOW\"}}",
                s.Symbol, s.RSI, s.EMA20, s.EMA50, s.MACD,
                s.ATR, s.VolumeSpike, s.Trend,
                s.SupportLevel, s.ResistanceLevel, ctx);

        private static string BuildRegimePrompt(string symbol, IndicatorSnapshot s)
            => string.Format(
                "Classify market regime for {0}: RSI={1:F1}, Trend={2}, ATR={3:F4}. " +
                "JSON only: {{\"regime\":\"Trending\"}}",
                symbol, s.RSI, s.Trend, s.ATR);

        private AISignalResult ParseSignalResponse(string raw)
        {
            try
            {
                var clean = raw.Trim();
                if (clean.StartsWith("```"))
                {
                    var lines = clean.Split('\n');
                    clean = string.Join('\n', lines.Skip(1).Take(lines.Length - 2)).Trim();
                }

                // Find JSON object boundary (Cohere occasionally prepends text despite instructions)
                var start = clean.IndexOf('{');
                var end = clean.LastIndexOf('}');
                if (start >= 0 && end > start)
                    clean = clean[start..(end + 1)];

                var doc = JsonDocument.Parse(clean);
                return new AISignalResult
                {
                    Action = doc.RootElement.GetProperty("action").GetString() ?? "HOLD",
                    Confidence = doc.RootElement.GetProperty("confidence").GetInt32(),
                    Reasoning = doc.RootElement.GetProperty("reasoning").GetString() ?? "",
                    RiskLevel = doc.RootElement.GetProperty("riskLevel").GetString() ?? "MEDIUM",
                    Provider = AiProviderType.Cohere,
                    ModelUsed = _opts.CohereModel
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cohere: failed to parse: {Raw}", raw);
                return new AISignalResult
                {
                    Action = "HOLD",
                    Confidence = 0,
                    Provider = AiProviderType.Cohere,
                    ModelUsed = _opts.CohereModel
                };
            }
        }

        private static string ExtractRegime(string raw)
        {
            try
            {
                var clean = raw.Trim();
                var start = clean.IndexOf('{');
                var end = clean.LastIndexOf('}');
                if (start >= 0 && end > start) clean = clean[start..(end + 1)];
                return JsonDocument.Parse(clean).RootElement.GetProperty("regime").GetString() ?? "Ranging";
            }
            catch { return "Ranging"; }
        }
    }
}