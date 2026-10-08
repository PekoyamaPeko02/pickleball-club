using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleballClub.Api.Features.Payments;

namespace PickleballClub.Tests;

public class BeamPaymentProviderTests
{
    // The example from Beam's "Webhook Authentication" page: key, exact body and the signature it must produce.
    private const string DocKey = "KOFELguf5L1ltuDlkDHGUkPPnQhrgYYijTR4Fqh7APc=";
    private const string DocBody = """{"chargeId":"ch_30GtUweMWec7r2hHIsV5xxQeJKp","merchantId":"m_2sHxsByPwESKYM4nMwdEBdhubPS","referenceId":"order#10001","status":"SUCCEEDED","currency":"THB","amount":3000000,"source":"PAYMENT_LINK","sourceId":"57Iot6c11o","transactionTime":"2025-07-23T10:16:12Z","paymentMethod":{"paymentMethodType":"CARD","card":{"last4":"1111","brand":"VISA"},"cardInstallments":null,"cardNetworkToken":null,"qrPromptPay":null,"alipay":null,"weChatPay":null,"trueMoney":null,"linePay":null,"shopeePay":null,"bangkokBankApp":null,"kPlus":null,"scbEasy":null,"krungsriApp":null},"failureCode":"","customer":{"primaryPhone":{"countryCode":"+66","number":"0958051075"},"email":"","deliveryAddress":{"contactName":"","phone":{"countryCode":"","number":""},"address":{"streetAddress":"","city":"","country":"","postCode":""}}},"createdAt":"2025-07-23T10:15:56.102401Z","updatedAt":"2025-07-23T10:16:17.418991Z"}""";
    private const string DocSignature = "1XzWtJHZ9Y1tmjkA/XZUIn1ZHrUQp1d0Ms0oDQfJBto=";

    private static string Sign(string body, string key = DocKey) =>
        Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(Convert.FromBase64String(key), Encoding.UTF8.GetBytes(body)));

    [Fact]
    public void The_signature_check_reproduces_beams_published_example() =>
        Assert.True(BeamPaymentProvider.SignatureIsValid(DocSignature, DocBody, DocKey));

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void A_wrong_or_missing_signature_is_rejected(string signature)
    {
        Assert.False(BeamPaymentProvider.SignatureIsValid(signature, DocBody, DocKey));
        Assert.False(BeamPaymentProvider.Parse(signature, "charge.succeeded", DocBody, DocKey).Authentic);
    }

    [Fact]
    public void A_changed_body_or_a_missing_key_is_rejected()
    {
        Assert.False(BeamPaymentProvider.SignatureIsValid(DocSignature, DocBody.Replace("3000000", "1"), DocKey));
        Assert.False(BeamPaymentProvider.SignatureIsValid(DocSignature, DocBody, ""));
    }

    [Fact]
    public void A_card_paid_through_a_payment_link_points_at_the_link_not_the_charge()
    {
        // Beam's example is exactly this case: source PAYMENT_LINK, sourceId = the link.
        var r = BeamPaymentProvider.Parse(DocSignature, "charge.succeeded", DocBody, DocKey);
        Assert.True(r.Authentic);
        Assert.Equal("57Iot6c11o", r.ProviderRef);
        Assert.True(r.Succeeded);
    }

    [Fact]
    public void A_failed_try_on_a_payment_link_is_ignored_because_the_customer_can_try_again()
    {
        var r = BeamPaymentProvider.Parse(DocSignature, "charge.failed", DocBody, DocKey);
        Assert.True(r.Authentic);
        Assert.Null(r.ProviderRef);
    }

    [Theory]
    [InlineData("charge.succeeded", """{"chargeId":"ch_1","status":"SUCCEEDED","source":"API"}""", "ch_1", true)]
    [InlineData("charge.failed", """{"chargeId":"ch_1","status":"FAILED","source":"API"}""", "ch_1", false)]
    [InlineData("charge.succeeded", """{"chargeId":"ch_2","status":"SUCCEEDED"}""", "ch_2", true)]
    [InlineData("payment_link.paid", """{"paymentLinkId":"rGtqz6DafS","status":"PAID"}""", "rGtqz6DafS", true)]
    public void Payment_events_name_the_payment_and_its_outcome(string eventType, string body, string expectedRef, bool succeeded)
    {
        var r = BeamPaymentProvider.Parse(Sign(body), eventType, body, DocKey);
        Assert.True(r.Authentic);
        Assert.Equal(expectedRef, r.ProviderRef);
        Assert.Equal(succeeded, r.Succeeded);
    }

    [Theory]
    [InlineData("refund.succeeded", """{"refundId":"r_1"}""")]
    [InlineData("transaction.created", """{"transactionId":"t_1"}""")]
    [InlineData("charge.succeeded", """{"status":"SUCCEEDED"}""")]   // no id to match
    [InlineData("charge.succeeded", "not json")]
    public void Other_authentic_events_are_acknowledged_and_ignored(string eventType, string body)
    {
        var r = BeamPaymentProvider.Parse(Sign(body), eventType, body, DocKey);
        Assert.True(r.Authentic);
        Assert.Null(r.ProviderRef);
    }

    [Theory]
    [InlineData(300, 30000)]
    [InlineData(1250.5, 125050)]
    [InlineData(0.01, 1)]
    public void Amounts_go_to_beam_in_satang(decimal baht, long satang) => Assert.Equal(satang, BeamPaymentProvider.Satang(baht));

    // ---------------------------------------------------------------- requests (against a stub, shaped like Beam's documented examples)

    private sealed class Stub(string responseJson) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(responseJson, Encoding.UTF8, "application/json") };
        }
    }

    private static (BeamPaymentProvider Provider, Stub Stub) Beam(string responseJson)
    {
        var stub = new Stub(responseJson);
        var options = Options.Create(new PaymentOptions
        {
            Provider = "beam",
            Beam = new BeamOptions { BaseUrl = "https://playground.api.beamcheckout.com/", MerchantId = "merchant", ApiKey = "key", WebhookHmacKey = DocKey },
        });
        return (new BeamPaymentProvider(new HttpClient(stub), options, NullLogger<BeamPaymentProvider>.Instance), stub);
    }

    private static readonly DateTime Expiry = new(2026, 10, 8, 3, 15, 0, DateTimeKind.Utc);
    private static PaymentRequest Request(string method) =>
        new(Guid.NewGuid(), "PB10001", 900m, method, Expiry, "http://localhost:9010/bookings/abc");

    [Fact]
    public async Task PromptPay_creates_a_qr_charge_that_expires_with_the_hold()
    {
        var (beam, stub) = Beam("""{"chargeId":"ch_abc","actionRequired":"ENCODED_IMAGE","paymentMethodType":"QR_PROMPT_PAY","encodedImage":{"imageBase64Encoded":"iVBORw0KGgo=","expiry":"2026-10-08T03:15:00Z","rawData":"00020101021229370016A000000677010111"}}""");

        var intent = await beam.CreateAsync(Request("promptpay"), default);

        Assert.Equal(HttpMethod.Post, stub.Request!.Method);
        Assert.Equal("https://playground.api.beamcheckout.com/api/v1/charges", stub.Request.RequestUri!.ToString());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("merchant:key")), stub.Request.Headers.Authorization!.ToString());
        var body = JsonDocument.Parse(stub.Body!).RootElement;
        Assert.Equal(90000, body.GetProperty("amount").GetInt64());
        Assert.Equal("THB", body.GetProperty("currency").GetString());
        Assert.Equal("QR_PROMPT_PAY", body.GetProperty("paymentMethod").GetProperty("paymentMethodType").GetString());
        Assert.Equal("2026-10-08T03:15:00Z", body.GetProperty("paymentMethod").GetProperty("qrPromptPay").GetProperty("expiryTime").GetString());
        Assert.Equal("PB10001", body.GetProperty("referenceId").GetString());
        Assert.Equal("http://localhost:9010/bookings/abc", body.GetProperty("returnUrl").GetString());

        Assert.Equal(new PaymentIntent("beam", "ch_abc", "promptpay", "00020101021229370016A000000677010111", null, Expiry), intent);
    }

    [Fact]
    public async Task Without_qr_text_the_ready_made_picture_is_used()
    {
        var (beam, _) = Beam("""{"chargeId":"ch_abc","actionRequired":"ENCODED_IMAGE","encodedImage":{"imageBase64Encoded":"iVBORw0KGgo=","expiry":"2026-10-08T03:15:00Z"}}""");
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", (await beam.CreateAsync(Request("promptpay"), default)).QrPayload);
    }

    [Fact]
    public async Task Card_creates_a_payment_link_that_takes_cards_only()
    {
        var (beam, stub) = Beam("""{"id":"rGtqz6DafS","url":"https://playground-pay.beamcheckout.com/merchant/rGtqz6DafS"}""");

        var intent = await beam.CreateAsync(Request("card"), default);

        Assert.Equal("https://playground.api.beamcheckout.com/api/v1/payment-links", stub.Request!.RequestUri!.ToString());
        var body = JsonDocument.Parse(stub.Body!).RootElement;
        Assert.Equal(90000, body.GetProperty("order").GetProperty("netAmount").GetInt64());
        Assert.Equal("THB", body.GetProperty("order").GetProperty("currency").GetString());
        Assert.Equal("PB10001", body.GetProperty("order").GetProperty("referenceId").GetString());
        Assert.True(body.GetProperty("linkSettings").GetProperty("card").GetProperty("isEnabled").GetBoolean());
        Assert.False(body.GetProperty("linkSettings").GetProperty("qrPromptPay").GetProperty("isEnabled").GetBoolean());
        Assert.Equal("2026-10-08T03:15:00Z", body.GetProperty("expiresAt").GetString());
        Assert.Equal("http://localhost:9010/bookings/abc", body.GetProperty("redirectUrl").GetString());

        Assert.Equal(new PaymentIntent("beam", "rGtqz6DafS", "card", null, "https://playground-pay.beamcheckout.com/merchant/rGtqz6DafS", Expiry), intent);
    }

    [Fact]
    public async Task A_refusal_from_beam_is_an_error_not_a_payment()
    {
        var stub = new FailingStub();
        var beam = new BeamPaymentProvider(new HttpClient(stub), Options.Create(new PaymentOptions { Beam = new BeamOptions { MerchantId = "m", ApiKey = "k" } }), NullLogger<BeamPaymentProvider>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => beam.CreateAsync(Request("promptpay"), default));
    }

    private sealed class FailingStub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("""{"error":{"errorCode":"UNAUTHORIZED"}}""") });
    }
}
