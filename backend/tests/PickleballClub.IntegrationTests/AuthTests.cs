using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PickleballClub.IntegrationTests.Infrastructure;

namespace PickleballClub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == status, $"expected {(int)status} {code}, got {(int)res.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    private Task<HttpResponseMessage> Login(string email, string password, string path = "login") =>
        api.Anonymous().PostAsJsonAsync($"/api/v1/auth/{path}", new { email, password });

    private HttpClient WithToken(string token)
    {
        var client = api.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ---------------------------------------------------------------- register

    [Fact]
    public async Task Register_signs_the_customer_in()
    {
        var user = await api.RegisterAsync(displayName: "  Nok  ", phone: "081-234-5678");

        var me = await user.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(user.Id, me.GetProperty("id").GetGuid());
        Assert.Equal(user.Email, me.GetProperty("email").GetString());
        Assert.Equal("Nok", me.GetProperty("displayName").GetString());
        Assert.Equal("0812345678", me.GetProperty("phone").GetString());
        Assert.Equal("customer", me.GetProperty("role").GetString());
        Assert.True(me.GetProperty("hasPassword").GetBoolean());
        Assert.True(me.GetProperty("profileComplete").GetBoolean());
        Assert.False(me.GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task Without_a_phone_the_profile_is_not_complete()
    {
        var user = await api.RegisterAsync(phone: null);
        var me = await user.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.False(me.GetProperty("profileComplete").GetBoolean());
    }

    [Fact]
    public async Task An_email_can_register_only_once_whatever_its_case()
    {
        var user = await api.RegisterAsync();
        var again = await api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new { email = "  " + user.Email.ToUpperInvariant(), password = ApiFactory.Password, displayName = "Other" });
        await AssertError(again, HttpStatusCode.Conflict, "email_taken");
    }

    [Theory]
    [InlineData("not-an-email", ApiFactory.Password, "Nok", "email")]
    [InlineData("a@example.test", "too short", "Nok", "password")]
    [InlineData("a@example.test", ApiFactory.Password, "   ", "displayName")]
    public async Task Register_validates_its_fields(string email, string password, string displayName, string field)
    {
        var res = await api.Anonymous().PostAsJsonAsync("/api/v1/auth/register", new { email, password, displayName });
        await AssertError(res, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Json(res)).GetProperty("errors").TryGetProperty(field, out _), $"no error for {field}");
    }

    [Fact]
    public async Task A_password_longer_than_bcrypt_reads_is_refused()
    {
        var res = await api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new { email = ApiFactory.NewEmail(), password = new string('ก', 30), displayName = "Nok" }); // 30 Thai letters = 90 bytes
        await AssertError(res, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Register_refuses_a_phone_that_is_not_a_number()
    {
        var res = await api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new { email = ApiFactory.NewEmail(), password = ApiFactory.Password, displayName = "Nok", phone = "call me" });
        await AssertError(res, HttpStatusCode.BadRequest, "bad_phone");
    }

    // ---------------------------------------------------------------- sign in

    [Fact]
    public async Task Sign_in_works_with_the_right_password_only()
    {
        var user = await api.RegisterAsync();

        var ok = await Login(user.Email.ToUpperInvariant(), user.Password);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(user.Id, (await Json(ok)).GetProperty("user").GetProperty("id").GetGuid());

        // 400, never 401: the apps sign the user out on a 401.
        await AssertError(await Login(user.Email, "wrong password!"), HttpStatusCode.BadRequest, "invalid_credentials");
        await AssertError(await Login(ApiFactory.NewEmail(), user.Password), HttpStatusCode.BadRequest, "invalid_credentials");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_fifteen_minutes()
    {
        var user = await api.RegisterAsync();
        for (var i = 0; i < 5; i++)
            await AssertError(await Login(user.Email, "wrong password!"), HttpStatusCode.BadRequest, "invalid_credentials");

        await AssertError(await Login(user.Email, user.Password), HttpStatusCode.BadRequest, "invalid_credentials");

        api.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.OK, (await Login(user.Email, user.Password)).StatusCode);
    }

    [Fact]
    public async Task Me_requires_a_session()
    {
        await AssertError(await api.Anonymous().GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized, "unauthorized");
        await AssertError(await WithToken("not.a.token").GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Customers_and_admins_sign_in_through_their_own_door()
    {
        var customer = await api.RegisterAsync();
        var admin = await api.AdminAsync();

        var me = await admin.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal("admin", me.GetProperty("role").GetString());

        await AssertError(await Login(customer.Email, customer.Password, "admin/login"), HttpStatusCode.BadRequest, "invalid_credentials");
        await AssertError(await Login(admin.Email, admin.Password), HttpStatusCode.BadRequest, "invalid_credentials");
    }

    // ---------------------------------------------------------------- profile

    [Fact]
    public async Task Profile_update_sets_name_and_phone()
    {
        var user = await api.RegisterAsync(phone: null);

        var res = await user.Http.PutAsJsonAsync("/api/v1/auth/me", new { displayName = "Somchai", phone = "+66 81 234 5678" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var me = await Json(res);
        Assert.Equal("Somchai", me.GetProperty("displayName").GetString());
        Assert.Equal("+66812345678", me.GetProperty("phone").GetString());
        Assert.True(me.GetProperty("profileComplete").GetBoolean());

        await AssertError(await user.Http.PutAsJsonAsync("/api/v1/auth/me", new { displayName = "Somchai", phone = "12345" }), HttpStatusCode.BadRequest, "bad_phone");
        await AssertError(await user.Http.PutAsJsonAsync("/api/v1/auth/me", new { displayName = "", phone = "0812345678" }), HttpStatusCode.BadRequest, "validation_failed");
        await AssertError(await api.Anonymous().PutAsJsonAsync("/api/v1/auth/me", new { displayName = "X", phone = "0812345678" }), HttpStatusCode.Unauthorized, "unauthorized");
    }

    // ---------------------------------------------------------------- email confirmation

    [Fact]
    public async Task The_emailed_link_confirms_the_address_once()
    {
        var user = await api.RegisterAsync();
        var mail = api.LastEmail(user.Email, "verify_email");
        Assert.Contains("http://localhost:9010/verify-email?token=", mail.TextBody);
        var token = ApiFactory.TokenIn(mail);

        Assert.Equal(HttpStatusCode.NoContent, (await api.Anonymous().PostAsJsonAsync("/api/v1/auth/email/verify", new { token })).StatusCode);
        Assert.True((await user.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("emailVerified").GetBoolean());

        await AssertError(await api.Anonymous().PostAsJsonAsync("/api/v1/auth/email/verify", new { token }), HttpStatusCode.BadRequest, "verify_token_invalid");
        await AssertError(await api.Anonymous().PostAsJsonAsync("/api/v1/auth/email/verify", new { token = "nope" }), HttpStatusCode.BadRequest, "verify_token_invalid");
    }

    [Fact]
    public async Task Resending_the_confirmation_voids_the_older_link()
    {
        var user = await api.RegisterAsync();
        var first = ApiFactory.TokenIn(api.LastEmail(user.Email, "verify_email"));

        Assert.Equal(HttpStatusCode.NoContent, (await user.Http.PostAsync("/api/v1/auth/email/resend", null)).StatusCode);
        var second = ApiFactory.TokenIn(api.LastEmail(user.Email, "verify_email"));
        Assert.NotEqual(first, second);

        await AssertError(await api.Anonymous().PostAsJsonAsync("/api/v1/auth/email/verify", new { token = first }), HttpStatusCode.BadRequest, "verify_token_invalid");
        Assert.Equal(HttpStatusCode.NoContent, (await api.Anonymous().PostAsJsonAsync("/api/v1/auth/email/verify", new { token = second })).StatusCode);

        // already confirmed: nothing more is sent
        var sent = api.Emails.Count(e => e.To == user.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Http.PostAsync("/api/v1/auth/email/resend", null)).StatusCode);
        Assert.Equal(sent, api.Emails.Count(e => e.To == user.Email));
    }

    // ---------------------------------------------------------------- passwords

    [Fact]
    public async Task Forgot_password_never_tells_whether_the_address_is_registered()
    {
        var unknown = ApiFactory.NewEmail();
        Assert.Equal(HttpStatusCode.NoContent, (await api.Anonymous().PostAsJsonAsync("/api/v1/auth/password/forgot", new { email = unknown })).StatusCode);
        Assert.DoesNotContain(api.Emails, e => e.To == unknown);
    }

    [Fact]
    public async Task A_reset_link_sets_a_new_password_and_signs_out_old_sessions()
    {
        var user = await api.RegisterAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await api.Anonymous().PostAsJsonAsync("/api/v1/auth/password/forgot", new { email = user.Email })).StatusCode);
        var mail = api.LastEmail(user.Email, "reset_password");
        Assert.Contains("http://localhost:9010/reset-password?token=", mail.TextBody);
        var token = ApiFactory.TokenIn(mail);

        const string newPassword = "a brand new password";
        await AssertError(await api.Anonymous().PostAsJsonAsync("/api/v1/auth/password/reset", new { token, newPassword = "short" }), HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(HttpStatusCode.NoContent, (await api.Anonymous().PostAsJsonAsync("/api/v1/auth/password/reset", new { token, newPassword })).StatusCode);

        await AssertError(await Login(user.Email, user.Password), HttpStatusCode.BadRequest, "invalid_credentials");
        Assert.Equal(HttpStatusCode.OK, (await Login(user.Email, newPassword)).StatusCode);
        // the session from before the reset is dead
        await AssertError(await user.Http.GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized, "unauthorized");
        // the link works once
        await AssertError(await api.Anonymous().PostAsJsonAsync("/api/v1/auth/password/reset", new { token, newPassword = "yet another password" }), HttpStatusCode.BadRequest, "reset_token_invalid");
    }

    [Fact]
    public async Task A_reset_link_expires()
    {
        var user = await api.RegisterAsync();
        await api.Anonymous().PostAsJsonAsync("/api/v1/auth/password/forgot", new { email = user.Email });
        var token = ApiFactory.TokenIn(api.LastEmail(user.Email, "reset_password"));

        api.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));
        await AssertError(await api.Anonymous().PostAsJsonAsync("/api/v1/auth/password/reset", new { token, newPassword = "a brand new password" }), HttpStatusCode.BadRequest, "reset_token_invalid");
    }

    [Fact]
    public async Task An_admin_reset_link_points_at_the_back_office()
    {
        var admin = await api.AdminAsync();
        await api.Anonymous().PostAsJsonAsync("/api/v1/auth/password/forgot", new { email = admin.Email });
        Assert.Contains("http://localhost:9011/reset-password?token=", api.LastEmail(admin.Email, "reset_password").TextBody);
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_keeps_this_session()
    {
        var user = await api.RegisterAsync();
        const string newPassword = "a brand new password";

        await AssertError(await user.Http.PostAsJsonAsync("/api/v1/auth/password/change", new { currentPassword = "wrong password!", newPassword }), HttpStatusCode.BadRequest, "wrong_password");
        await AssertError(await user.Http.PostAsJsonAsync("/api/v1/auth/password/change", new { currentPassword = user.Password, newPassword = user.Password }), HttpStatusCode.BadRequest, "same_password");

        var res = await user.Http.PostAsJsonAsync("/api/v1/auth/password/change", new { currentPassword = user.Password, newPassword });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var fresh = (await Json(res)).GetProperty("accessToken").GetString()!;

        await AssertError(await user.Http.GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized, "unauthorized"); // old token
        Assert.Equal(HttpStatusCode.OK, (await WithToken(fresh).GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(user.Email, newPassword)).StatusCode);
    }

    // ---------------------------------------------------------------- Google

    private Task<HttpResponseMessage> Google(string credential) =>
        api.Anonymous().PostAsJsonAsync("/api/v1/auth/google", new { credential });

    [Fact]
    public async Task The_apps_can_ask_whether_google_sign_in_is_on()
    {
        var config = await api.Anonymous().GetFromJsonAsync<JsonElement>("/api/v1/auth/config");
        Assert.Equal(ApiFactory.GoogleClientId, config.GetProperty("googleClientId").GetString());
    }

    [Fact]
    public async Task Google_creates_the_customer_on_first_use_and_finds_them_afterwards()
    {
        var email = ApiFactory.NewEmail();
        var subject = Guid.NewGuid().ToString("N");

        var first = await Google(ApiFactory.GoogleCredential(subject, email.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var user = (await Json(first)).GetProperty("user");
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("customer", user.GetProperty("role").GetString());
        Assert.True(user.GetProperty("emailVerified").GetBoolean());
        Assert.False(user.GetProperty("hasPassword").GetBoolean());
        Assert.False(user.GetProperty("profileComplete").GetBoolean()); // no phone yet

        var second = await Google(ApiFactory.GoogleCredential(subject, email));
        Assert.Equal(user.GetProperty("id").GetGuid(), (await Json(second)).GetProperty("user").GetProperty("id").GetGuid());

        // no password on file: the password door stays shut
        await AssertError(await Login(email, ApiFactory.Password), HttpStatusCode.BadRequest, "invalid_credentials");
    }

    [Fact]
    public async Task Google_links_to_a_customer_who_has_confirmed_the_same_email_and_keeps_their_password()
    {
        var user = await api.RegisterAsync();
        var token = ApiFactory.TokenIn(api.LastEmail(user.Email, "verify_email"));
        await api.Anonymous().PostAsJsonAsync("/api/v1/auth/email/verify", new { token });

        var res = await Google(ApiFactory.GoogleCredential(Guid.NewGuid().ToString("N"), user.Email));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var linked = (await Json(res)).GetProperty("user");
        Assert.Equal(user.Id, linked.GetProperty("id").GetGuid());
        Assert.True(linked.GetProperty("hasPassword").GetBoolean());

        // Both doors open the same account, and the earlier session lives on.
        Assert.Equal(HttpStatusCode.OK, (await Login(user.Email, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.Http.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Google_takes_over_an_account_nobody_confirmed_and_shuts_out_whoever_registered_it()
    {
        // Someone registers another person's address with a password of their own, hoping the owner turns up later.
        var squatter = await api.RegisterAsync();

        // The real owner signs in with Google.
        var res = await Google(ApiFactory.GoogleCredential(Guid.NewGuid().ToString("N"), squatter.Email));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var owner = (await Json(res)).GetProperty("user");
        Assert.Equal(squatter.Id, owner.GetProperty("id").GetGuid());
        Assert.True(owner.GetProperty("emailVerified").GetBoolean());
        Assert.False(owner.GetProperty("hasPassword").GetBoolean());

        // The password and the session from before are dead.
        await AssertError(await Login(squatter.Email, squatter.Password), HttpStatusCode.BadRequest, "invalid_credentials");
        await AssertError(await squatter.Http.GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized, "unauthorized");
        // The owner's own session works.
        var ownerToken = (await Json(await Google(ApiFactory.GoogleCredential("ignored-subject", squatter.Email)))).GetProperty("accessToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await WithToken(ownerToken).GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Google_sign_in_is_refused_when_it_cannot_be_trusted()
    {
        await AssertError(await Google("garbage"), HttpStatusCode.BadRequest, "google_token_invalid");
        await AssertError(await Google(ApiFactory.GoogleCredential("s1", ApiFactory.NewEmail(), verified: false)), HttpStatusCode.BadRequest, "google_email_unverified");

        var admin = await api.AdminAsync();
        await AssertError(await Google(ApiFactory.GoogleCredential("s2", admin.Email)), HttpStatusCode.BadRequest, "google_token_invalid");
    }
}
