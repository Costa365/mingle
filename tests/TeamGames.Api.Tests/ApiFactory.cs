using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TeamGames.Api.Tests;

internal sealed class ApiFactory(string dataDirectory, string environment = "Production", TimeProvider? clock = null,
    Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = dataDirectory,
                ["Security:AllowInsecureCookies"] = "true",
                ["Rooms:EntryRateLimit"] = "1000"
            };
            if (settings is not null) foreach (var pair in settings) values[pair.Key] = pair.Value;
            configuration.AddInMemoryCollection(values);
        });
        if (configureServices is not null) builder.ConfigureServices(configureServices);
        if (clock is not null) builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock);
        });
    }
}
