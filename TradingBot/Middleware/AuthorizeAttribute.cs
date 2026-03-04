using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TradingBot.Middleware
{
    /// <summary>
    /// Authorization attribute. Apply to controllers/actions that require API key auth.
    /// 
    /// Example:
    ///   [Authorize]
    ///   [HttpPost("trade/open")]
    ///   public async Task<IActionResult> OpenTrade(...) { }
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AuthorizeAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.HttpContext.User?.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedObjectResult(new
                {
                    error = "Unauthorized",
                    message = "API key required. Use header: Authorization: ApiKey {key}"
                });
            }

            await Task.CompletedTask;
        }
    }
}