using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;

namespace PickleballClub.Api.Features.Auth;

public class JwtOptions
{
    public const string DevSigningKey = "dev-only-signing-key-change-me-0123456789";

    public string Issuer { get; set; } = "pickleball-club";
    public string Audience { get; set; } = "pickleball-club-apps";
    /// <summary>At least 32 chars. Override via env Jwt__SigningKey in every non-dev environment (the API refuses to start otherwise).</summary>
    public string SigningKey { get; set; } = DevSigningKey;
    public int AccessTokenDays { get; set; } = 7;
}

public class AuthOptions
{
    /// <summary>OAuth client ID for "Sign in with Google". Empty = Google sign-in is switched off.</summary>
    public string GoogleClientId { get; set; } = "";
    public int ResetTokenHours { get; set; } = 2;
    public int VerifyTokenHours { get; set; } = 48;
}

public record UserDto(Guid Id, string Email, string? DisplayName, string? Phone, string Role, bool EmailVerified, bool HasPassword, bool ProfileComplete);

public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);

/// <summary>Signed-in sessions are JWTs; one-time email links are random tokens stored hashed.</summary>
public static class Sessions
{
    public const string StampClaim = "pwd";

    /// <summary>A Customer may book only with a name and a phone number on file.</summary>
    public static bool ProfileComplete(User u) => !string.IsNullOrWhiteSpace(u.DisplayName) && !string.IsNullOrWhiteSpace(u.Phone);

    public static UserDto ToDto(User u) =>
        new(u.Id, u.Email, u.DisplayName, u.Phone, u.Role, u.EmailVerifiedAt is not null, u.PasswordHash is not null, ProfileComplete(u));

    /// <summary>
    /// Changes with every password change/reset (unix ms of <c>password_changed_at</c>, "0" if never). It rides in the JWT so a new
    /// password signs out every older session.
    /// </summary>
    public static string Stamp(DateTime? passwordChangedAt) =>
        passwordChangedAt is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeMilliseconds().ToString() : "0";

    public static AuthResponse Issue(User user, JwtOptions o, DateTime nowUtc)
    {
        var expires = nowUtc.AddDays(o.AccessTokenDays);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(StampClaim, Stamp(user.PasswordChangedAt)),
        };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, nowUtc, expires, creds);
        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, ToDto(user));
    }

    public static Guid UserId(this ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue(JwtRegisteredClaimNames.Sub)
                   ?? throw new UnauthorizedAccessException());

    // ---------------------------------------------------------------- one-time email links

    /// <summary>Creates a fresh single-use token for the user (voiding older ones of the same purpose). Returns the plaintext once; only its hash is kept.</summary>
    public static async Task<string> IssueTokenAsync(AppDbContext db, Guid userId, string purpose, TimeSpan lifetime, DateTime now, CancellationToken ct)
    {
        await db.UserTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), ct);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.UserTokens.Add(new UserToken { Id = Guid.NewGuid(), UserId = userId, Purpose = purpose, TokenHash = HashToken(token), ExpiresAt = now + lifetime });
        await db.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>Uses the token up and returns its user, or null when it is unknown, expired or already used. Atomic: of two concurrent uses only one wins.</summary>
    public static async Task<Guid?> ConsumeTokenAsync(AppDbContext db, string token, string purpose, DateTime now, CancellationToken ct)
    {
        var hash = HashToken(token.Trim());
        var row = await db.UserTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose && t.ConsumedAt == null && t.ExpiresAt > now, ct);
        if (row is null) return null;
        var consumed = await db.UserTokens.Where(t => t.Id == row.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), ct);
        return consumed == 0 ? null : row.UserId;
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
