using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TradingBot.Infrastructure.Binance.Models;

namespace TradingBot.Infrastructure.Binance
{
    public class BinanceAccountService
    {
        private readonly HttpClient _httpClient;
        private readonly BinanceOptions _options;
        private readonly BinanceSignatureService _signatureService;

        public BinanceAccountService(HttpClient httpClient, IOptions<BinanceOptions> options)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _httpClient.BaseAddress = new Uri(_options.BaseUrl);

            _signatureService = new BinanceSignatureService(_options.ApiSecret);
        }

        public async Task<BinanceAccountResponse> GetAccountInfoAsync()
        {
            // Get server time (prevents timestamp error)
            var timeResponse = await _httpClient.GetAsync("/api/v3/time");
            timeResponse.EnsureSuccessStatusCode();

            var timeJson = await timeResponse.Content.ReadAsStringAsync();
            var timeDoc = JsonDocument.Parse(timeJson);
            var serverTime = timeDoc.RootElement.GetProperty("serverTime").GetInt64();

            var query = $"timestamp={serverTime}&recvWindow=5000";
            var signature = _signatureService.Sign(query);

            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v3/account?{query}&signature={signature}");

            request.Headers.Add("X-MBX-APIKEY", _options.ApiKey);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Account request failed: {error}");
            }

            var json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<BinanceAccountResponse>(json)!;
        }
        public async Task<decimal> GetAssetBalanceAsync(string asset)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = $"timestamp={timestamp}";
            var signature = _signatureService.Sign(query);

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/v3/account?{query}&signature={signature}");

            request.Headers.Add("X-MBX-APIKEY", _options.ApiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(json);

            var balances = result.GetProperty("balances");

            foreach (var balance in balances.EnumerateArray())
            {
                if (balance.GetProperty("asset").GetString() == asset)
                {
                    return decimal.Parse(
                        balance.GetProperty("free").GetString()!,
                        System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            return 0m;
        }

    }

}
