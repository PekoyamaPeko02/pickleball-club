using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PickleballClub.Api.Features.Auth;

/// <summary>Who Google says the person is.</summary>
public record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name);

public interface IGoogleTokenVerifier
{
    /// <summary>Checks the ID token the browser got from "Sign in with Google". Null when it is not a valid token for <paramref name="clientId"/>.</summary>
    Task<GoogleIdentity?> VerifyAsync(string credential, string clientId, CancellationToken ct);
}

/// <summary>
/// Validates the ID token's signature against Google's published keys, plus issuer, audience and lifetime.
/// NOT YET TRIED AGAINST LIVE GOOGLE: the Club has no OAuth client ID yet (config <c>Auth:GoogleClientId</c>).
/// </summary>
public sealed class GoogleTokenVerifier : IGoogleTokenVerifier
{
    private static readonly ConfigurationManager<OpenIdConnectConfiguration> Metadata = new(
        "https://accounts.google.com/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever());

    public async Task<GoogleIdentity?> VerifyAsync(string credential, string clientId, CancellationToken ct)
    {
        OpenIdConnectConfiguration google;
        try { google = await Metadata.GetConfigurationAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return null; } // Google unreachable: treat as "could not verify"

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(credential, new TokenValidationParameters
        {
            ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
            ValidAudience = clientId,
            IssuerSigningKeys = google.SigningKeys,
            ValidateLifetime = true,
        });
        if (!result.IsValid) return null;

        string? Claim(string name) => result.Claims.TryGetValue(name, out var v) ? v?.ToString() : null;
        var subject = Claim("sub");
        var email = Claim("email");
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(email)) return null;
        var verified = string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        return new GoogleIdentity(subject, email, verified, Claim("name"));
    }
}
