using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Auth;

public record RegisterRequest(
    [property: Required, EmailAddress, MaxLength(254)] string Email,
    [property: Required, MinLength(Passwords.MinLength), MaxLength(128), MaxUtf8Bytes(72)] string Password,
    [property: NotEmpty, MaxLength(80)] string DisplayName,
    [property: MaxLength(30)] string? Phone);

public record LoginRequest(
    [property: Required, MaxLength(254)] string Email,
    [property: Required, MaxLength(128), MaxUtf8Bytes(72)] string Password);

public record GoogleLoginRequest([property: Required, MaxLength(4096)] string Credential);

public record UpdateProfileRequest(
    [property: NotEmpty, MaxLength(80)] string DisplayName,
    [property: NotEmpty, MaxLength(30)] string Phone);

/// <param name="GoogleClientId">Null when "Sign in with Google" is switched off.</param>
public record AuthConfigDto(string? GoogleClientId);

public static class AuthEndpoints
{
    private const int MaxFailedLogins = 5;
    private const int LockMinutes = 15;
    /// <summary>Verified when the account is unknown/ineligible so both paths cost the same bcrypt work.</summary>
    private static readonly string DummyHash = Passwords.Hash(Guid.NewGuid().ToString());

    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth").WithTags("Auth");

        g.MapGet("/config", (IOptions<AuthOptions> auth) =>
            new AuthConfigDto(string.IsNullOrWhiteSpace(auth.Value.GoogleClientId) ? null : auth.Value.GoogleClientId));

        // A new Customer. Signed in straight away; the email address is confirmed later through the emailed link.
        g.MapPost("/register", async (RegisterRequest req, AppDbContext db, IOptions<JwtOptions> jwt, IOptions<AuthOptions> auth,
            IOptions<AppOptions> app, IEmailSender email, TimeProvider clock, CancellationToken ct) =>
        {
            var address = NormalizeEmail(req.Email);
            var phone = string.IsNullOrWhiteSpace(req.Phone) ? null
                : NormalizePhone(req.Phone) ?? throw new ApiException(400, "bad_phone", "That phone number does not look right.");
            if (await db.Users.AnyAsync(u => u.Email == address, ct)) throw EmailTaken();

            var now = clock.GetUtcNow().UtcDateTime;
            var user = new User
            {
                Id = Guid.NewGuid(), Email = address, PasswordHash = Passwords.Hash(req.Password),
                DisplayName = req.DisplayName.Trim(), Phone = phone, Role = Roles.Customer,
            };
            db.Users.Add(user);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw EmailTaken(); // two registrations for the same address at once
            }

            await PasswordEndpoints.SendVerificationAsync(db, user, auth.Value, app.Value, email, now, ct);
            return Results.Json(Sessions.Issue(user, jwt.Value, now), statusCode: StatusCodes.Status201Created);
        }).RequireRateLimiting(ApiErrors.AuthLimit).WithValidation();

        g.MapPost("/login", (LoginRequest req, AppDbContext db, IOptions<JwtOptions> jwt, TimeProvider clock, CancellationToken ct) =>
            PasswordLoginAsync(req, Roles.Customer, db, jwt.Value, clock, ct))
            .RequireRateLimiting(ApiErrors.AuthLimit).WithValidation();

        // Back office: same mechanics and lockout, but only Admin accounts; Customer accounts cannot use it.
        g.MapPost("/admin/login", (LoginRequest req, AppDbContext db, IOptions<JwtOptions> jwt, TimeProvider clock, CancellationToken ct) =>
            PasswordLoginAsync(req, Roles.Admin, db, jwt.Value, clock, ct))
            .RequireRateLimiting(ApiErrors.AuthLimit).WithValidation();

        // "Sign in with Google": the browser sends the ID token Google gave it. Creates the Customer on first use,
        // or links Google to an existing Customer with the same (Google-verified) email.
        g.MapPost("/google", async (GoogleLoginRequest req, AppDbContext db, IGoogleTokenVerifier google, IOptions<AuthOptions> auth,
            IOptions<JwtOptions> jwt, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(auth.Value.GoogleClientId))
                throw new ApiException(400, "google_not_configured", "Sign in with Google is not available yet.");
            var who = await google.VerifyAsync(req.Credential, auth.Value.GoogleClientId, ct)
                      ?? throw new ApiException(400, "google_token_invalid", "Google sign-in failed. Please try again.");
            if (!who.EmailVerified)
                throw new ApiException(400, "google_email_unverified", "Your Google account's email address is not verified.");

            var now = clock.GetUtcNow().UtcDateTime;
            var address = NormalizeEmail(who.Email);
            var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == who.Subject, ct)
                       ?? await db.Users.FirstOrDefaultAsync(u => u.Email == address, ct);
            if (user is null)
            {
                user = new User { Id = Guid.NewGuid(), Email = address, GoogleSubject = who.Subject, DisplayName = who.Name?.Trim(), Role = Roles.Customer, EmailVerifiedAt = now };
                db.Users.Add(user);
            }
            else
            {
                // The back office signs in with a password only.
                if (user.Role != Roles.Customer) throw new ApiException(400, "google_token_invalid", "Google sign-in failed. Please try again.");
                if (user.GoogleSubject is null && user.EmailVerifiedAt is null && user.PasswordHash is not null)
                {
                    // Nobody ever proved they own this address, so the password on file may be a stranger's who registered
                    // it first. Google has now vouched for this person: drop that password and every session issued under it
                    // (the owner can set their own through "Forgot password").
                    user.PasswordHash = null;
                    user.PasswordChangedAt = now;
                    user.FailedLogins = 0;
                    user.LockedUntil = null;
                }
                user.GoogleSubject ??= who.Subject;
                user.EmailVerifiedAt ??= now;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(Sessions.Issue(user, jwt.Value, now));
        }).RequireRateLimiting(ApiErrors.AuthLimit).WithValidation();

        g.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == principal.UserId(), ct);
            return user is null ? Results.NotFound() : Results.Ok(Sessions.ToDto(user));
        }).RequireAuthorization();

        // Name and phone: a Customer needs both before the first Booking.
        g.MapPut("/me", async (UpdateProfileRequest req, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var phone = NormalizePhone(req.Phone) ?? throw new ApiException(400, "bad_phone", "That phone number does not look right.");
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == principal.UserId(), ct);
            if (user is null) return Results.NotFound();
            user.DisplayName = req.DisplayName.Trim();
            user.Phone = phone;
            await db.SaveChangesAsync(ct);
            return Results.Ok(Sessions.ToDto(user));
        }).RequireAuthorization().WithValidation();

        g.MapPasswords();

        return api;
    }

    private static async Task<IResult> PasswordLoginAsync(
        LoginRequest req, string role, AppDbContext db, JwtOptions jwt, TimeProvider clock, CancellationToken ct)
    {
        var address = NormalizeEmail(req.Email);
        var now = clock.GetUtcNow().UtcDateTime;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address, ct);

        var eligible = user is { PasswordHash: not null } && user.Role == role;
        var locked = user?.LockedUntil > now;
        // Always run one bcrypt verify (against a dummy hash if need be) so timing doesn't reveal whether the email exists.
        var passwordOk = BCrypt.Net.BCrypt.Verify(req.Password, eligible ? user!.PasswordHash! : DummyHash);

        if (!eligible || locked || !passwordOk)
        {
            if (eligible && !locked) await RegisterFailedPasswordAsync(db, user!.Id, now, ct);
            // 400, not 401: the client signs the user out on any 401 (expired session), which would loop on the sign-in form.
            throw new ApiException(400, "invalid_credentials", "The email or password is not right (or the account is locked for a few minutes).");
        }

        if (user!.FailedLogins != 0 || user.LockedUntil is not null)
        {
            user.FailedLogins = 0;
            user.LockedUntil = null;
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(Sessions.Issue(user, jwt, now));
    }

    /// <summary>Atomic, so parallel guesses can't slip past the limit: the 5th consecutive failure locks the account.</summary>
    internal static Task RegisterFailedPasswordAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var lockUntil = now.AddMinutes(LockMinutes);
        return db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.LockedUntil, u => u.FailedLogins + 1 >= MaxFailedLogins ? lockUntil : u.LockedUntil)
            .SetProperty(u => u.FailedLogins, u => u.FailedLogins + 1 >= MaxFailedLogins ? 0 : u.FailedLogins + 1), ct);
    }

    private static ApiException EmailTaken() => new(409, "email_taken", "An account with this email already exists. Sign in instead.");

    /// <summary>Emails are stored and looked up trimmed and lower-cased.</summary>
    public static string NormalizeEmail(string raw) => raw.Trim().ToLowerInvariant();

    /// <summary>Keeps digits and a leading +; 8–15 digits, so Thai and foreign numbers both pass. Null when it is not a phone number.</summary>
    public static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Any(c => !(c is >= '0' and <= '9' or '+' or ' ' or '-' or '(' or ')' or '.'))) return null;
        var digits = new string(trimmed.Where(c => c is >= '0' and <= '9').ToArray());
        if (digits.Length is < 8 or > 15 || trimmed.LastIndexOf('+') > 0) return null;
        return (trimmed[0] == '+' ? "+" : "") + digits;
    }
}

public static class Passwords
{
    public const int MinLength = 10;

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 10);
}
