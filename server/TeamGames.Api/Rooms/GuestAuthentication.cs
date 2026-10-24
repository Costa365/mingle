using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TeamGames.Core.Rooms;
using TeamGames.Infrastructure.Persistence;
using TeamGames.Infrastructure.Rooms;

namespace TeamGames.Api.Rooms;

public sealed class GuestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IDataProtectionProvider protection, TeamGamesDbContext database, TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Guest";
    public const string CookieName = "mingle.guest";
    public static string Verifier(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    public static ClaimsPrincipal Principal(Guid id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], SchemeName));

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(CookieName, out var cookie)) return AuthenticateResult.NoResult();
        try
        {
            var parts = protection.CreateProtector("guest-v1").Unprotect(cookie).Split(':');
            if (parts.Length != 2 || !Guid.TryParse(parts[0], out var id)) return AuthenticateResult.Fail("Invalid guest session");
            var guest = await database.Guests.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, Context.RequestAborted);
            if (guest is null || guest.ExpiresTicks <= time.GetUtcNow().Ticks ||
                !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(guest.Verifier), Convert.FromHexString(Verifier(parts[1]))))
                return AuthenticateResult.Fail("Expired guest session");
            return AuthenticateResult.Success(new AuthenticationTicket(Principal(id), SchemeName));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return AuthenticateResult.Fail("Invalid guest session");
        }
    }

    public static Guid GuestId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static async Task<Guid> EnsureAsync(HttpContext context, TeamGamesDbContext database, RoomWriter writer,
        TimeProvider time, RoomSettings settings, IDataProtectionProvider protection, IConfiguration configuration)
    {
        if (context.User.Identity?.IsAuthenticated == true) return GuestId(context.User);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var guest = new GuestSession { Id = Guid.NewGuid(), Verifier = Verifier(secret), ExpiresTicks = time.GetUtcNow().AddDays(settings.GuestDays).Ticks };
        await writer.Gate.WaitAsync(context.RequestAborted);
        try
        {
            database.Guests.Add(guest);
            await database.SaveChangesAsync(context.RequestAborted);
        }
        finally { writer.Gate.Release(); }
        context.Response.Cookies.Append(CookieName, protection.CreateProtector("guest-v1").Protect($"{guest.Id}:{secret}"), new CookieOptions
        {
            HttpOnly = true,
            Secure = !configuration.GetValue<bool>("Security:AllowInsecureCookies"),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = new DateTimeOffset(guest.ExpiresTicks, TimeSpan.Zero),
            IsEssential = true
        });
        context.User = Principal(guest.Id);
        return guest.Id;
    }
}
