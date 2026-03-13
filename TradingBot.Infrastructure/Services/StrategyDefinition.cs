using System.Text.Json.Serialization;

namespace TradingBot.Infrastructure.Services
{
    public class StrategyDefinition
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "ema_crossover";

        [JsonPropertyName("weight")]
        public decimal Weight { get; set; } = 1.0m;

        [JsonPropertyName("fastEma")]
        public int FastEma { get; set; } = 20;

        [JsonPropertyName("slowEma")]
        public int SlowEma { get; set; } = 50;

        [JsonPropertyName("useRsi")]
        public bool UseRsi { get; set; } = true;

        [JsonPropertyName("rsiMin")]
        public decimal RsiMin { get; set; } = 45m;

        [JsonPropertyName("rsiMax")]
        public decimal RsiMax { get; set; } = 70m;

        [JsonPropertyName("useMacd")]
        public bool UseMacd { get; set; } = true;

        [JsonPropertyName("macdMin")]
        public decimal MacdMin { get; set; } = 0m;

        [JsonPropertyName("useAtr")]
        public bool UseAtr { get; set; } = false;

        [JsonPropertyName("atrMin")]
        public decimal AtrMin { get; set; } = 0m;

        [JsonPropertyName("requireVolumeSpike")]
        public bool RequireVolumeSpike { get; set; } = false;

        [JsonPropertyName("minConfidence")]
        public int MinConfidence { get; set; } = 70;
    }
}
