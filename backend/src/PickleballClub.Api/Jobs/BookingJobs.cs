using PickleballClub.Api.Features.Admin;
using PickleballClub.Api.Features.Bookings;

namespace PickleballClub.Api.Jobs;

/// <summary>Every 30 seconds: releases Holds whose time ran out, completes checked-in Bookings whose time is over, and sends reminders that are due.</summary>
public class BookingJobs(IServiceScopeFactory scopes, ILogger<BookingJobs> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
                var n = await bookings.ExpireHoldsAsync(stoppingToken);
                if (n > 0) log.LogInformation("Released {Count} unpaid holds", n);
                var done = await scope.ServiceProvider.GetRequiredService<AdminBookingService>().CompleteFinishedAsync(stoppingToken);
                if (done > 0) log.LogInformation("Completed {Count} finished bookings", done);
                var reminded = await scope.ServiceProvider.GetRequiredService<BookingMailer>().SendRemindersAsync(stoppingToken);
                if (reminded > 0) log.LogInformation("Sent {Count} booking reminders", reminded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Booking jobs run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
