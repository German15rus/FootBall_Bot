using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PremierLeagueBot.Data;
using PremierLeagueBot.Infrastructure;
using PremierLeagueBot.Services.Background;
using PremierLeagueBot.Services.Bot;
using PremierLeagueBot.Services.Football;
using PremierLeagueBot.Services.Notification;
using Serilog;
using Telegram.Bot;

// ── Configure Serilog early so startup errors are captured ──────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/bot-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog();

    // ── Configuration binding ────────────────────────────────────────────────
    var botToken = "8632451769:AAFVS9WSs0XU5N5uBdkwC8aY1DXEYKY-j5E";
    builder.Services.Configure<FootballApiOptions>(
        builder.Configuration.GetSection(FootballApiOptions.Section));

    // ── Telegram Bot client ──────────────────────────────────────────────────
    builder.Services.AddSingleton<ITelegramBotClient>(
        new TelegramBotClient(botToken));

    // ── Football API HTTP client with Polly retry ────────────────────────────
    // Source: TheSportsDB (free, no key needed) + BBC Sport RSS
    builder.Services
        .AddHttpClient<IFootballApiClient, FootballApiClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<FootballApiOptions>>().Value;
            if (string.IsNullOrWhiteSpace(opts.BaseUrl))
                throw new InvalidOperationException(
                    $"Configuration section '{FootballApiOptions.Section}:BaseUrl' is missing.");

            if (!Uri.TryCreate(opts.BaseUrl, UriKind.Absolute, out var baseUri))
                throw new InvalidOperationException(
                    $"Invalid '{FootballApiOptions.Section}:BaseUrl': '{opts.BaseUrl}'.");

            client.BaseAddress = baseUri;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "PremierLeagueBot/1.0 (+https://github.com/your/repo)");
            client.Timeout = TimeSpan.FromSeconds(20);
        })
        .AddStandardResilienceHandler(); // Polly: retry + circuit-breaker

    // ── Memory cache ─────────────────────────────────────────────────────────
    builder.Services.AddMemoryCache();

    // ── Database (SQLite dev / PostgreSQL prod) ───────────────────────────────
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=premier_league_bot.db";

    if (connectionString.Contains("Host=") || connectionString.Contains("postgresql://"))
    {
        builder.Services.AddDbContextFactory<AppDbContext>(opts =>
            opts.UseNpgsql(connectionString));
        builder.Services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(connectionString));
    }
    else
    {
        builder.Services.AddDbContextFactory<AppDbContext>(opts =>
            opts.UseSqlite(connectionString));
        builder.Services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite(connectionString));
    }

    // ── Application services ──────────────────────────────────────────────────
    // Singleton: safe because both use IDbContextFactory (not DbContext directly)
    builder.Services.AddSingleton<NotificationService>();
    builder.Services.AddSingleton<UpdateHandler>();

    // ── Background services ───────────────────────────────────────────────────
    builder.Services.AddHostedService<BotHostedService>();
    builder.Services.AddHostedService<DataUpdateService>();
    builder.Services.AddHostedService<MatchNotificationService>();
    builder.Services.AddHostedService<NewsNotificationService>();

    // ── Health-check endpoint (useful for containers) ─────────────────────────
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>();

    var app = builder.Build();

    // ── Apply migrations on startup ───────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migrations applied");
    }

    app.MapHealthChecks("/health");

    // Long Polling: no webhook needed – BotHostedService calls bot.ReceiveAsync()
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed");
}
finally
{
    await Log.CloseAndFlushAsync();
}
