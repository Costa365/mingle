using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TeamGames.Api.Health;
using TeamGames.Api.Administration;
using TeamGames.Api.Rooms;
using TeamGames.Core.Games;
using TeamGames.Core.Storage;
using TeamGames.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var environment = services.GetRequiredService<IHostEnvironment>();
    var paths = StoragePaths.Resolve(configuration["Storage:DataDirectory"], environment.ContentRootPath);
    Directory.CreateDirectory(paths.DirectoryPath);
    Directory.CreateDirectory(paths.KeyDirectory);
    return paths;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameCatalog>();
builder.Services.AddDbContext<TeamGamesDbContext>((services, options) => options.UseSqlite(new SqliteConnectionStringBuilder
{
    DataSource = services.GetRequiredService<StoragePaths>().DatabasePath,
    DefaultTimeout = 5,
    ForeignKeys = true
}.ToString()));
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<IStorageProbe, SqliteStorageProbe>();
builder.Services.AddDataProtection()
    .SetApplicationName("TeamGames");
builder.Services.AddOptions<KeyManagementOptions>().Configure<StoragePaths, ILoggerFactory>((options, paths, loggerFactory) =>
    options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(paths.KeyDirectory), loggerFactory));
builder.Services.AddHealthChecks().AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.AddRooms();
builder.AddAdministration();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context);
});
app.UseRoomSecurity();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/api/games", (GameCatalog catalog) => catalog.Games);
app.MapRooms();
app.MapAdministration();
app.MapGet("/api/games/{id}", (string id, GameCatalog catalog) =>
    catalog.Find(id) is { } game ? Results.Ok(game) : Results.NotFound());

app.MapHealthChecks("/api/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponse
});
app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

// Initialization must finish before accepting requests. A storage/migration
// failure stops startup instead of publishing a falsely healthy application.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

await app.RunAsync();

static Task WriteHealthResponse(HttpContext context, HealthReport report) =>
    context.Response.WriteAsJsonAsync(new
    {
        status = report.Status == HealthStatus.Healthy ? "healthy" : "unavailable"
    }, context.RequestAborted);

public partial class Program;
