using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
namespace TradingBot.Infrastructure
{
    // INSTALL: dotnet add TradingBot package Microsoft.Extensions.Http.Resilience
    // See PHASE4_PLAN.md for full Program.cs integration snippet.
    public static class ResilienceExtensions
    {
        // Binance: 3 retries exp backoff + circuit breaker. Never retry 400/401/403.
        public static IHttpClientBuilder AddBinanceResilience(this IHttpClientBuilder b)
        {
            b.AddResilienceHandler("binance", p =>
            {
                p.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(1),
                    UseJitter = true,
                    ShouldHandle = args =>
                    {
                        var code = args.Outcome.Result?.StatusCode;
                        if (code is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                            return ValueTask.FromResult(false);
                        return ValueTask.FromResult(args.Outcome.Exception != null || code is (System.Net.HttpStatusCode)429 or System.Net.HttpStatusCode.InternalServerError or System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout);
                    }
                });
                p.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 5,
                    BreakDuration = TimeSpan.FromSeconds(15)
                });
            });

            return b;
        }
        // AI providers: 2 retries on network errors only (429 rotated by MultiProviderAIService).
        public static IHttpClientBuilder AddAiProviderResilience(this IHttpClientBuilder b)
        {
            b.AddResilienceHandler("ai-provider", p =>
                p.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(500),
                    UseJitter = true,
                    ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception != null)
                }));

            return b;
        }
    }
}