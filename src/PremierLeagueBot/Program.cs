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
    var botToken = builder.Configuration["BotToken"]
        ?? throw new InvalidOperationException(
            "BotToken is not configured. Add it to appsettings.json or environment variable BotToken.");
    builder.Services.Configure<FootballApiOptions>(
        builder.Configuration.GetSection(FootballApiOptions.Section));

    // ── Telegram Bot client ──────────────────────────────────────────────────
    builder.Services.AddSingleton<ITelegramBotClient>(
        new TelegramBotClient(botToken));

    // ── Premier League official Pulselive API (squad + fixtures) ────────────
    builder.Services.AddHttpClient("plapi", client =>
    {
        client.BaseAddress = new Uri("https://footballapi.pulselive.com/football/");
        client.DefaultRequestHeaders.Add("Origin", "https://www.premierleague.com");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        client.Timeout = TimeSpan.FromSeconds(20);
    });
    builder.Services.AddSingleton<PremierLeagueApiClient>();

    // ── TheSportsDB HTTP client (standings + league-wide fixtures) ───────────
    builder.Services
        .AddHttpClient<IFootballApiClient, FootballApiClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<FootballApiOptions>>().Value;
            if (!Uri.TryCreate(opts.BaseUrl, UriKind.Absolute, out var baseUri))
                throw new InvalidOperationException($"Invalid FootballApi:BaseUrl '{opts.BaseUrl}'");

            client.BaseAddress = baseUri;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PremierLeagueBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(20);
        })
        .AddStandardResilienceHandler();

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
