using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TeamGames.Api.Rooms;
using TeamGames.Core.Administration;
using TeamGames.Core.Rooms;
using TeamGames.Infrastructure.Administration;
using TeamGames.Infrastructure.Persistence;
using TeamGames.Infrastructure.Rooms;

namespace TeamGames.Api.Administration;

public static class AdminEndpoints
{
    public static void AddAdministration(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(services =>
        {
            var settings = new AdminSettings();
            services.GetRequiredService<IConfiguration>().GetSection("Admin").Bind(settings);
            settings.Validate();
            return settings;
        });
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AdminAuthentication>(AdminAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorizationBuilder().AddPolicy("administrator", policy => policy.AddAuthenticationSchemes(AdminAuthentication.SchemeName).RequireAuthenticatedUser());
        builder.Services.AddRateLimiter(options => options.AddPolicy("admin-login", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = context.RequestServices.GetRequiredService<AdminSettings>().LoginRateLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
    }
    public static void UseAdminIdentity(this WebApplication app) => app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api/admin"))
            context.User = (await context.AuthenticateAsync(AdminAuthentication.SchemeName)).Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
        await next(context);
    });
    private static CookieOptions Cookie(IConfiguration config) => new()
    { HttpOnly = true, Secure = !config.GetValue<bool>("Security:AllowInsecureCookies"), SameSite = SameSiteMode.Strict, Path = "/api/admin", IsEssential = true };
    private static async ValueTask<object?> Csrf(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
        return await next(context);
    }
    public static void MapAdministration(this WebApplication app)
    {
        app.MapGet("/api/admin/session", (HttpContext context, AdminSettings settings, IAntiforgery csrf) =>
            new AdminSessionView(settings.Enabled, context.User.Identity?.IsAuthenticated == true, csrf.GetAndStoreTokens(context).RequestToken!, context.Items["admin-expiry"] as long?))
            .RequireRateLimiting("room-read");
        app.MapPost("/api/admin/login", async (AdminLogin request, HttpContext context, AdminSettings settings, TeamGamesDbContext database,
            RoomWriter writer, TimeProvider time, IDataProtectionProvider protection, IConfiguration config, IAntiforgery csrf) =>
        {
            if (!settings.Enabled) throw new RoomException("admin_disabled", "Administration is not configured", 404);
            if (!settings.Accepts(request.Credential)) throw new RoomException("login_failed", "The administrator credential was not accepted", 401);
            var id = Guid.NewGuid();
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expires = time.GetUtcNow().AddMinutes(settings.SessionMinutes).Ticks;
            await writer.Gate.WaitAsync(context.RequestAborted);
            try
            {
                await using var transaction = await database.Database.BeginTransactionAsync(context.RequestAborted);
                if (context.User.Identity?.IsAuthenticated == true)
                    await database.AdminSessions.Where(s => s.Id == AdminAuthentication.SessionId(context.User)).ExecuteDeleteAsync(context.RequestAborted);
                database.AdminSessions.Add(new() { Id = id, Verifier = AdminSettings.Hash(secret), CredentialVersion = settings.CredentialVersion, ExpiresTicks = expires });
                database.AdminAudits.Add(new() { Id = Guid.NewGuid(), SessionId = id, CommandId = Guid.NewGuid(), CreatedTicks = time.GetUtcNow().Ticks, Action = "admin-login", TargetId = id.ToString() });
                await database.SaveChangesAsync(context.RequestAborted);
                await transaction.CommitAsync(context.RequestAborted);
            }
            finally { writer.Gate.Release(); }
            var cookie = Cookie(config); cookie.Expires = new DateTimeOffset(expires, TimeSpan.Zero);
            context.Response.Cookies.Append(AdminAuthentication.CookieName, protection.CreateProtector("admin-v1").Protect($"{id}:{secret}"), cookie);
            context.User = AdminAuthentication.Principal(id);
            return new AdminSessionView(true, true, csrf.GetAndStoreTokens(context).RequestToken!, expires);
        }).AddEndpointFilter(Csrf).RequireRateLimiting("admin-login");
        var admin = app.MapGroup("/api/admin").RequireAuthorization("administrator").RequireRateLimiting("room-read");
        admin.MapPost("/logout", async (HttpContext context, TeamGamesDbContext database, RoomWriter writer, IConfiguration config, TimeProvider time) =>
        {
            var id = AdminAuthentication.SessionId(context.User);
            await writer.Gate.WaitAsync(context.RequestAborted);
            try
            {
                await using var transaction = await database.Database.BeginTransactionAsync(context.RequestAborted);
                await database.AdminSessions.Where(s => s.Id == id).ExecuteDeleteAsync(context.RequestAborted);
                database.AdminAudits.Add(new() { Id = Guid.NewGuid(), SessionId = id, CommandId = Guid.NewGuid(), CreatedTicks = time.GetUtcNow().Ticks, Action = "admin-logout", TargetId = id.ToString() });
                await database.SaveChangesAsync(context.RequestAborted);
                await transaction.CommitAsync(context.RequestAborted);
            }
            finally { writer.Gate.Release(); }
            context.Response.Cookies.Delete(AdminAuthentication.CookieName, Cookie(config));
            return Results.NoContent();
        }).AddEndpointFilter(Csrf);
        admin.MapGet("/rooms", (RoomStore store, int page = 1, int pageSize = 20, string? status = null) => store.AdminRoomsAsync(page, pageSize, status));
        admin.MapGet("/rooms/{id:guid}", (Guid id, RoomStore store) => store.AdminRoomAsync(id));
        admin.MapPost("/rooms/{id:guid}/commands", async (Guid id, AdminRoomCommand command, HttpContext context, RoomStore store, AdminSettings settings, RoomPublisher publisher) =>
        {
            var result = await store.AdminExecuteAsync(id, AdminAuthentication.SessionId(context.User), settings, command);
            await publisher.PublishAsync(id);
            return result;
        }).AddEndpointFilter(Csrf);
        admin.MapGet("/prompts", (RoomStore store, int page = 1, int pageSize = 20, string? gameId = null, string? search = null) => store.AdminPromptsAsync(page, pageSize, gameId, search));
        admin.MapPost("/prompts", async (AdminPromptCommand command, HttpContext context, RoomStore store, AdminSettings settings, RoomPublisher publisher) =>
        {
            var result = await store.AdminPromptAsync(AdminAuthentication.SessionId(context.User), settings, command);
            foreach (var id in await store.AdminActiveRoomIdsAsync()) await publisher.PublishAsync(id);
            return result;
        }).AddEndpointFilter(Csrf);
        admin.MapGet("/audit", (RoomStore store, int page = 1, int pageSize = 20) => store.AdminAuditAsync(page, pageSize));
    }
}
