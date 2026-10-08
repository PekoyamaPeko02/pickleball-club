using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using PickleballClub.Api.Data;
using PickleballClub.Api.Features.Auth;
using PickleballClub.Api.Services;

namespace PickleballClub.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against a per-run test database with a controllable clock.
/// Shared across test classes through <see cref="ApiCollection"/>; isolate tests by using distinct Courts and days.
/// The clock can only move forward, so tests that advance it must not depend on the absolute time afterwards.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private TestDatabase _db = null!;

    /// <summary>Starts at real "now" (DB defaults use now()); tests move it with <c>Advance</c>.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>Exceptions the API logged at Error level (i.e. behind a 500). Include them in assertion messages.</summary>
    public ConcurrentQueue<string> ServerErrors { get; } = new();

    /// <summary>Every email the API "sent" during the run, oldest first.</summary>
    public ConcurrentQueue<EmailMessage> Emails { get; } = new();

    /// <summary>The OAuth client ID the API is configured with; <see cref="FakeGoogle"/> stands in for Google.</summary>
    public const string GoogleClientId = "test-google-client-id";

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _db.ConnectionString);
        builder.UseSetting("Auth:GoogleClientId", GoogleClientId);
        // Every test signs in from the same (null) client IP: keep the per-IP limiter out of the way.
        builder.UseSetting("RateLimit:Auth:PermitLimit", "1000000");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(new ErrorCapture(ServerErrors));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(new CapturingEmailSender(Emails));
            services.RemoveAll<IGoogleTokenVerifier>();
            services.AddSingleton<IGoogleTokenVerifier, FakeGoogle>();
            // Tests drive Hold expiry explicitly via BookingService.ExpireHoldsAsync instead of a 30 s timer.
            foreach (var d in services.Where(d => d.ImplementationType == typeof(PickleballClub.Api.Jobs.BookingJobs)).ToList())
                services.Remove(d);
            // Tokens are minted with fake-clock timestamps, so validate their lifetime against the same clock —
            // otherwise advancing the clock would make freshly issued tokens "not yet valid" for the real-time validator.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                o.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, p) =>
                {
                    var now = Clock.GetUtcNow().UtcDateTime;
                    return (notBefore is null || notBefore <= now + p.ClockSkew) && (expires is null || expires > now - p.ClockSkew);
                });
        });
    }

    public HttpClient Anonymous() => CreateClient();

    private static int _seq;

    /// <summary>A unique address so tests never share accounts.</summary>
    public static string NewEmail() => $"user{Interlocked.Increment(ref _seq)}-{Guid.NewGuid():N}@example.test";

    public const string Password = "correct horse battery";

    /// <summary>Registers a new Customer through the real endpoint and returns a client carrying the bearer token.</summary>
    public async Task<TestUser> RegisterAsync(string? email = null, string? phone = "0812345678", string displayName = "Test Customer", string password = Password)
    {
        email ??= NewEmail();
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, displayName, phone });
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"register failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return Signed(client, await res.Content.ReadFromJsonAsync<JsonElement>(), email, password);
    }

    /// <summary>Signs in as the Admin account the API creates at startup from configuration (appsettings.Development.json).</summary>
    public async Task<TestUser> AdminAsync()
    {
        var cfg = Services.GetRequiredService<IConfiguration>();
        var (email, password) = (cfg["Admin:Email"]!, cfg["Admin:Password"]!);
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/admin/login", new { email, password });
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"admin login failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return Signed(client, await res.Content.ReadFromJsonAsync<JsonElement>(), email, password);
    }

    public static TestUser Signed(HttpClient client, JsonElement auth, string email, string password)
    {
        var token = auth.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new TestUser(client, auth.GetProperty("user").GetProperty("id").GetGuid(), email, password, token);
    }

    /// <summary>The newest email of that kind sent to the address.</summary>
    public EmailMessage LastEmail(string to, string kind) =>
        Emails.LastOrDefault(e => e.To == to && e.Kind == kind) ?? throw new InvalidOperationException($"no '{kind}' email was sent to {to}");

    /// <summary>The one-time token in an emailed link (…?token=XYZ).</summary>
    public static string TokenIn(EmailMessage email) => Regex.Match(email.TextBody, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;

    private sealed class CapturingEmailSender(ConcurrentQueue<EmailMessage> sink) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            sink.Enqueue(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stands in for Google: a credential of the form <c>google|subject|email|verified</c> (verified = true/false) is "valid"
    /// for <see cref="GoogleClientId"/>; anything else is rejected.
    /// </summary>
    private sealed class FakeGoogle : IGoogleTokenVerifier
    {
        public Task<GoogleIdentity?> VerifyAsync(string credential, string clientId, CancellationToken ct)
        {
            var parts = credential.Split('|');
            GoogleIdentity? who = clientId == GoogleClientId && parts is ["google", var sub, var email, var verified]
                ? new GoogleIdentity(sub, email, verified == "true", "Google Person")
                : null;
            return Task.FromResult(who);
        }
    }

    public static string GoogleCredential(string subject, string email, bool verified = true) => $"google|{subject}|{email}|{(verified ? "true" : "false")}";

    /// <summary>Runs <paramref name="work"/> in a fresh DI scope (DbContext, services, ...).</summary>
    public async Task<T> WithScope<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    public Task WithDb(Func<AppDbContext, Task> work) =>
        WithScope<int>(async sp =>
        {
            await work(sp.GetRequiredService<AppDbContext>());
            return 0;
        });

    /// <summary>Today's date at the Club (Asia/Bangkok in the seed) according to the fake clock.</summary>
    public DateOnly ClubToday() => ClubClock.Today("Asia/Bangkok", Clock.GetUtcNow().UtcDateTime);

    private sealed class ErrorCapture(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Capture(sink, categoryName);
        public void Dispose() { }

        private sealed class Capture(ConcurrentQueue<string> sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            // EF logs every failed SaveChanges at Error even when the service handles it (e.g. 23P01 → 409); skip that noise.
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Error && !category.StartsWith("Microsoft.EntityFrameworkCore");
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // Deliberate 4xx ApiExceptions are also logged by the exception-handler middleware; only genuine 500s matter.
                if (IsEnabled(level) && exception is not ApiException) sink.Enqueue($"[{category}] {formatter(state, exception)}\n{exception}");
            }
        }
    }
}

public sealed record TestUser(HttpClient Http, Guid Id, string Email, string Password, string Token);

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>
/// A second API + database for the tests that change the Club's settings (hours, prices, Courts), so they cannot disturb
/// the tests that rely on the seeded Club. Do not use <see cref="SlotPicker"/> here: its blocks belong to the other database.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SettingsCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "settings";
}
