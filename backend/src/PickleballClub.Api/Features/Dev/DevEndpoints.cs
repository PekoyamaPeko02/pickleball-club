using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Dev;

/// <summary>Helpers that exist only in the Development environment. Never mapped anywhere else.</summary>
public static class DevEndpoints
{
    public static void MapDev(this RouteGroupBuilder api, IWebHostEnvironment env)
    {
        if (!env.IsDevelopment()) return;
        var g = api.MapGroup("/dev").WithTags("Dev");

        // The emails the dev sender "sent" (newest first), so a developer can copy a confirmation or reset link.
        g.MapGet("/emails", (IEmailSender sender) =>
            sender is DevEmailSender dev ? Results.Ok(dev.Sent) : Results.NotFound());
    }
}
