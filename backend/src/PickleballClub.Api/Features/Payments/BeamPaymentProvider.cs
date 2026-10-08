using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PickleballClub.Api.Domain;

namespace PickleballClub.Api.Features.Payments;

public class BeamOptions
{
    /// <summary>Playground: https://playground.api.beamcheckout.com — production: https://api.beamcheckout.com</summary>
    public string BaseUrl { get; set; } = "https://playground.api.beamcheckout.com";
    public string MerchantId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    /// <summary>The webhook HMAC key from Beam Lighthouse, base64 as shown there.</summary>
    public string WebhookHmacKey { get; set; } = "";
}

/// <summary>
/// Beam Checkout (https://docs.beamcheckout.com), written from its public documentation.
/// NOT YET RUN AGAINST BEAM: the Club has no merchant account, so no request has reached Playground. Before switching
/// <c>Payments:Provider</c> to "beam", make one PromptPay and one card payment in Playground and check the webhook arrives.
///
/// PromptPay → a QR charge (<c>POST /api/v1/charges</c>) that expires with the Hold; the QR is shown on our own page.
/// Card → a hosted Payment Link (<c>POST /api/v1/payment-links</c>) limited to cards, because charging a card directly
/// needs the card number to pass through this server (PCI scope).
/// Webhooks are verified by <c>X-Beam-Signature</c>: base64 HMAC-SHA256 of the raw body, keyed with the base64-decoded key.
/// </summary>
public sealed class BeamPaymentProvider(HttpClient http, IOptions<PaymentOptions> options, ILogger<BeamPaymentProvider> log) : IPaymentProvider
{
    public const string ProviderName = "beam";

    private BeamOptions Beam => options.Value.Beam;

    public string Name => ProviderName;

    public async Task<PaymentIntent> CreateAsync(PaymentRequest request, CancellationToken ct)
    {
        var expires = request.ExpiresAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return request.Method == PaymentMethod.PromptPay
            ? await CreateQrChargeAsync(request, expires, ct)
            : await CreateCardLinkAsync(request, expires, ct);
    }

    private async Task<PaymentIntent> CreateQrChargeAsync(PaymentRequest request, string expires, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["amount"] = Satang(request.Amount),
            ["currency"] = "THB",
            ["paymentMethod"] = new JsonObject
            {
                ["paymentMethodType"] = "QR_PROMPT_PAY",
                ["qrPromptPay"] = new JsonObject { ["expiryTime"] = expires },
            },
            ["referenceId"] = request.BookingCode,
            ["returnUrl"] = request.ReturnUrl,
        };
        var res = await PostAsync("/api/v1/charges", body, ct);
        var chargeId = res.GetProperty("chargeId").GetString() ?? throw new InvalidOperationException("Beam returned no chargeId");
        if (!res.TryGetProperty("encodedImage", out var image) || image.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Beam charge {chargeId} has no QR (actionRequired = {(res.TryGetProperty("actionRequired", out var a) ? a.GetString() : "?")})");

        // Prefer the QR's text (we draw it ourselves); fall back to Beam's ready-made picture.
        var qr = image.TryGetProperty("rawData", out var raw) && raw.GetString() is { Length: > 0 } text
            ? text
            : "data:image/png;base64," + image.GetProperty("imageBase64Encoded").GetString();
        return new PaymentIntent(Name, chargeId, PaymentMethod.PromptPay, qr, null, request.ExpiresAt);
    }

    private async Task<PaymentIntent> CreateCardLinkAsync(PaymentRequest request, string expires, CancellationToken ct)
    {
        static JsonObject Off() => new() { ["isEnabled"] = false };
        var body = new JsonObject
        {
            ["order"] = new JsonObject
            {
                ["currency"] = "THB",
                ["netAmount"] = Satang(request.Amount),
                ["description"] = $"Court booking {request.BookingCode}",
                ["referenceId"] = request.BookingCode,
            },
            ["linkSettings"] = new JsonObject
            {
                ["card"] = new JsonObject { ["isEnabled"] = true },
                ["cardInstallments"] = Off(),
                ["qrPromptPay"] = Off(),
                ["eWallets"] = Off(),
                ["mobileBanking"] = Off(),
                ["buyNowPayLater"] = Off(),
            },
            ["collectDeliveryAddress"] = false,
            ["redirectUrl"] = request.ReturnUrl,
            ["expiresAt"] = expires,
        };
        var res = await PostAsync("/api/v1/payment-links", body, ct);
        var id = res.GetProperty("id").GetString() ?? throw new InvalidOperationException("Beam returned no payment link id");
        var url = res.GetProperty("url").GetString() ?? throw new InvalidOperationException("Beam returned no payment link url");
        return new PaymentIntent(Name, id, PaymentMethod.Card, null, url, request.ExpiresAt);
    }

    private async Task<JsonElement> PostAsync(string path, JsonObject body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Beam.BaseUrl.TrimEnd('/') + path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Beam.MerchantId}:{Beam.ApiKey}")));
        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            log.LogError("Beam {Path} answered {Status}: {Body}", path, (int)res.StatusCode, text);
            throw new InvalidOperationException($"Beam {path} answered {(int)res.StatusCode}");
        }
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Beam takes amounts in the smallest unit: 300.00 THB is 30000.</summary>
    internal static long Satang(decimal baht) => (long)decimal.Round(baht * 100m, 0, MidpointRounding.AwayFromZero);

    public Task<WebhookResult> ParseWebhookAsync(HttpRequest request, string rawBody, CancellationToken ct) =>
        Task.FromResult(Parse(request.Headers["X-Beam-Signature"].ToString(), request.Headers["X-Beam-Event"].ToString(), rawBody, Beam.WebhookHmacKey));

    internal static WebhookResult Parse(string signature, string eventType, string rawBody, string hmacKeyBase64)
    {
        if (!SignatureIsValid(signature, rawBody, hmacKeyBase64)) return WebhookResult.Rejected;
        try
        {
            var doc = JsonDocument.Parse(rawBody).RootElement;
            string? Text(string name) => doc.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            switch (eventType)
            {
                case "payment_link.paid":
                    return Text("paymentLinkId") is { Length: > 0 } link ? WebhookResult.Payment(link, true) : WebhookResult.Ignored;

                case "charge.succeeded" or "charge.failed":
                    var succeeded = eventType == "charge.succeeded";
                    // A card paid through a Payment Link also raises charge events; our payment is the link, not that charge.
                    if (Text("source") == "PAYMENT_LINK")
                    {
                        // A failed try on a link is not final: the customer can try another card until the link expires.
                        if (!succeeded) return WebhookResult.Ignored;
                        return Text("sourceId") is { Length: > 0 } source ? WebhookResult.Payment(source, true) : WebhookResult.Ignored;
                    }
                    return Text("chargeId") is { Length: > 0 } charge ? WebhookResult.Payment(charge, succeeded) : WebhookResult.Ignored;

                default:
                    return WebhookResult.Ignored; // refunds, card authorisations, Bolt …: not used here
            }
        }
        catch (JsonException)
        {
            return WebhookResult.Ignored; // authentic but not JSON we understand
        }
    }

    /// <summary>X-Beam-Signature = base64( HMAC-SHA256( key = base64-decoded HMAC key, message = the exact request body ) ).</summary>
    internal static bool SignatureIsValid(string signature, string rawBody, string hmacKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(hmacKeyBase64)) return false;
        byte[] key, given;
        try
        {
            key = Convert.FromBase64String(hmacKeyBase64.Trim());
            given = Convert.FromBase64String(signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(rawBody));
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }
}
