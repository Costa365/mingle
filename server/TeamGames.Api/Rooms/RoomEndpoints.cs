using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using TeamGames.Core.Rooms;
using TeamGames.Core.Prompts;
using TeamGames.Core.Games;
using TeamGames.Infrastructure.Persistence;
using TeamGames.Infrastructure.Rooms;

namespace TeamGames.Api.Rooms;

public static class RoomEndpoints
{
    public static void AddRooms(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(services =>
        {
            var settings = new RoomSettings();
            services.GetRequiredService<IConfiguration>().GetSection("Rooms").Bind(settings);
            if (settings.Capacity is < 2 or > 100 || settings.IdleHours is < 1 or > 168 ||
                settings.RetentionHours is < 1 or > 168 || settings.GuestDays is < 1 or > 7)
                throw new InvalidOperationException("Invalid room settings");
            return settings;
        });
        builder.Services.AddSingleton<IGameRandom, GameRandom>();
        builder.Services.AddSingleton<IGameModule, ThisOrThat>();
        builder.Services.AddSingleton<IGameModule, WouldYouRather>();
        builder.Services.AddSingleton<IGameModule, NeverHaveIEver>();
        builder.Services.AddSingleton<IGameModule, MostLikelyTo>();
        builder.Services.AddSingleton<IGameModule, TwoTruthsAndALie>();
        builder.Services.AddSingleton<GameModules>();
        builder.Services.AddSingleton<RoomWriter>();
        builder.Services.AddSingleton<PresenceRegistry>();
        builder.Services.AddScoped<RoomStore>();
        builder.Services.AddAuthentication(GuestAuthentication.SchemeName).AddScheme<AuthenticationSchemeOptions, GuestAuthentication>(GuestAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "mingle.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        builder.Services.AddOptions<AntiforgeryOptions>().Configure<IConfiguration>((options, configuration) =>
            options.Cookie.SecurePolicy = configuration.GetValue<bool>("Security:AllowInsecureCookies")
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);
        builder.Services.AddSignalR(options => { options.MaximumReceiveMessageSize = 4096; options.EnableDetailedErrors = false; });
        builder.Services.AddSingleton<RoomPublisher>();
        builder.Services.AddHostedService<RoomMaintenance>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("entry", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Rooms:EntryRateLimit", 90),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
            options.AddPolicy("room-read", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public static bool OriginAllowed(HttpRequest request, IConfiguration configuration)
    {
        var origin = request.Headers.Origin.ToString();
        var expected = configuration["Security:PublicOrigin"] ?? $"{request.Scheme}://{request.Host}";
        if (request.Headers["Sec-Fetch-Site"] == "cross-site") return false;
        if (origin.Length == 0) return !string.Equals(request.Headers.Upgrade, "websocket", StringComparison.OrdinalIgnoreCase);
        return string.Equals(origin, expected.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    public static void UseRoomSecurity(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            if ((context.Request.Path.StartsWithSegments("/hubs") ||
                 (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))) &&
                !OriginAllowed(context.Request, context.RequestServices.GetRequiredService<IConfiguration>()))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new RoomErrorView("origin_rejected", "Use this app from its configured origin"));
                return;
            }
            try { await next(context); }
            catch (RoomException exception)
            {
                context.Response.StatusCode = exception.Status;
                await context.Response.WriteAsJsonAsync(new RoomErrorView(exception.Code, exception.Message));
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new RoomErrorView("csrf_invalid", "Refresh the page and try again"));
            }
        });
    }

    private static async ValueTask<object?> CheckCsrf(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
        return await next(context);
    }

    public static void MapRooms(this WebApplication app)
    {
        app.MapGet("/api/session", (HttpContext context, IAntiforgery antiforgery) =>
            new SessionView(antiforgery.GetAndStoreTokens(context).RequestToken!)).RequireRateLimiting("entry");
        app.MapPost("/api/session", async (HttpContext context, IAntiforgery antiforgery, TeamGamesDbContext database,
            RoomWriter writer, TimeProvider time, RoomSettings settings, IDataProtectionProvider protection, IConfiguration configuration) =>
        {
            await GuestAuthentication.EnsureAsync(context, database, writer, time, settings, protection, configuration);
            return Results.Ok(new SessionView(antiforgery.GetAndStoreTokens(context).RequestToken!));
        }).AddEndpointFilter(CheckCsrf).RequireRateLimiting("entry").Produces<SessionView>();

        app.MapPost("/api/rooms", async (EntryRequest request, HttpContext context, RoomStore store, RoomPublisher publisher) =>
        {
            var result = await store.EnterAsync(GuestAuthentication.GuestId(context.User), request, true);
            await publisher.PublishAsync(result.Snapshot.Id);
            return Results.Ok(result);
        }).RequireAuthorization().AddEndpointFilter(CheckCsrf).RequireRateLimiting("entry").Produces<CommandResult>();
        app.MapPost("/api/rooms/join", async (EntryRequest request, HttpContext context, RoomStore store, RoomPublisher publisher) =>
        {
            var result = await store.EnterAsync(GuestAuthentication.GuestId(context.User), request, false);
            await publisher.PublishAsync(result.Snapshot.Id);
            return Results.Ok(result);
        }).RequireAuthorization().AddEndpointFilter(CheckCsrf).RequireRateLimiting("entry").Produces<CommandResult>();
        app.MapGet("/api/rooms/{id:guid}", async (Guid id, HttpContext context, RoomStore store) =>
            Results.Ok(await store.GetAsync(id, GuestAuthentication.GuestId(context.User))))
            .RequireAuthorization().RequireRateLimiting("room-read").Produces<RoomSnapshot>();
        app.MapPost("/api/rooms/{id:guid}/commands", async (Guid id, RoomCommand command, HttpContext context, RoomStore store, RoomPublisher publisher) =>
        {
            var result = await store.ExecuteAsync(id, GuestAuthentication.GuestId(context.User), command);
            await publisher.PublishAsync(id);
            return Results.Ok(result);
        }).RequireAuthorization().AddEndpointFilter(CheckCsrf).RequireRateLimiting("room-read").Produces<CommandResult>();
        app.MapPost("/api/rooms/{id:guid}/game", async (Guid id, GameCommand command, HttpContext context, RoomStore store, RoomPublisher publisher) =>
        {
            var result = await store.PlayAsync(id, GuestAuthentication.GuestId(context.User), command);
            await publisher.PublishAsync(id);
            return Results.Ok(result);
        }).RequireAuthorization().AddEndpointFilter(CheckCsrf).RequireRateLimiting("room-read").Produces<CommandResult>();
        app.MapGet("/api/rooms/{id:guid}/prompts", async (Guid id, string? gameId, string? category, string? tag, string? search, HttpContext context, RoomStore store) =>
            Results.Ok(await store.PromptsAsync(id, GuestAuthentication.GuestId(context.User), gameId, category, tag, search)))
            .RequireAuthorization().RequireRateLimiting("room-read").Produces<PromptPage>();
        app.MapPost("/api/rooms/{id:guid}/prompts", async (Guid id, PromptCommand command, HttpContext context, RoomStore store, RoomPublisher publisher) =>
        {
            var result = await store.EditPromptAsync(id, GuestAuthentication.GuestId(context.User), command);
            await publisher.PublishAsync(id);
            return Results.Ok(result);
        }).RequireAuthorization().AddEndpointFilter(CheckCsrf).RequireRateLimiting("room-read").Produces<CommandResult>();
        app.MapHub<RoomHub>("/hubs/rooms", options => options.LongPolling.PollTimeout = TimeSpan.FromSeconds(25));
    }
}
