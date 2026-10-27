using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using TeamGames.Core.Rooms;

namespace TeamGames.Api.Deployment;

public sealed record DeploymentSettings(IPAddress? KnownProxy)
{
    public static DeploymentSettings Read(IConfiguration configuration)
    {
        var address = configuration["Proxy:KnownProxy"];
        if (string.IsNullOrWhiteSpace(address)) return new((IPAddress?)null);
        if (!IPAddress.TryParse(address, out var proxy) || !Uri.TryCreate(configuration["Security:PublicOrigin"], UriKind.Absolute, out var origin) ||
            origin.Scheme != "https" || origin.AbsolutePath != "/" || origin.Query.Length > 0 || origin.Fragment.Length > 0 || origin.UserInfo.Length > 0 || configuration.GetValue<bool>("Security:AllowInsecureCookies"))
            throw new InvalidOperationException("A trusted proxy requires an IP address, HTTPS public origin and Secure cookies");
        return new(proxy);
    }
    public ForwardedHeadersOptions Forwarding()
    {
        var options = new ForwardedHeadersOptions { ForwardedHeaders = KnownProxy is null ? ForwardedHeaders.None : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
        options.KnownIPNetworks.Clear(); options.KnownProxies.Clear();
        if (KnownProxy is not null) options.KnownProxies.Add(KnownProxy);
        return options;
    }
}
public static class DeploymentSecurity
{
    public const int MaximumBodyBytes = 64 * 1024;
    public static void AddDeploymentSecurity(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(services => DeploymentSettings.Read(services.GetRequiredService<IConfiguration>()));
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaximumBodyBytes);
    }
    public static void UseDeploymentSecurity(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<DeploymentSettings>();
        if (settings.KnownProxy is not null) app.UseForwardedHeaders(settings.Forwarding());
        app.Use(async (context, next) =>
        {
            if (context.Request.ContentLength > MaximumBodyBytes)
            {
                context.Response.StatusCode = 413;
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                await context.Response.WriteAsJsonAsync(new RoomErrorView("request_too_large", "This request is too large"));
                return;
            }
            try { await next(context); }
            catch (BadHttpRequestException exception) when (exception.StatusCode == 413 && !context.Response.HasStarted)
            {
                context.Response.Clear(); context.Response.StatusCode = 413;
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                await context.Response.WriteAsJsonAsync(new RoomErrorView("request_too_large", "This request is too large"));
            }
        });
    }
}
