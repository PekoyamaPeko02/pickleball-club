using Npgsql;
using PickleballClub.Api.Services;

namespace PickleballClub.Tests;

public class ConnectionStringsTests
{
    private static NpgsqlConnectionStringBuilder Read(string url) => new(ConnectionStrings.Normalize(url));

    [Fact]
    public void A_keyword_connection_string_is_left_alone()
    {
        const string local = "Host=localhost;Port=5434;Database=pickleball;Username=pickleball;Password=pickleball";
        Assert.Same(local, ConnectionStrings.Normalize(local));
        Assert.Null(ConnectionStrings.Normalize(null));
    }

    [Fact]
    public void A_hosted_postgres_url_becomes_keywords()
    {
        // The shape Neon shows in its dashboard.
        var b = Read("postgresql://neondb_owner:npg_AbC123xyz@ep-cool-name-a1b2c3d4.ap-southeast-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require");

        Assert.Equal("ep-cool-name-a1b2c3d4.ap-southeast-1.aws.neon.tech", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("neondb", b.Database);
        Assert.Equal("neondb_owner", b.Username);
        Assert.Equal("npg_AbC123xyz", b.Password);
        Assert.Equal(SslMode.Require, b.SslMode);
        Assert.Equal(ChannelBinding.Require, b.ChannelBinding);
    }

    [Fact]
    public void Port_and_escaped_characters_are_read()
    {
        var b = Read("  postgres://app%2Buser:p%40ss%2Fw%3Brd@db.example.test:6543/app%20db?sslmode=verify-full&connect_timeout=10  ");

        Assert.Equal("db.example.test", b.Host);
        Assert.Equal(6543, b.Port);
        Assert.Equal("app db", b.Database);
        Assert.Equal("app+user", b.Username);
        Assert.Equal("p@ss/w;rd", b.Password);
        Assert.Equal(SslMode.VerifyFull, b.SslMode);
    }

    [Theory]
    [InlineData("postgresql://user:s3cret-pass@host/db?sslmode=sometimes")]
    [InlineData("postgresql://user:s3cret-pass@host/db?channel_binding=7")]
    [InlineData("postgresql://user:s3cret-pass@/db")]
    public void A_url_it_cannot_read_fails_without_showing_the_password(string url)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ConnectionStrings.Normalize(url));
        Assert.DoesNotContain("s3cret-pass", error.Message);
    }
}
