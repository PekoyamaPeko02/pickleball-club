using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;

namespace PickleballClub.Api.Features.Auth;

public class AdminBootstrapOptions
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string DisplayName { get; set; } = "Club Admin";
}

/// <summary>
/// Creates the first Admin account from configuration (<c>Admin:Email</c> / <c>Admin:Password</c>) when that email has no
/// account yet. There is deliberately no Admin in the SQL seed: a default password in a seed file ends up in production.
/// An existing account is never changed, so the password in configuration only matters the first time.
/// </summary>
public sealed class AdminBootstrap(IServiceProvider services, IOptions<AdminBootstrapOptions> options, ILogger<AdminBootstrap> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Email) || string.IsNullOrWhiteSpace(o.Password)) return;
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var email = AuthEndpoints.NormalizeEmail(o.Email);
            if (await db.Users.AnyAsync(u => u.Email == email, ct)) return;

            db.Users.Add(new User
            {
                Id = Guid.NewGuid(), Email = email, PasswordHash = Passwords.Hash(o.Password),
                DisplayName = o.DisplayName, Role = Roles.Admin, EmailVerifiedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            log.LogInformation("Created the first Admin account {Email}", email);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The database may not be up yet; the API still starts and /health reports it.
            log.LogError(e, "Could not create the first Admin account");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
