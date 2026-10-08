using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace PickleballClub.Api.Services;

public static class ConnectionStrings
{
    /// <summary>
    /// Hosted Postgres (Neon, Render, Supabase …) hands out a <c>postgresql://user:password@host/db?sslmode=require</c> URL,
    /// which Npgsql does not read. Turns such a URL into Npgsql's <c>Host=…;Database=…</c> form; anything else is returned as it is.
    /// </summary>
    public static string? Normalize(string? value)
    {
        var text = value?.Trim();
        if (text is null || !(text.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)))
            return value;

        // Never echo the value in an error: it holds the password.
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            throw new InvalidOperationException("ConnectionStrings:Default looks like a postgresql:// URL but cannot be read. Copy it again from the database provider.");

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
        };
        if (userInfo.Length == 2) builder.Password = Uri.UnescapeDataString(userInfo[1]);

        foreach (var (key, values) in QueryHelpers.ParseQuery(uri.Query))
        {
            var v = values.ToString();
            switch (key.ToLowerInvariant())
            {
                case "sslmode": builder.SslMode = Parse<SslMode>(key, v.Replace("-", "")); break; // verify-full → VerifyFull
                case "channel_binding": builder.ChannelBinding = Parse<ChannelBinding>(key, v); break;
                case "options": builder.Options = v; break;
                case "application_name": builder.ApplicationName = v; break;
                // Other libpq parameters have no bearing on how this API connects.
            }
        }
        return builder.ConnectionString;
    }

    private static T Parse<T>(string key, string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"ConnectionStrings:Default has {key}={value}, which is not a value this API knows.");
}
