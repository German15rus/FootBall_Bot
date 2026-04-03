using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Formatters;
using PremierLeagueBot.Models.Api;
using PremierLeagueBot.Models.Bot;
using PremierLeagueBot.Services.Football;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PremierLeagueBot.Services.Bot;

public sealed class UpdateHandler(
    ITelegramBotClient bot,
    IFootballApiClient football,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<UpdateHandler> logger)
{
    // ── Entry point ──────────────────────────────────────────────────────────

    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        try
        {
            await (update.Type switch
            {
                UpdateType.Message       => HandleMessageAsync(update.Message!, ct),
                UpdateType.CallbackQuery => HandleCallbackAsync(update.CallbackQuery!, ct),
                _                        => Task.CompletedTask
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
        }
    }

    // ── Message router ───────────────────────────────────────────────────────

    private async Task HandleMessageAsync(Message msg, CancellationToken ct)
    {
        if (msg.From is null || msg.Text is null) return;

        await EnsureUserAsync(msg.From, ct);

        var text = msg.Text.Trim();

        if (text.StartsWith("/start"))                     { await HandleStartAsync(msg, ct);     return; }
        if (text is "/table"   or "📊 Таблица")           { await HandleTableAsync(msg, ct);     return; }
        if (text is "/matches" or "📅 Матчи")             { await HandleMatchesAsync(msg, ct);   return; }
        if (text is "/team"    or "🏟 Команда")           { await HandleTeamListAsync(msg, ct);  return; }
        if (text is "/myteam"  or "⭐ Моя команда")       { await HandleMyTeamMenuAsync(msg, ct);return; }

        if (text.StartsWith("/team "))
        {
            await HandleTeamByNameAsync(msg, text[6..].Trim(), ct);
            return;
        }

        await bot.SendMessage(msg.Chat.Id,
            "Воспользуйся меню или напиши /start 👇",
            cancellationToken: ct);
    }

    // ── /start — красочное приветствие ───────────────────────────────────────

    private async Task HandleStartAsync(Message msg, CancellationToken ct)
    {
        var keyboard = new ReplyKeyboardMarkup(
        [
            [new KeyboardButton("📊 Таблица"),   new KeyboardButton("📅 Матчи")],
            [new KeyboardButton("🏟 Команда"),   new KeyboardButton("⭐ Моя команда")],
        ])
        {
            ResizeKeyboard  = true,
            OneTimeKeyboard = false
        };

        var welcome =
            $"👋 Привет, <b>{msg.From!.FirstName}</b>!\n" +
            "━━━━━━━━━━━━━━━━━━━━━━\n" +
            "⚽ Добро пожаловать в <b>EPL Bot</b> — твой личный\n" +
            "гид по <b>Английской Премьер-Лиге</b>! 🏴󠁧󠁢󠁥󠁮󠁧󠁿🔥\n\n" +
            "Вот что я умею:\n\n" +
            "📊 <b>Таблица</b>\n" +
            "Актуальная турнирная таблица АПЛ со всеми 20 командами: место, очки, разница мячей.\n\n" +
            "📅 <b>Матчи</b>\n" +
            "Расписание ближайших игр на 7 дней — дата, время, стадион.\n\n" +
            "🏟 <b>Команда</b>\n" +
            "Состав и результаты последних 5 матчей любой команды лиги.\n\n" +
            "⭐ <b>Моя команда</b>\n" +
            "Выбери любимую команду и получай уведомления за 15 минут до матча, итог после игры и свежие новости!\n" +
            "━━━━━━━━━━━━━━━━━━━━━━\n" +
            "Выбери раздел 👇";

        await bot.SendMessage(
            msg.Chat.Id, welcome,
            parseMode:   ParseMode.Html,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    // ── Таблица ──────────────────────────────────────────────────────────────

    private async Task HandleTableAsync(Message msg, CancellationToken ct)
    {
        await bot.SendChatAction(msg.Chat.Id, ChatAction.Typing, cancellationToken: ct);
        var standings = await football.GetStandingsAsync(ct);
        await bot.SendMessage(msg.Chat.Id,
            StandingsFormatter.Format(standings),
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

    // ── Матчи ────────────────────────────────────────────────────────────────

    private async Task HandleMatchesAsync(Message msg, CancellationToken ct)
    {
        await bot.SendChatAction(msg.Chat.Id, ChatAction.Typing, cancellationToken: ct);
        var from    = DateTime.UtcNow.Date;
        var matches = await football.GetMatchesAsync(from, from.AddDays(7), ct);
        await bot.SendMessage(msg.Chat.Id,
            MatchesFormatter.FormatUpcoming(matches),
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

    // ── Команда: список команд ───────────────────────────────────────────────

    private async Task HandleTeamListAsync(Message msg, CancellationToken ct)
    {
        await bot.SendChatAction(msg.Chat.Id, ChatAction.Typing, cancellationToken: ct);

        var teams = await GetTeamsAsync(ct);
        if (teams.Count == 0)
        {
            await bot.SendMessage(msg.Chat.Id,
                "⚠️ Данные о командах загружаются. Попробуй через несколько секунд.",
                cancellationToken: ct);
            return;
        }

        var buttons = teams
            .OrderBy(t => t.Name)
            .Chunk(2)
            .Select(row => row.Select(t =>
                InlineKeyboardButton.WithCallbackData(t.Name, CallbackData.TeamInfo(t.Id))))
            .ToArray();

        await bot.SendMessage(msg.Chat.Id,
            "🏟 <b>Выбери команду:</b>",
            parseMode:   ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    // ── Команда: по имени через /team Arsenal ────────────────────────────────

    private async Task HandleTeamByNameAsync(Message msg, string teamName, CancellationToken ct)
    {
        var teams = await GetTeamsAsync(ct);
        var found = teams.FirstOrDefault(t =>
            t.Name.Contains(teamName, StringComparison.OrdinalIgnoreCase));

        if (found == default)
        {
            await bot.SendMessage(msg.Chat.Id,
                $"❌ Команда «{teamName}» не найдена. Проверь название.",
                cancellationToken: ct);
            return;
        }

        await SendTeamInfoAsync(msg.Chat.Id, found.Id, found.Name, ct);
    }

    // ── Моя команда ──────────────────────────────────────────────────────────

    private async Task HandleMyTeamMenuAsync(Message msg, CancellationToken ct)
    {
        await bot.SendChatAction(msg.Chat.Id, ChatAction.Typing, cancellationToken: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var user = await db.Users
            .Include(u => u.FavoriteTeam)
            .FirstOrDefaultAsync(u => u.TelegramId == msg.From!.Id, ct);

        var header = user?.FavoriteTeam is not null
            ? $"⭐ Сейчас следишь за: <b>{user.FavoriteTeam.Name}</b>\n\nВыбери другую команду или отпишись:"
            : "⭐ <b>Выбери любимую команду</b>\n\nБудешь получать уведомления о её матчах и новостях:";

        var teams = await GetTeamsAsync(ct);
        if (teams.Count == 0)
        {
            await bot.SendMessage(msg.Chat.Id,
                "⚠️ Данные о командах загружаются. Попробуй через несколько секунд.",
                cancellationToken: ct);
            return;
        }

        var buttons = teams
            .OrderBy(t => t.Name)
            .Chunk(2)
            .Select(row => row.Select(t =>
                InlineKeyboardButton.WithCallbackData(t.Name, CallbackData.SetFavorite(t.Id))))
            .ToList<IEnumerable<InlineKeyboardButton>>();

        if (user?.FavoriteTeamId is not null)
            buttons.Add([InlineKeyboardButton.WithCallbackData("❌ Отписаться от уведомлений", CallbackData.RemoveMyTeam)]);

        await bot.SendMessage(msg.Chat.Id, header,
            parseMode:   ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    // ── Callback ─────────────────────────────────────────────────────────────

    private async Task HandleCallbackAsync(CallbackQuery query, CancellationToken ct)
    {
        if (query.Data is null || query.Message is null) return;

        CallbackData.TryParse(query.Data, out var action, out var param);

        switch (action)
        {
            case "team_info":
                if (int.TryParse(param, out var teamId))
                {
                    var teamName = await ResolveTeamNameAsync(teamId, ct);
                    await SendTeamInfoAsync(query.Message.Chat.Id, teamId, teamName, ct);
                }
                break;

            case "set_fav":
                if (int.TryParse(param, out var favId))
                    await SetFavoriteTeamAsync(query, favId, ct);
                break;

            case "remove_my_team":
                await RemoveFavoriteTeamAsync(query, ct);
                break;
        }

        try { await bot.AnswerCallbackQuery(query.Id, cancellationToken: ct); }
        catch { /* already answered or timed out */ }
    }

    // ── Информация о команде (состав + последние матчи) ──────────────────────

    private async Task SendTeamInfoAsync(long chatId, int teamId, string teamName, CancellationToken ct)
    {
        await bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

        var squadTask  = football.GetTeamSquadAsync(teamId, ct);
        var recentTask = football.GetRecentMatchesAsync(teamId, 5, ct);
        await Task.WhenAll(squadTask, recentTask);

        var squadText  = TeamInfoFormatter.FormatSquad(teamName, squadTask.Result);
        var recentText = TeamInfoFormatter.FormatRecentMatches(teamName, recentTask.Result);

        await bot.SendMessage(chatId, squadText,
            parseMode: ParseMode.Html, cancellationToken: ct);

        await bot.SendMessage(chatId, recentText,
            parseMode: ParseMode.Html, cancellationToken: ct);
    }

    // ── Установить любимую команду ───────────────────────────────────────────

    private async Task SetFavoriteTeamAsync(CallbackQuery query, int teamId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Ensure user exists (callback may come before any message handler)
        await EnsureUserAsync(query.From, ct);

        var user = await db.Users.FindAsync([query.From.Id], ct);
        if (user is null) return;

        // Upsert team if not yet in DB (happens when clicked before DataUpdateService finishes)
        if (!await db.Teams.AnyAsync(t => t.TeamId == teamId, ct))
        {
            var standings = await football.GetStandingsAsync(ct);
            var dto       = standings.FirstOrDefault(s => s.TeamId == teamId);
            if (dto is not null)
            {
                db.Teams.Add(new Data.Entities.Team
                {
                    TeamId    = dto.TeamId,
                    Name      = dto.TeamName,
                    ShortName = dto.ShortName,
                    EmblemUrl = dto.EmblemUrl
                });
                await db.SaveChangesAsync(ct);
            }
        }

        var teamName = (await db.Teams.FindAsync([teamId], ct))?.Name
                    ?? await ResolveTeamNameAsync(teamId, ct);

        user.FavoriteTeamId = teamId;
        await db.SaveChangesAsync(ct);

        await bot.SendMessage(query.Message!.Chat.Id,
            $"🎉 Готово! Теперь ты следишь за <b>{teamName}</b>!\n\n" +
            "Ты будешь получать:\n" +
            "🔔 Напоминание за 15 минут до матча\n" +
            "🏁 Результат сразу после финального свистка\n" +
            "📰 Важные новости команды",
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

    // ── Удалить любимую команду ──────────────────────────────────────────────

    private async Task RemoveFavoriteTeamAsync(CallbackQuery query, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([query.From.Id], ct);
        if (user is null) return;

        user.FavoriteTeamId = null;
        await db.SaveChangesAsync(ct);

        await bot.SendMessage(query.Message!.Chat.Id,
            "✅ Ты отписан от уведомлений. Можешь выбрать другую команду в любое время.",
            cancellationToken: ct);
    }

    // ── Вспомогательные методы ───────────────────────────────────────────────

    /// <summary>
    /// Returns team list: tries DB first (fast), falls back to live standings API
    /// if DB hasn't been synced yet (first seconds after startup).
    /// </summary>
    private async Task<List<(int Id, string Name)>> GetTeamsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dbTeams = await db.Teams.OrderBy(t => t.Name).ToListAsync(ct);

        if (dbTeams.Count > 0)
            return dbTeams.Select(t => (t.TeamId, t.Name)).ToList();

        // Fallback: load from standings API (cached, very fast after first call)
        var standings = await football.GetStandingsAsync(ct);
        return standings.Select(s => (s.TeamId, s.TeamName)).ToList();
    }

    /// <summary>Resolves team name by ID from DB or standings cache.</summary>
    private async Task<string> ResolveTeamNameAsync(int teamId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var team = await db.Teams.FindAsync([teamId], ct);
        if (team is not null) return team.Name;

        var standings = await football.GetStandingsAsync(ct);
        return standings.FirstOrDefault(s => s.TeamId == teamId)?.TeamName ?? $"Команда #{teamId}";
    }

    private async Task EnsureUserAsync(Telegram.Bot.Types.User from, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.TelegramId == from.Id, ct))
        {
            db.Users.Add(new Data.Entities.User
            {
                TelegramId   = from.Id,
                Username     = from.Username,
                FirstName    = from.FirstName,
                RegisteredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("New user registered: {TelegramId} (@{Username})", from.Id, from.Username);
        }
    }
}
