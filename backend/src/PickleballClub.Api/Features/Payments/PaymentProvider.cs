using Microsoft.Extensions.Options;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Payments;

/// <param name="ReturnUrl">Where the provider's hosted page sends the Customer back to (card payments).</param>
public record PaymentRequest(Guid BookingId, string BookingCode, decimal Amount, string Method, DateTime ExpiresAt, string ReturnUrl);

/// <param name="QrPayload">PromptPay: the text to draw as a QR.</param>
/// <param name="CheckoutUrl">Card: the provider's hosted payment page to send the Customer to.</param>
public record PaymentIntent(string Provider, string ProviderRef, string Method, string? QrPayload, string? CheckoutUrl, DateTime ExpiresAt);

/// <summary>What a webhook call turned out to be.</summary>
/// <param name="Authentic">False when the signature did not verify: answer 401 and do nothing.</param>
/// <param name="ProviderRef">The payment the event is about; null when the event is not about one of our payments (acknowledge and ignore).</param>
public record WebhookResult(bool Authentic, string? ProviderRef, bool Succeeded)
{
    public static readonly WebhookResult Rejected = new(false, null, false);
    public static readonly WebhookResult Ignored = new(true, null, false);
    public static WebhookResult Payment(string providerRef, bool succeeded) => new(true, providerRef, succeeded);
}

/// <summary>
/// Abstraction over the payment gateway. An implementation creates a charge and later confirms it through
/// <c>POST /api/v1/payments/webhook/{provider}</c>; a Booking is never confirmed any other way.
/// The charge must expire no later than the Hold (<see cref="PaymentRequest.ExpiresAt"/>) so money cannot arrive for a
/// Court that has gone back on sale.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }
    Task<PaymentIntent> CreateAsync(PaymentRequest request, CancellationToken ct);
    /// <summary>Validate a webhook request (signature etc.) and extract the payment + outcome.</summary>
    Task<WebhookResult> ParseWebhookAsync(HttpRequest request, string rawBody, CancellationToken ct);
}

public class PaymentOptions
{
    /// <summary>"mock" (local development, no money moves) or "beam".</summary>
    public string Provider { get; set; } = "mock";
    public BeamOptions Beam { get; set; } = new();
    /// <summary>PromptPay ID used by the mock provider to render a scannable (but never confirmed) QR.</summary>
    public string MockPromptPayId { get; set; } = "0812345678";
    public string MockWebhookSecret { get; set; } = "dev-webhook-secret";
}

/// <summary>
/// Local-dev provider: no money moves. PromptPay gets a real-looking QR payload, card gets no hosted page; either is
/// "paid" through a signed webhook call or the Development-only simulate endpoint.
/// </summary>
public class MockPaymentProvider(IOptions<PaymentOptions> options) : IPaymentProvider
{
    public const string ProviderName = "mock";

    public string Name => ProviderName;

    public Task<PaymentIntent> CreateAsync(PaymentRequest request, CancellationToken ct)
    {
        var qr = request.Method == PaymentMethod.PromptPay ? PromptPay.Payload(options.Value.MockPromptPayId, request.Amount) : null;
        return Task.FromResult(new PaymentIntent(Name, $"mock_{Guid.NewGuid():N}", request.Method, qr, null, request.ExpiresAt));
    }

    public Task<WebhookResult> ParseWebhookAsync(HttpRequest request, string rawBody, CancellationToken ct)
    {
        if (request.Headers["X-Webhook-Secret"] != options.Value.MockWebhookSecret)
            return Task.FromResult(WebhookResult.Rejected);
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(rawBody).RootElement;
            var providerRef = doc.GetProperty("providerRef").GetString() ?? "";
            var status = doc.GetProperty("status").GetString();
            return Task.FromResult(WebhookResult.Payment(providerRef, status == "succeeded"));
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Task.FromResult(WebhookResult.Rejected);
        }
    }
}
