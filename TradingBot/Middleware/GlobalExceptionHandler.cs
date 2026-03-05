using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json;
using TradingBot.Domain.Entities;
using TradingBot.Persistence;

namespace TradingBot.API.Middleware
{
    /// <summary>
    /// Global exception handler. Register with:
    ///   app.UseExceptionHandler(GlobalExceptionHandler.Handle);
    ///
    /// Returns consistent JSON error envelope instead of raw stack traces.
    /// Persists every unhandled exception to the SystemLogs table so it appears
    /// in GET /api/system/logs/errors.
    ///
    /// Domain exceptions (InvalidOperationException, plain Exception thrown by
    /// our services) return HTTP 400 with the original message.
    /// All other exceptions return HTTP 500 with a safe generic message.
    /// </summary>
    public static class GlobalExceptionHandler
    {
        public static void Handle(IApplicationBuilder errorApp)
        {
            errorApp.Run(async context =>
            {
                var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                if (ex == null) return;

                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "Unhandled exception [{Method} {Path}]",
                    context.Request.Method, context.Request.Path);

                await TryPersistAsync(context, ex);

                bool isDomain = ex is InvalidOperationException
                             || ex is ArgumentException;

                context.Response.StatusCode = isDomain ? 400 : 500;
                context.Response.ContentType = "application/json";

                var payload = JsonSerializer.Serialize(new
                {
                    error = isDomain ? "Bad Request" : "Internal Server Error",
                    message = isDomain
                        ? ex.Message
                        : "An unexpected error occurred. See server logs for details.",
                    requestId = context.TraceIdentifier,
                    timestamp = DateTime.UtcNow
                });

                await context.Response.WriteAsync(payload);
            });
        }

        // Best-effort DB log — swallow any secondary exception so we never mask the primary.
        private static async Task TryPersistAsync(HttpContext ctx, Exception ex)
        {
            try
            {
                using var scope = ctx.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();
                var stack = ex.StackTrace is { Length: > 4000 } s ? s[..4000] : ex.StackTrace;

                db.SystemLogs!.Add(new SystemLog
                {
                    Level = "ERROR",
                    Message = $"[{ctx.Request.Method} {ctx.Request.Path}] {ex.Message}",
                    StackTrace = stack
                });
                await db.SaveChangesAsync();
            }
            catch { /* swallow */ }
        }
    }
}