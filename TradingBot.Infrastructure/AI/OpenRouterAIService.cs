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
    /// OpenRouter provider — aggregates 200+ models under one API key.
    /// Free models: append ":free" suffix — NO credit card required.
    ///   meta-llama/llama-3.1-8b-instruct:free
    ///   mistralai/mistral-7b-instruct:free
    ///   google/gemma-2-9b-it:free
    /// Docs: https://openrouter.ai/docs
    /// </summary>
    public class OpenRouterAIService : IAISignalService
    {
        private readonly HttpClient _http;
        private readonly AiOptions _opts;
        private readonly ILogger<OpenRouterAIService> _logger;

        private const string BaseUrl = "https://openrouter.ai/api/v1/chat/completions";

        public OpenRouterAIService(HttpClient http, IOptions<AiOptions> opts, ILogger<OpenRouterAIService> logger)
        {
            _http = http;
            _opts = opts.Value;
            _logger = logger;
        }

        public async Task<AISignalResult> ValidateSignalAsync(
            IndicatorSnapshot snapshot, string recentTradeContext)
        {
            var raw = await CallOpenRouterAsync(BuildSignalPrompt(snapshot, recentTradeContext));
            return ParseSignalResponse(raw);
        }

        public async Task<string> AnalyzeMarketRegimeAsync(string symbol, IndicatorSnapshot snapshot)
        {
            var raw = await CallOpenRouterAsync(BuildRegimePrompt(symbol, snapshot));
            return ExtractRegime(raw);
        }

        private async Task<string> CallOpenRouterAsync(string userPrompt)
        {
            if (string.IsNullOrWhiteSpace(_opts.OpenRouterApiKey))
                throw new InvalidOperationException("OpenRouter API key not configured.");

            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.OpenRouterApiKey);
            request.Headers.Add("HTTP-Referer", "https://github.com/Mukarram990/TradingBot");
            request.Headers.Add("X-Title", "TradingBot");

            var body = JsonSerializer.Serialize(new
            {
                model = _opts.OpenRouterModel,
                messages = new[]
                {
                    new { role = "system",
                          content = "You are a professional crypto trading analyst. Respond with valid JSON only." },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.2,
                max_tokens = 512
            });

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                response.StatusCode == HttpStatusCode.PaymentRequired)
                throw new AiRateLimitException("OpenRouter rate limit / free quota exceeded.");

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";
        }

        private static string BuildSignalPrompt(IndicatorSnapshot s, string ctx)
            => string.Format(
                "Evaluate crypto BUY signal for {0}. " +
                "RSI={1:F2}, EMA20={2:F4}, EMA50={3:F4}, MACD={4:F6}, " +
                "ATR={5:F4}, VolumeSpike={6}, Trend={7}, " +
                "Support={8:F4}, Resistance={9:F4}. Context: {10}. " +
                "Respond JSON only: {{\"action\":\"BUY or HOLD or SELL\",\"confidence\":0-100,\"reasoning\":\"brief\",\"riskLevel\":\"LOW or MEDIUM or HIGH\"}}",
                s.Symbol, s.RSI, s.EMA20, s.EMA50, s.MACD,
                s.ATR, s.VolumeSpike, s.Trend,
                s.SupportLevel, s.ResistanceLevel, ctx);

        private static string BuildRegimePrompt(string symbol, IndicatorSnapshot s)
            => string.Format(
                "Market regime for {0}: RSI={1:F1}, Trend={2}, ATR={3:F4}. " +
                "JSON: {{\"regime\":\"Trending or Ranging or Volatile or Bearish\"}}",
                symbol, s.RSI, s.Trend, s.ATR);

        private AISignalResult ParseSignalResponse(string raw)
        {
            try
            {
                var clean = raw.Trim();
                // Strip markdown fences if model ignored instructions
                if (clean.StartsWith("```"))
                {
                    var lines = clean.Split('\n');
                    clean = string.Join('\n', lines.Skip(1).Take(lines.Length - 2)).Trim();
                }

                var doc = JsonDocument.Parse(clean);
                return new AISignalResult
                {
                    Action = doc.RootElement.GetProperty("action").GetString() ?? "HOLD",
                    Confidence = doc.RootElement.GetProperty("confidence").GetInt32(),
                    Reasoning = doc.RootElement.GetProperty("reasoning").GetString() ?? "",
                    RiskLevel = doc.RootElement.GetProperty("riskLevel").GetString() ?? "MEDIUM",
                    Provider = AiProviderType.OpenRouter,
                    ModelUsed = _opts.OpenRouterModel
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenRouter: failed to parse response: {Raw}", raw);
                return new AISignalResult
                {
                    Action = "HOLD",
                    Confidence = 0,
                    Provider = AiProviderType.OpenRouter,
                    ModelUsed = _opts.OpenRouterModel
                };
            }
        }

        private static string ExtractRegime(string raw)
        {
            try
            {
                var clean = raw.Trim();
                if (clean.StartsWith("```"))
                {
                    var lines = clean.Split('\n');
                    clean = string.Join('\n', lines.Skip(1).Take(lines.Length - 2)).Trim();
                }
                return JsonDocument.Parse(clean).RootElement.GetProperty("regime").GetString() ?? "Ranging";
            }
            catch { return "Ranging"; }
        }
    }
}