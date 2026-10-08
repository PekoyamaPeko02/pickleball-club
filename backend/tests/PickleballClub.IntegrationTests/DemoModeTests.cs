using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PickleballClub.Api.Features.Payments;
using PickleballClub.IntegrationTests.Infrastructure;

namespace PickleballClub.IntegrationTests;

/// <summary>Outside Development, payments may only be simulated on a demo site (<c>Demo:Enabled</c>).</summary>
[Collection(ApiCollection.Name)]
public class DemoModeTests(ApiFactory api)
{
    /// <summary>The same API and database, started the way a hosted site is: not Development, with a signing key of its own.</summary>
    private WebApplicationFactory<Program> Hosted(params (string Key, string Value)[] settings) =>
        api.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Jwt:SigningKey", "a-hosted-signing-key-of-at-least-32-chars");
            foreach (var (key, value) in settings) b.UseSetting(key, value);
        });

    private static List<string> RoutesOf(WebApplicationFactory<Program> app) =>
        app.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? "").ToList();

    [Fact]
    public async Task A_developer_machine_is_not_a_demo_site()
    {
        var club = await api.Anonymous().GetFromJsonAsync<JsonElement>("/api/v1/club");
        Assert.False(club.GetProperty("demo").GetBoolean());
        Assert.Contains(RoutesOf(api), r => r.Contains("/dev/emails"));
        Assert.Equal(HttpStatusCode.OK, (await api.Anonymous().GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }

    [Fact]
    public void A_hosted_site_does_not_start_with_simulated_payments()
    {
        using var hosted = Hosted();
        var error = Record.Exception(() => hosted.CreateClient());
        Assert.NotNull(error);
        Assert.Contains("no money would be collected", error.ToString());
    }

    [Fact]
    public void A_demo_site_does_not_start_with_a_real_payment_provider()
    {
        using var hosted = Hosted(("Demo:Enabled", "true"), ("Payments:Provider", "beam"),
            ("Payments:Beam:MerchantId", "merchant"), ("Payments:Beam:ApiKey", "key"), ("Payments:Beam:WebhookHmacKey", "aG1hYw=="));
        var error = Record.Exception(() => hosted.CreateClient());
        Assert.NotNull(error);
        Assert.Contains("Turn Demo off to take real payments", error.ToString());
    }

    [Fact]
    public async Task A_demo_site_simulates_payments_and_keeps_the_developer_tools_closed()
    {
        await using var demo = Hosted(("Demo:Enabled", "true"));
        var http = demo.CreateClient();

        var club = await http.GetFromJsonAsync<JsonElement>("/api/v1/club");
        Assert.True(club.GetProperty("demo").GetBoolean());

        var routes = RoutesOf(demo);
        Assert.DoesNotContain(routes, r => r.Contains("/dev/emails")); // it would hand anyone the password-reset links
        Assert.Contains(routes, r => r.Contains("simulate-payment"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/swagger/v1/swagger.json")).StatusCode);

        // A visitor registers, books and "pays".
        var email = ApiFactory.NewEmail();
        var registered = await http.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = ApiFactory.Password, displayName = "Demo Visitor", phone = "0812345678" });
        Assert.True(registered.IsSuccessStatusCode, await registered.Content.ReadAsStringAsync());
        var visitor = ApiFactory.Signed(http, await registered.Content.ReadFromJsonAsync<JsonElement>(), email, ApiFactory.Password);

        var held = await visitor.BookAsync(SlotPicker.Next(api));
        Assert.Equal("held", held.Status());
        // The public can scan this QR: it must not be a PromptPay payload a banking app would pay.
        Assert.Equal(MockPaymentProvider.DemoQrText, held.GetProperty("payment").GetProperty("qrPayload").GetString());

        var paid = await demo.CreateClient().PostAsync($"/api/v1/dev/bookings/{held.Id()}/simulate-payment", null);
        Assert.True(paid.IsSuccessStatusCode, $"{(int)paid.StatusCode}: {string.Join("\n", api.ServerErrors)}");
        Assert.Equal("confirmed", (await paid.Content.ReadFromJsonAsync<JsonElement>()).Status());
    }
}
