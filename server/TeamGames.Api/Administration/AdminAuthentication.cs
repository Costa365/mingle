using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TeamGames.Core.Administration;
using TeamGames.Infrastructure.Persistence;

namespace TeamGames.Api.Administration;

public sealed class AdminAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IDataProtectionProvider protection, TeamGamesDbContext database, TimeProvider time, AdminSettings settings)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Administrator";
    public const string CookieName = "mingle.admin";
    public static ClaimsPrincipal Principal(Guid id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], SchemeName));
    public static Guid SessionId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!settings.Enabled || !Request.Cookies.TryGetValue(CookieName, out var cookie)) return AuthenticateResult.NoResult();
        try
        {
            var parts = protection.CreateProtector("admin-v1").Unprotect(cookie).Split(':');
            if (parts.Length != 2 || !Guid.TryParse(parts[0], out var id)) return AuthenticateResult.Fail("Invalid administrator session");
            var session = await database.AdminSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, Context.RequestAborted);
            if (session is null || session.ExpiresTicks <= time.GetUtcNow().Ticks || session.CredentialVersion != settings.CredentialVersion ||
                !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(session.Verifier), Convert.FromHexString(AdminSettings.Hash(parts[1]))))
                return AuthenticateResult.Fail("Expired administrator session");
            Context.Items["admin-expiry"] = session.ExpiresTicks;
            return AuthenticateResult.Success(new AuthenticationTicket(Principal(id), SchemeName));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException) { return AuthenticateResult.Fail("Invalid administrator session"); }
    }
}
