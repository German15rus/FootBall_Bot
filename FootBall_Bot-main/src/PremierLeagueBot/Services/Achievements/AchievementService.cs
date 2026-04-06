using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Data.Entities;

namespace PremierLeagueBot.Services.Achievements;

/// <summary>
/// Seeds achievement definitions and grants achievements to users after predictions are scored.
/// </summary>
public sealed class AchievementService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AchievementService> logger)
{
    // ── Achievement definitions ───────────────────────────────────────────────

    public static readonly IReadOnlyList<Achievement> Definitions =
    [
        new Achievement
        {
            Code          = "ROOKIE",
            NameRu        = "Новичок",
            NameEn        = "Rookie",
            DescriptionRu = "Набери свой первый балл",
            DescriptionEn = "Earn your first point",
            Icon          = "🎯"
        },
        new Achievement
        {
            Code          = "FIRST_EXACT",
            NameRu        = "Первое попадание",
            NameEn        = "First Hit",
            DescriptionRu = "Угадай точный счёт впервые",
            DescriptionEn = "Predict an exact score for the first time",
            Icon          = "🎱"
        },
        new Achievement
        {
            Code          = "SNIPER",
            NameRu        = "Снайпер",
            NameEn        = "Sniper",
            DescriptionRu = "Угадай точный счёт в 3 матчах подряд",
            DescriptionEn = "Predict exact scores in 3 consecutive matches",
            Icon          = "🔫"
        },
        new Achievement
        {
            Code          = "PERFECT_WEEK",
            NameRu        = "Идеальный тур",
            NameEn        = "Perfect Round",
            DescriptionRu = "Угадай все счета в туре (мин. 3 матча)",
            DescriptionEn = "Predict all scores in a round (min. 3 matches)",
            Icon          = "🏆"
        },
        new Achievement
        {
            Code          = "EXPERT",
            NameRu        = "Эксперт",
            NameEn        = "Expert",
            DescriptionRu = "Угадай 50 исходов матчей",
            DescriptionEn = "Correctly predict 50 match outcomes",
            Icon          = "📊"
        },
        new Achievement
        {
            Code          = "PROPHET",
            NameRu        = "Провидец",
            NameEn        = "Prophet",
            DescriptionRu = "Угадай 10 точных счетов",
            DescriptionEn = "Predict 10 exact scores",
            Icon          = "🔮"
        },
        new Achievement
        {
            Code          = "CENTURY",
            NameRu        = "Сотня",
            NameEn        = "Century",
            DescriptionRu = "Набери 100 очков",
            DescriptionEn = "Accumulate 100 points",
            Icon          = "💯"
        }
    ];

    // ── Seeding ───────────────────────────────────────────────────────────────

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var def in Definitions)
        {
            if (!await db.Achievements.AnyAsync(a => a.Code == def.Code, ct))
                db.Achievements.Add(def);
        }
        await db.SaveChangesAsync(ct);
    }

    // ── Granting ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks and grants any newly earned achievements for the given user.
    /// Called by PredictionScoringService after scoring predictions.
    /// </summary>
    public async Task CheckAndGrantAsync(long telegramId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var scored = await db.Predictions
            .Where(p => p.TelegramId == telegramId && p.IsScored)
            .OrderBy(p => p.Match.MatchDate)
            .Select(p => new { p.PointsAwarded, p.Match.MatchDate })
            .ToListAsync(ct);

        var earnedList = await db.UserAchievements
            .Where(ua => ua.TelegramId == telegramId)
            .Select(ua => ua.AchievementCode)
            .ToListAsync(ct);
        var earned = earnedList.ToHashSet();

        var toGrant = new List<string>();

        int totalPoints   = scored.Sum(p => p.PointsAwarded ?? 0);
        int totalOutcomes = scored.Count(p => (p.PointsAwarded ?? 0) > 0);
        int exactScores   = scored.Count(p => (p.PointsAwarded ?? 0) >= 3);

        // ROOKIE: first point
        if (!earned.Contains("ROOKIE") && totalPoints > 0)
            toGrant.Add("ROOKIE");

        // FIRST_EXACT: first exact score
        if (!earned.Contains("FIRST_EXACT") && exactScores >= 1)
            toGrant.Add("FIRST_EXACT");

        // EXPERT: 50 correct outcomes
        if (!earned.Contains("EXPERT") && totalOutcomes >= 50)
            toGrant.Add("EXPERT");

        // PROPHET: 10 exact scores
        if (!earned.Contains("PROPHET") && exactScores >= 10)
            toGrant.Add("PROPHET");

        // CENTURY: 100 total points
        if (!earned.Contains("CENTURY") && totalPoints >= 100)
            toGrant.Add("CENTURY");

        // SNIPER: 3 consecutive exact scores (chronological order)
        if (!earned.Contains("SNIPER"))
        {
            int consecutive = 0;
            foreach (var p in scored)
            {
                if ((p.PointsAwarded ?? 0) >= 3) consecutive++;
                else consecutive = 0;
                if (consecutive >= 3) { toGrant.Add("SNIPER"); break; }
            }
        }

        // PERFECT_WEEK: all predictions in an ISO week are exact (≥ 3 that week)
        if (!earned.Contains("PERFECT_WEEK"))
        {
            var byWeek = scored
                .GroupBy(p => System.Globalization.ISOWeek.GetWeekOfYear(p.MatchDate));
            if (byWeek.Any(g => g.Count() >= 3 && g.All(p => (p.PointsAwarded ?? 0) >= 3)))
                toGrant.Add("PERFECT_WEEK");
        }

        foreach (var code in toGrant)
        {
            db.UserAchievements.Add(new UserAchievement
            {
                TelegramId      = telegramId,
                AchievementCode = code,
                EarnedAt        = DateTime.UtcNow
            });
            logger.LogInformation("Achievement {Code} granted to user {Id}", code, telegramId);
        }

        if (toGrant.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
