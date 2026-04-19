using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PremierLeagueBot.Data;
using PremierLeagueBot.Infrastructure;
using PremierLeagueBot.Services.Achievements;
using PremierLeagueBot.Services.Background;
using PremierLeagueBot.Services.Bot;
using PremierLeagueBot.Services.Football;
using PremierLeagueBot.Services.Emoji;
using PremierLeagueBot.Services.Notification;
using PremierLeagueBot.Services.TelegramAvatar;
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
            "BotToken is not configured. Add it to appsettings.Development.json or environment variable BotToken.");
    builder.Services.Configure<FootballApiOptions>(
        builder.Configuration.GetSection(FootballApiOptions.Section));

    // ── Telegram Bot client ──────────────────────────────────────────────────
    builder.Services.AddSingleton<ITelegramBotClient>(
        new TelegramBotClient(botToken));

    // ── HTTP clients ──────────────────────────────────────────────────────────
    {
        var apiOpts = builder.Configuration
            .GetSection(FootballApiOptions.Section)
            .Get<FootballApiOptions>() ?? new FootballApiOptions();

        builder.Services
            .AddHttpClient(FootballApiClient.PlApiClient, client =>
            {
                client.BaseAddress = new Uri(apiOpts.PlBaseUrl);
                client.DefaultRequestHeaders.Add("Origin", "https://www.premierleague.com");
                client.DefaultRequestHeaders.Add("Referer", "https://www.premierleague.com/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            .AddStandardResilienceHandler();

        builder.Services
            .AddHttpClient(FootballApiClient.SportsDbClient, client =>
            {
                client.BaseAddress = new Uri(apiOpts.SportsDbBaseUrl);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PremierLeagueBot/1.0");
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            .AddStandardResilienceHandler();
    }

    builder.Services.AddSingleton<IFootballApiClient, FootballApiClient>();

    // ── CORS (required for Telegram Mini App to load the page) ──────────────
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("TelegramMiniApp", policy =>
        {
            policy.WithOrigins("https://web.telegram.org", "https://t.me")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });
    // узнать для чего
    // ── Memory cache ─────────────────────────────────────────────────────────
    builder.Services.AddMemoryCache();

    // ── Database (SQLite dev / PostgreSQL prod) ───────────────────────────────
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=premier_league_bot.db";

    if (connectionString.Contains("Host=") || connectionString.Contains("postgresql://"))
    {
        builder.Services.AddDbContextFactory<AppDbContext>(opts =>
            opts.UseNpgsql(connectionString, b =>
                b.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(3), errorCodesToAdd: null)));
        builder.Services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(connectionString, b =>
                b.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(3), errorCodesToAdd: null)));
    }
    else
    {
        builder.Services.AddDbContextFactory<AppDbContext>(opts =>
            opts.UseSqlite(connectionString));
        builder.Services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite(connectionString));
    }

    // ── Application services ──────────────────────────────────────────────────
    builder.Services.AddSingleton<NotificationService>();
    builder.Services.AddSingleton<EmojiPackService>();
    builder.Services.AddSingleton<UpdateHandler>();

    // ── Mini App services ─────────────────────────────────────────────────────
    builder.Services.AddScoped<TelegramAuthFilter>();
    builder.Services.AddScoped<AvatarService>();
    builder.Services.AddScoped<AchievementService>();

    // ── Controllers (for Mini App API) ───────────────────────────────────────
    builder.Services.AddControllers();

    // ── Background services ───────────────────────────────────────────────────
    builder.Services.AddHostedService<BotHostedService>();
    builder.Services.AddHostedService<DataUpdateService>();
    builder.Services.AddHostedService<MatchNotificationService>();
    builder.Services.AddHostedService<NewsNotificationService>();
    builder.Services.AddHostedService<PredictionScoringService>();

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

        // WAL mode — только для SQLite (PostgreSQL не поддерживает PRAGMA)
        if (db.Database.IsSqlite())
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        // Seed achievement definitions
        var achievements = scope.ServiceProvider.GetRequiredService<AchievementService>();
        await achievements.SeedAsync();
        Log.Information("Achievement definitions seeded");
    }

    app.MapHealthChecks("/health");

    // ── Static files + CORS for Telegram Mini App ─────────────────────────────
    // wwwroot/ will contain the Mini App frontend (HTML/JS/CSS).
    app.UseDefaultFiles();   // serves index.html for "/"

    // index.html must never be cached so deployments take effect immediately
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            if (ctx.File.Name == "index.html")
            {
                ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                ctx.Context.Response.Headers["Pragma"]        = "no-cache";
                ctx.Context.Response.Headers["Expires"]       = "0";
            }
        }
    });
    app.UseCors("TelegramMiniApp");

    // ── API routes ────────────────────────────────────────────────────────────
    app.MapControllers();

    // ── Load custom emoji pack (non-blocking; falls back to standard emoji) ───
    var emojiService = app.Services.GetRequiredService<EmojiPackService>();
    _ = emojiService.InitialiseAsync(); // fire-and-forget; bot starts without waiting

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
