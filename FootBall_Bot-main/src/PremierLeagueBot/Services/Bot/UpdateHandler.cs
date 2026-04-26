using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Formatters;
using PremierLeagueBot.Models.Api;
using PremierLeagueBot.Models.Bot;
using PremierLeagueBot.Services.Emoji;
using PremierLeagueBot.Services.Football;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PremierLeagueBot.Services.Bot;

public sealed class UpdateHandler(
    ITelegramBotClient bot,
    IFootballApiClient football,
    IServiceScopeFactory scopeFactory,
    EmojiPackService emojiService,
    IConfiguration configuration,
    ILogger<UpdateHandler> logger)
{
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

    private async Task HandleMessageAsync(Message msg, CancellationToken ct)
    {
        if (msg.From is null || msg.Text is null) return;

        await EnsureUserAsync(msg.From, ct);

        var text = msg.Text.Trim();

        if (text.StartsWith("/start"))                    { await HandleStartAsync(msg, ct);      return; }
        if (text is "/table"   or "📊 Таблица")          { await HandleTableAsync(msg, ct);      return; }
        if (text is "/matches" or "📅 Матчи")            { await HandleMatchesAsync(msg, ct);    return; }
        if (text is "/team"    or "🏟 Команда")          { await HandleTeamListAsync(msg, ct);   return; }
        if (text is "/myteam"  or "⭐ Моя команда")      { await HandleMyTeamMenuAsync(msg, ct); return; }
        if (text is "/predict" or "🔮 Предикты")         { await HandlePredictAsync(msg, ct);    return; }

        if (text.StartsWith("/team "))
        {
            await HandleTeamByNameAsync(msg, text[6..].Trim(), ct);
            return;
        }

        await bot.SendMessage(msg.Chat.Id, "Воспользуйся меню или напиши /start 👇", cancellationToken: ct);
    }

    private async Task HandleStartAsync(Message msg, CancellationToken ct)
    {
        var keyboard = new ReplyKeyboardMarkup(
        [
            [new KeyboardButton("📊 Таблица"),  new KeyboardButton("📅 Матчи")],
            [new KeyboardButton("🏟 Команда"),  new KeyboardButton("⭐ Моя команда")],
            [new KeyboardButton("🔮 Предикты")],
        ])
        {
            ResizeKeyboard  = true,
            OneTimeKeyboard = false
        };

        var userMention = msg.From!.Username is { Length: > 0 } un
            ? $"@{un}"
            : $"<b>{msg.From.FirstName}</b>";

        var welcome =
            $"👋 Привет, {userMention}!\n" +
            "━━━━━━━━━━━━━━━━━━━━━━\n" +
            "⚽ Добро пожаловать в <b>EPL Bot</b> — твой личный\n" +
            "гид по <b>Английской Премьер-Лиге</b>! 🏴󠁧󠁢󠁥󠁮󠁧󠁿🔥\n" +
            "Я помогу тебе быть в курсе обо всех самых актуальных событиях лиги!🔥\n\n" +
            "Вот что я умею:\n\n" +
            "📊 <b>Таблица:</b>\n" +
            "Актуальная турнирная таблица АПЛ со всеми 20 командами: место, очки, разница мячей.\n\n" +
            "📅 <b>Матчи:</b>\n" +
            "Расписание ближайших игр на 7 дней — дата, время, стадион.\n\n" +
            "🏟 <b>Команда:</b>\n" +
            "Состав и результаты последних 5 матчей любой нужной тебе команды лиги.\n\n" +
            "⭐ <b>Моя команда:</b>\n" +
            "Выбери любимую команду и получай уведомления за 15 минут до матча, итог после игры и свежие новости!\n" +
            "━━━━━━━━━━━━━━━━━━━━━━\n" +
            "Выбери нужный раздел 👇";

        await bot.SendMessage(
            msg.Chat.Id, welcome,
            parseMode:   ParseMode.Html,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandlePredictAsync(Message msg, CancellationToken ct)
    {
        var miniAppUrl = configuration["MiniAppUrl"] ?? "";
        if (string.IsNullOrEmpty(miniAppUrl))
        {
            await bot.SendMessage(msg.Chat.Id, "⚙️ Mini App пока не настроен.", cancellationToken: ct);
            return;
        }

        var inlineKeyboard = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithWebApp("🔮 Открыть предикты", new WebAppInfo { Url = miniAppUrl }));

        await bot.SendMessage(
            msg.Chat.Id,
            "🔮 <b>Предикты АПЛ</b>\n\nУгадывай счета матчей и соревнуйся с другими!\nНажми кнопку ниже 👇",
            parseMode:   ParseMode.Html,
            replyMarkup: inlineKeyboard,
            cancellationToken: ct);
    }

    private async Task HandleTableAsync(Message msg, CancellationToken ct)
    {
        await bot.SendChatAction(msg.Chat.Id, ChatAction.Typing, cancellationToken: ct);
        var standings = await football.GetStandingsAsync(ct);
        await bot.SendMessage(msg.Chat.Id,
            StandingsFormatter.Format(standings, emojiService),
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

    private async Task HandleMatchesAsync(Message msg, CancellationToken ct)
    {
        await bot.SendChatAction(msg.Chat.Id, ChatAction.Typing, cancellationToken: ct);

        int? favoriteTeamId = null;
        if (msg.From is not null)
        {
            using var scope = scopeFactory.CreateScope();
            var userRepo    = scope.ServiceProvider.GetRequiredService<UserRepository>();
            favoriteTeamId  = (await userRepo.GetByIdAsync(msg.From.Id, ct))?.FavoriteTeamId;
        }

        var from       = DateTime.UtcNow.Date;
        var allMatches = await football.GetMatchesAsync(from, from.AddDays(7), ct);

        var standings  = await football.GetStandingsAsync(ct);
        var eplTeamIds = standings.Select(s => s.TeamId).ToHashSet();

        var matches = eplTeamIds.Count > 0
            ? allMatches.Where(m => eplTeamIds.Contains(m.HomeTeamId) || eplTeamIds.Contains(m.AwayTeamId)).ToList()
            : (IReadOnlyList<MatchDto>)allMatches;

        await bot.SendMessage(msg.Chat.Id,
            MatchesFormatter.FormatUpcomingWithFavorite(matches, favoriteTeamId),
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

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

        await bot.SendMessage(msg.Chat.Id, "🏟 <b>Выбери команду:</b>",
            parseMode:   ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleTeamByNameAsync(Message msg, string teamName, CancellationToken ct)
    {
        var teams = await GetTeamsAsync(ct);
        var found = teams.FirstOrDefault(t => t.Name.Contains(teamName, StringComparison.OrdinalIgnoreCase));

        if (found == default)
        {
            await bot.SendMessage(msg.Chat.Id,
                $"❌ Команда «{teamName}» не найдена. Проверь название.",
                cancellationToken: ct);
            return;
        }

        await SendTeamInfoAsync(msg.Chat.Id, found.Id, found.Name, ct);
    }

    private async Task HandleMyTeamMenuAsync(Message msg, CancellationToken ct)
    {
        await bot.SendChatAction(msg.Chat.Id, ChatAction.Typing, cancellationToken: ct);

        using var scope  = scopeFactory.CreateScope();
        var userRepo     = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var teamRepo     = scope.ServiceProvider.GetRequiredService<TeamRepository>();

        var user        = await userRepo.GetByIdAsync(msg.From!.Id, ct);
        TeamDoc? favTeam = null;
        if (user?.FavoriteTeamId.HasValue == true)
            favTeam = await teamRepo.GetByIdAsync(user.FavoriteTeamId.Value, ct);

        var header = favTeam is not null
            ? $"⭐ Сейчас следишь за: <b>{favTeam.Name}</b>\n\nВыбери другую команду или отпишись:"
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

    private async Task SendTeamInfoAsync(long chatId, int teamId, string teamName, CancellationToken ct)
    {
        await bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

        var squadTask  = football.GetTeamSquadAsync(teamId, ct);
        var recentTask = football.GetRecentMatchesAsync(teamId, 5, ct);
        await Task.WhenAll(squadTask, recentTask);

        await bot.SendMessage(chatId, TeamInfoFormatter.FormatSquad(teamName, squadTask.Result),
            parseMode: ParseMode.Html, cancellationToken: ct);
        await bot.SendMessage(chatId, TeamInfoFormatter.FormatRecentMatches(teamName, teamId, recentTask.Result),
            parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task SetFavoriteTeamAsync(CallbackQuery query, int teamId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var userRepo    = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var teamRepo    = scope.ServiceProvider.GetRequiredService<TeamRepository>();

        await EnsureUserAsync(query.From, ct);

        var user = await userRepo.GetByIdAsync(query.From.Id, ct);
        if (user is null) return;

        // Upsert team if not yet in Firestore
        var team = await teamRepo.GetByIdAsync(teamId, ct);
        if (team is null)
        {
            var standings = await football.GetStandingsAsync(ct);
            var dto       = standings.FirstOrDefault(s => s.TeamId == teamId);
            if (dto is not null)
            {
                team = new TeamDoc { TeamId = dto.TeamId, Name = dto.TeamName, ShortName = dto.ShortName, EmblemUrl = dto.EmblemUrl };
                await teamRepo.UpsertAsync(team, ct);
            }
        }

        var teamName = team?.Name ?? await ResolveTeamNameAsync(teamId, ct);
        user.FavoriteTeamId = teamId;
        await userRepo.UpsertAsync(user, ct);

        await bot.SendMessage(query.Message!.Chat.Id,
            $"🎉 Готово! Теперь ты следишь за <b>{teamName}</b>!\n\n" +
            "Ты будешь получать:\n" +
            "🔔 Напоминание за 15 минут до матча\n" +
            "🏁 Результат сразу после финального свистка\n" +
            "📰 Важные новости команды",
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

    private async Task RemoveFavoriteTeamAsync(CallbackQuery query, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var userRepo    = scope.ServiceProvider.GetRequiredService<UserRepository>();

        var user = await userRepo.GetByIdAsync(query.From.Id, ct);
        if (user is null) return;

        user.FavoriteTeamId = null;
        await userRepo.UpsertAsync(user, ct);

        await bot.SendMessage(query.Message!.Chat.Id,
            "✅ Ты отписан от уведомлений. Можешь выбрать другую команду в любое время.",
            cancellationToken: ct);
    }

    private async Task<List<(int Id, string Name)>> GetTeamsAsync(CancellationToken ct)
    {
        var standings = await football.GetStandingsAsync(ct);
        return standings.OrderBy(s => s.TeamName).Select(s => (s.TeamId, s.TeamName)).ToList();
    }

    private async Task<string> ResolveTeamNameAsync(int teamId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var teamRepo    = scope.ServiceProvider.GetRequiredService<TeamRepository>();

        var team = await teamRepo.GetByIdAsync(teamId, ct);
        if (team is not null) return team.Name;

        var standings = await football.GetStandingsAsync(ct);
        return standings.FirstOrDefault(s => s.TeamId == teamId)?.TeamName ?? $"Команда #{teamId}";
    }

    private async Task EnsureUserAsync(Telegram.Bot.Types.User from, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var userRepo    = scope.ServiceProvider.GetRequiredService<UserRepository>();

        var existing = await userRepo.GetByIdAsync(from.Id, ct);
        if (existing is null)
        {
            await userRepo.UpsertAsync(new UserDoc
            {
                TelegramId   = from.Id,
                Username     = from.Username,
                FirstName    = from.FirstName,
                RegisteredAt = DateTime.UtcNow
            }, ct);
            logger.LogInformation("New user registered: {TelegramId} (@{Username})", from.Id, from.Username);
        }
    }
}
