using Microsoft.EntityFrameworkCore;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Features.Bookings;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Payments;

public static class PaymentEndpoints
{
    /// <param name="simulate">Development or a demo site: also map the endpoint that pretends a payment arrived.</param>
    public static RouteGroupBuilder MapPayments(this RouteGroupBuilder api, bool simulate)
    {
        // Gateway → us. The provider validates the signature; a Booking's status only ever becomes "confirmed" here.
        api.MapPost("/payments/webhook/{provider}", async (
            string provider, HttpRequest request, IPaymentProvider payments, BookingService bookings, ILoggerFactory log, CancellationToken ct) =>
        {
            if (!string.Equals(provider, payments.Name, StringComparison.OrdinalIgnoreCase)) return Results.NotFound();

            using var reader = new StreamReader(request.Body);
            var raw = await reader.ReadToEndAsync(ct);
            var result = await payments.ParseWebhookAsync(request, raw, ct);
            if (!result.Authentic) return Results.Unauthorized();
            // An event that is not about one of our payments is acknowledged, so the gateway stops retrying it.
            if (result.ProviderRef is null) return Results.Ok(new { received = true });

            try
            {
                await bookings.ConfirmPaymentAsync(payments.Name, result.ProviderRef, result.Succeeded, raw, ct);
            }
            catch (ApiException e) when (e.Code == "payment_not_found")
            {
                log.CreateLogger("Payments").LogWarning("Webhook from {Provider} for an unknown payment {Ref}", payments.Name, result.ProviderRef);
            }
            return Results.Ok(new { received = true });
        }).AllowAnonymous().WithTags("Payments");

        if (simulate)
        {
            // Development and demo sites only: pretend the Customer paid the newest open payment of the Booking.
            api.MapPost("/dev/bookings/{id:guid}/simulate-payment", async (
                Guid id, AppDbContext db, BookingService bookings, CancellationToken ct) =>
            {
                var pay = await db.Payments.AsNoTracking().Where(p => p.BookingId == id && p.Status == PaymentStatus.Pending)
                    .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
                if (pay is null) return Results.NotFound();
                await bookings.ConfirmPaymentAsync(pay.Provider, pay.ProviderRef, true, """{"simulated":true}""", ct);
                return Results.Ok(await BookingEndpoints.LoadDtoAsync(db, id, ct));
            }).WithTags("Dev");
        }

        return api;
    }
}
