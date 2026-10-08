using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Features.Admin;
using PickleballClub.Api.Features.Auth;
using PickleballClub.Api.Features.Bookings;
using PickleballClub.Api.Features.Club;
using PickleballClub.Api.Features.Dev;
using PickleballClub.Api.Features.Payments;
using PickleballClub.Api.Jobs;
using PickleballClub.Api.Services;

// Wire formats (yyyy-MM-dd, HH:mm) must not depend on the host locale: a th-TH machine would emit Buddhist-era years.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ---------------------------------------------------------------- options
builder.Services.Configure<JwtOptions>(cfg.GetSection("Jwt"));
builder.Services.Configure<AuthOptions>(cfg.GetSection("Auth"));
builder.Services.Configure<AdminBootstrapOptions>(cfg.GetSection("Admin"));
builder.Services.Configure<AppOptions>(cfg.GetSection("App"));
builder.Services.Configure<RateLimitOptions>(cfg.GetSection("RateLimit"));
builder.Services.Configure<BookingOptions>(cfg.GetSection("Booking"));
builder.Services.Configure<PaymentOptions>(cfg.GetSection("Payments"));
builder.Services.Configure<AdminOptions>(cfg.GetSection("AdminDesk"));
builder.Services.Configure<NotificationOptions>(cfg.GetSection("Notifications"));
builder.Services.Configure<DemoOptions>(cfg.GetSection("Demo"));
var jwt = cfg.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (!builder.Environment.IsDevelopment() && jwt.SigningKey == JwtOptions.DevSigningKey)
    throw new InvalidOperationException("Set Jwt:SigningKey (env Jwt__SigningKey) to a secret of at least 32 characters outside Development.");

// ---------------------------------------------------------------- data
// Read here, not inside the lambda, so a connection string that cannot be understood stops the start with a clear message.
var connectionString = ConnectionStrings.Normalize(cfg.GetConnectionString("Default"));
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention());

// ---------------------------------------------------------------- services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<BookingMailer>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<AdminBookingService>();
var payments = cfg.GetSection("Payments").Get<PaymentOptions>() ?? new PaymentOptions();
var demo = cfg.GetSection("Demo").Get<DemoOptions>() ?? new DemoOptions();
// Payments may be simulated on a developer's machine or on a demo site (Demo:Enabled) — nowhere else.
var simulatedPayments = builder.Environment.IsDevelopment() || demo.Enabled;
if (demo.Enabled && !string.Equals(payments.Provider, MockPaymentProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Demo:Enabled lets anyone mark a Booking as paid, so it only runs with Payments:Provider \"mock\". Turn Demo off to take real payments.");
switch (payments.Provider.ToLowerInvariant())
{
    case MockPaymentProvider.ProviderName:
        if (!simulatedPayments)
            throw new InvalidOperationException("Payments:Provider is \"mock\": no money would be collected. Set it to \"beam\", or set Demo:Enabled for a demo site where nobody pays.");
        builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
        break;
    case BeamPaymentProvider.ProviderName:
        if (string.IsNullOrWhiteSpace(payments.Beam.MerchantId) || string.IsNullOrWhiteSpace(payments.Beam.ApiKey) || string.IsNullOrWhiteSpace(payments.Beam.WebhookHmacKey))
            throw new InvalidOperationException("Payments:Beam needs MerchantId, ApiKey and WebhookHmacKey (env Payments__Beam__MerchantId …).");
        builder.Services.AddHttpClient<IPaymentProvider, BeamPaymentProvider>(c => c.Timeout = TimeSpan.FromSeconds(20));
        break;
    default:
        throw new InvalidOperationException($"Unknown Payments:Provider \"{payments.Provider}\" (use \"mock\" or \"beam\").");
}
builder.Services.AddHostedService<BookingJobs>();
builder.Services.AddSingleton<IEmailSender, DevEmailSender>(); // TODO(provider): a real sender once the Club picks an email provider
builder.Services.AddSingleton<IGoogleTokenVerifier, GoogleTokenVerifier>();
builder.Services.AddHostedService<AdminBootstrap>();

// ---------------------------------------------------------------- auth
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        o.Events = new JwtBearerEvents
        {
            // A password change/reset signs out every older session: the token carries the password stamp it was issued under.
            OnTokenValidated = async ctx =>
            {
                var principal = ctx.Principal!;
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var userId = principal.UserId();
                var u = await db.Users.AsNoTracking().Where(x => x.Id == userId)
                    .Select(x => new { x.PasswordChangedAt })
                    .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);
                if (u is null) { ctx.Fail("unknown user"); return; }
                if ((principal.FindFirstValue(Sessions.StampClaim) ?? "0") != Sessions.Stamp(u.PasswordChangedAt))
                    ctx.Fail("password changed");
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Customer, p => p.RequireRole(Roles.Customer))
    .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin));

// ---------------------------------------------------------------- http
var origins = cfg.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.CustomSchemaIds(t => t.FullName?.Replace('+', '.') ?? t.Name));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApiRateLimiting();

var app = builder.Build();
if (demo.Enabled)
    app.Logger.LogWarning("Demo mode is ON: payments are simulated and anyone can mark a Booking as paid. Never run a real Club like this.");

// CORS first so error responses (500 etc.) still carry CORS headers and the browser can read them.
app.UseCors();

// Expected failures (ApiException, malformed requests) are answered by ApiExceptionHandler; anything else is a 500.
app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    var (code, message) = ApiErrors.For(StatusCodes.Status500InternalServerError);
    await ctx.Response.WriteAsJsonAsync(new { code, message });
}));
// Bodiless 401/403/404/405 … get the same { code, message } JSON.
app.UseStatusCodePages(ApiErrors.WriteStatusPageAsync);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness + database reachability (503 when Postgres is down, so orchestrators stop routing to this instance).
app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(3));
    var up = false;
    try { up = await db.Database.CanConnectAsync(timeout.Token); }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    return up
        ? Results.Ok(new { status = "ok", db = "up" })
        : Results.Json(new { status = "degraded", db = "down" }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

var api = app.MapGroup("/api/v1");
api.MapClub();
api.MapAuth();
api.MapBookings();
api.MapPayments(simulatedPayments);
api.MapAdmin();
api.MapDev(app.Environment);

app.Run();

public partial class Program;

public static class Policies
{
    public const string Customer = "customer";
    public const string Admin = "admin";
}
