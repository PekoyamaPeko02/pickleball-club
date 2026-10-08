using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Auth;

public record ChangePasswordRequest(
    [property: Required, MaxLength(128), MaxUtf8Bytes(72)] string CurrentPassword,
    [property: Required, MinLength(Passwords.MinLength), MaxLength(128), MaxUtf8Bytes(72)] string NewPassword);

public record ForgotPasswordRequest([property: Required, MaxLength(254)] string Email);

public record ResetPasswordRequest(
    [property: Required, MaxLength(100)] string Token,
    [property: Required, MinLength(Passwords.MinLength), MaxLength(128), MaxUtf8Bytes(72)] string NewPassword);

public record VerifyEmailRequest([property: Required, MaxLength(100)] string Token);

/// <summary>Password change (signed in), reset by emailed link, and email confirmation. Every password change signs out older sessions.</summary>
public static class PasswordEndpoints
{
    public static void MapPasswords(this RouteGroupBuilder g)
    {
        // The current password is re-checked (and wrong guesses count towards the sign-in lockout) so a stolen session can't take the account over.
        g.MapPost("/password/change", async (ChangePasswordRequest req, ClaimsPrincipal principal, AppDbContext db,
            IOptions<JwtOptions> jwt, TimeProvider clock, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == principal.UserId(), ct);
            if (user?.PasswordHash is null)
                throw new ApiException(400, "no_password", "This account has no password yet. Use “Forgot password” to set one.");
            var now = clock.GetUtcNow().UtcDateTime;
            var locked = user.LockedUntil > now;
            var ok = !locked && BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash);
            if (!ok)
            {
                if (!locked) await AuthEndpoints.RegisterFailedPasswordAsync(db, user.Id, now, ct);
                throw new ApiException(400, "wrong_password", "The current password is not right.");
            }
            if (req.NewPassword == req.CurrentPassword)
                throw new ApiException(400, "same_password", "The new password must be different from the current one.");

            await SetPasswordAsync(db, user, req.NewPassword, now, ct);
            // The old token just died with the old stamp; hand back a fresh one so the caller stays signed in.
            return Results.Ok(Sessions.Issue(user, jwt.Value, now));
        }).RequireAuthorization().RequireRateLimiting(ApiErrors.AuthLimit).WithValidation();

        // Anonymous. Always 204, whether or not the address has an account, so it cannot be used to find out who is registered.
        g.MapPost("/password/forgot", async (ForgotPasswordRequest req, AppDbContext db, IOptions<AuthOptions> auth,
            IOptions<AppOptions> app, IEmailSender email, TimeProvider clock, CancellationToken ct) =>
        {
            var address = AuthEndpoints.NormalizeEmail(req.Email);
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == address, ct);
            if (user is not null)
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var token = await Sessions.IssueTokenAsync(db, user.Id, TokenPurpose.ResetPassword, TimeSpan.FromHours(auth.Value.ResetTokenHours), now, ct);
                var site = user.Role == Roles.Admin ? app.Value.AdminUrl : app.Value.WebUrl;
                await email.SendAsync(EmailTemplates.ResetPassword(user.Email, await ClubNameAsync(db, ct),
                    $"{site.TrimEnd('/')}/reset-password?token={token}"), ct);
            }
            return Results.NoContent();
        }).RequireRateLimiting(ApiErrors.AuthLimit).WithValidation();

        // Anonymous: the token is the credential. Unknown, expired and used tokens all look the same.
        g.MapPost("/password/reset", async (ResetPasswordRequest req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var userId = await Sessions.ConsumeTokenAsync(db, req.Token, TokenPurpose.ResetPassword, now, ct);
            var user = userId is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                throw new ApiException(400, "reset_token_invalid", "This password link is not valid any more. Ask for a new one.");

            user.EmailVerifiedAt ??= now; // using the emailed link proves the address is theirs
            await SetPasswordAsync(db, user, req.NewPassword, now, ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        }).RequireRateLimiting(ApiErrors.AuthLimit).WithValidation();

        g.MapPost("/email/verify", async (VerifyEmailRequest req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var userId = await Sessions.ConsumeTokenAsync(db, req.Token, TokenPurpose.VerifyEmail, now, ct)
                         ?? throw new ApiException(400, "verify_token_invalid", "This confirmation link is not valid any more. Ask for a new one from your account page.");
            await db.Users.Where(u => u.Id == userId && u.EmailVerifiedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerifiedAt, now), ct);
            return Results.NoContent();
        }).RequireRateLimiting(ApiErrors.AuthLimit).WithValidation();

        g.MapPost("/email/resend", async (ClaimsPrincipal principal, AppDbContext db, IOptions<AuthOptions> auth,
            IOptions<AppOptions> app, IEmailSender email, TimeProvider clock, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == principal.UserId(), ct);
            if (user is null) return Results.NotFound();
            if (user.EmailVerifiedAt is null)
                await SendVerificationAsync(db, user, auth.Value, app.Value, email, clock.GetUtcNow().UtcDateTime, ct);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting(ApiErrors.AuthLimit);
    }

    internal static async Task SendVerificationAsync(AppDbContext db, User user, AuthOptions auth, AppOptions app, IEmailSender email, DateTime now, CancellationToken ct)
    {
        var token = await Sessions.IssueTokenAsync(db, user.Id, TokenPurpose.VerifyEmail, TimeSpan.FromHours(auth.VerifyTokenHours), now, ct);
        await email.SendAsync(EmailTemplates.VerifyEmail(user.Email, await ClubNameAsync(db, ct),
            $"{app.WebUrl.TrimEnd('/')}/verify-email?token={token}"), ct);
    }

    private static async Task SetPasswordAsync(AppDbContext db, User user, string password, DateTime now, CancellationToken ct)
    {
        user.PasswordHash = Passwords.Hash(password);
        user.PasswordChangedAt = now;
        user.FailedLogins = 0;
        user.LockedUntil = null;
        await db.SaveChangesAsync(ct);
        // Any other outstanding reset link is void now.
        await db.UserTokens.Where(t => t.UserId == user.Id && t.Purpose == TokenPurpose.ResetPassword && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), ct);
    }

    internal static Task<string> ClubNameAsync(AppDbContext db, CancellationToken ct) =>
        db.ClubSettings.AsNoTracking().Select(c => c.Name).SingleAsync(ct);
}
