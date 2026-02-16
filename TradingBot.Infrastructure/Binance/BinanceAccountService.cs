using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public async Task<string> GetAccountInfoAsync()
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = $"timestamp={timestamp}";
            var signature = _signatureService.Sign(query);

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/v3/account?{query}&signature={signature}");

            request.Headers.Add("X-MBX-APIKEY", _options.ApiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }
    }

}
