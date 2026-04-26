using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;

namespace PremierLeagueBot.Services.Achievements;

public sealed class AchievementService(
    AchievementRepository achRepo,
    PredictionRepository predRepo,
    ILogger<AchievementService> logger)
{
    public static readonly IReadOnlyList<AchievementDoc> Definitions =
    [
        new AchievementDoc { Code = "ROOKIE",       NameRu = "Новичок",          NameEn = "Rookie",        DescriptionRu = "Набери свой первый балл",               DescriptionEn = "Earn your first point",                           Icon = "🎯" },
        new AchievementDoc { Code = "FIRST_EXACT",  NameRu = "Первое попадание",  NameEn = "First Hit",     DescriptionRu = "Угадай точный счёт впервые",             DescriptionEn = "Predict an exact score for the first time",       Icon = "🎱" },
        new AchievementDoc { Code = "SNIPER",       NameRu = "Снайпер",           NameEn = "Sniper",        DescriptionRu = "Угадай точный счёт в 3 матчах подряд",   DescriptionEn = "Predict exact scores in 3 consecutive matches",   Icon = "🔫" },
        new AchievementDoc { Code = "PERFECT_WEEK", NameRu = "Идеальный тур",     NameEn = "Perfect Round", DescriptionRu = "Угадай все счета в туре (мин. 3 матча)", DescriptionEn = "Predict all scores in a round (min. 3 matches)",  Icon = "🏆" },
        new AchievementDoc { Code = "EXPERT",       NameRu = "Эксперт",           NameEn = "Expert",        DescriptionRu = "Угадай 50 исходов матчей",               DescriptionEn = "Correctly predict 50 match outcomes",             Icon = "📊" },
        new AchievementDoc { Code = "PROPHET",      NameRu = "Провидец",          NameEn = "Prophet",       DescriptionRu = "Угадай 10 точных счетов",                DescriptionEn = "Predict 10 exact scores",                         Icon = "🔮" },
        new AchievementDoc { Code = "CENTURY",      NameRu = "Сотня",             NameEn = "Century",       DescriptionRu = "Набери 100 очков",                       DescriptionEn = "Accumulate 100 points",                           Icon = "💯" },
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var def in Definitions)
        {
            if (!await achRepo.ExistsAsync(def.Code, ct))
                await achRepo.UpsertAchievementAsync(def, ct);
        }
    }

    /// <summary>
    /// Loads this user's scored predictions and grants any newly earned achievements.
    /// Called by PredictionScoringService after scoring.
    /// </summary>
    public async Task CheckAndGrantAsync(long telegramId, CancellationToken ct = default)
    {
        var scored = await predRepo.GetScoredByUserAsync(telegramId, ct);
        await GrantNewAchievementsAsync(telegramId, scored, ct);
    }

    private async Task GrantNewAchievementsAsync(
        long telegramId,
        IReadOnlyList<PredictionDoc> scored,
        CancellationToken ct)
    {
        var earned = await achRepo.GetEarnedCodesAsync(telegramId, ct);

        var totalPoints   = scored.Sum(p => p.PointsAwarded ?? 0);
        var totalOutcomes = scored.Count(p => (p.PointsAwarded ?? 0) > 0);
        var exactScores   = scored.Count(p => (p.PointsAwarded ?? 0) >= 3);

        var toGrant = new List<AchievementDoc>();

        void Consider(string code)
        {
            if (!earned.Contains(code))
            {
                var def = Definitions.FirstOrDefault(d => d.Code == code);
                if (def is not null) toGrant.Add(def);
            }
        }

        if (totalPoints   >  0)  Consider("ROOKIE");
        if (exactScores   >= 1)  Consider("FIRST_EXACT");
        if (totalOutcomes >= 50) Consider("EXPERT");
        if (exactScores   >= 10) Consider("PROPHET");
        if (totalPoints   >= 100) Consider("CENTURY");

        if (!earned.Contains("SNIPER"))
        {
            int consecutive = 0;
            foreach (var p in scored)
            {
                if ((p.PointsAwarded ?? 0) >= 3) consecutive++;
                else consecutive = 0;
                if (consecutive >= 3) { Consider("SNIPER"); break; }
            }
        }

        if (!earned.Contains("PERFECT_WEEK"))
        {
            var byWeek = scored.GroupBy(p => System.Globalization.ISOWeek.GetWeekOfYear(p.MatchDate));
            if (byWeek.Any(g => g.Count() >= 3 && g.All(p => (p.PointsAwarded ?? 0) >= 3)))
                Consider("PERFECT_WEEK");
        }

        foreach (var def in toGrant)
        {
            await achRepo.GrantAsync(new UserAchievementDoc
            {
                TelegramId      = telegramId,
                AchievementCode = def.Code,
                EarnedAt        = DateTime.UtcNow,
                NameRu          = def.NameRu,
                NameEn          = def.NameEn,
                DescriptionRu   = def.DescriptionRu,
                DescriptionEn   = def.DescriptionEn,
                Icon            = def.Icon
            }, ct);
            logger.LogInformation("Achievement {Code} granted to user {Id}", def.Code, telegramId);
        }
    }
}
