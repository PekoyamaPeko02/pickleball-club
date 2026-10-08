using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace PickleballClub.Api.Services;

/// <summary>A domain rule refused the request. Answered as <c>{ code, message }</c> with <see cref="Status"/>.</summary>
public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>Every error the API returns is JSON <c>{ code, message }</c>. Codes are stable; messages are English.</summary>
public static class ApiErrors
{
    public static (string Code, string Message) For(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ("bad_request", "The request is not valid."),
        StatusCodes.Status401Unauthorized => ("unauthorized", "Please sign in."),
        StatusCodes.Status403Forbidden => ("forbidden", "You do not have access to this."),
        StatusCodes.Status404NotFound => ("not_found", "Not found."),
        StatusCodes.Status405MethodNotAllowed => ("method_not_allowed", "This method is not supported here."),
        StatusCodes.Status409Conflict => ("conflict", "This cannot be done in the current state."),
        StatusCodes.Status415UnsupportedMediaType => ("unsupported_media_type", "Send the request body as application/json."),
        StatusCodes.Status429TooManyRequests => ("rate_limited", "Too many requests. Please try again later."),
        >= 500 => ("server_error", "Something went wrong. Please try again."),
        _ => ("error", "Something went wrong."),
    };

    /// <summary>Fills in the JSON body for bodiless 4xx/5xx responses (Results.NotFound(), auth challenges, 405 …).</summary>
    public static Task WriteStatusPageAsync(StatusCodeContext ctx)
    {
        var (code, message) = For(ctx.HttpContext.Response.StatusCode);
        return ctx.HttpContext.Response.WriteAsJsonAsync(new { code, message });
    }

    /// <summary>Name of the rate-limit policy for sign-in, registration and password endpoints.</summary>
    public const string AuthLimit = "auth";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Sign-in and password endpoints are the brute-force target: limit per client IP (on top of the per-account lockout).
            // Options are read per request so deployments/tests can override them through configuration.
            o.AddPolicy(AuthLimit, http =>
            {
                var opt = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.Auth;
                return RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = opt.PermitLimit,
                        Window = TimeSpan.FromSeconds(opt.WindowSeconds),
                        QueueLimit = 0,
                    });
            });
            o.OnRejected = async (ctx, ct) =>
            {
                var window = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.Auth.WindowSeconds;
                var retryAfter = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra) ? (int)Math.Ceiling(ra.TotalSeconds) : window;
                ctx.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var (code, message) = For(StatusCodes.Status429TooManyRequests);
                await ctx.HttpContext.Response.WriteAsJsonAsync(new { code, message }, ct);
            };
        });
        return services;
    }
}

public class RateLimitOptions
{
    public LimitOptions Auth { get; set; } = new();

    public class LimitOptions
    {
        public int PermitLimit { get; set; } = 20;
        public int WindowSeconds { get; set; } = 60;
    }
}

/// <summary>Turns expected failures (domain rules, malformed requests) into the standard error body; anything else falls through to the 500 handler.</summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        int status;
        string code, message;
        switch (ex)
        {
            case ApiException api:
                (status, code, message) = (api.Status, api.Code, api.Message);
                break;
            case BadHttpRequestException bad:
                status = bad.StatusCode;
                (code, message) = status == StatusCodes.Status415UnsupportedMediaType
                    ? ApiErrors.For(status)
                    : ApiErrors.For(StatusCodes.Status400BadRequest);
                break;
            default:
                return false;
        }

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { code, message }, ct);
        return true;
    }
}
