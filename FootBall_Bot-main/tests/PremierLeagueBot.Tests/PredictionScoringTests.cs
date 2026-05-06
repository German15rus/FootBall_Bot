using PremierLeagueBot.Services.Background;

namespace PremierLeagueBot.Tests;

public class PredictionScoringTests
{
    // ── 3 очка: точный счёт ───────────────────────────────────────────────────

    [Fact]
    public void ExactScore_Returns3Points()
    {
        var points = PredictionScoringService.CalculatePoints(2, 1, 2, 1);
        Assert.Equal(3, points);
    }

    [Fact]
    public void ExactScoreDraw_Returns3Points()
    {
        var points = PredictionScoringService.CalculatePoints(0, 0, 0, 0);
        Assert.Equal(3, points);
    }

    // ── 1 очко: угадан исход, но не точный счёт ───────────────────────────────

    [Fact]
    public void CorrectOutcomeWin_WrongScore_Returns1Point()
    {
        var points = PredictionScoringService.CalculatePoints(2, 0, 3, 1);
        Assert.Equal(1, points);
    }

    [Fact]
    public void CorrectOutcomeDraw_WrongScore_Returns1Point()
    {
        var points = PredictionScoringService.CalculatePoints(1, 1, 2, 2);
        Assert.Equal(1, points);
    }

    [Fact]
    public void CorrectOutcomeLoss_WrongScore_Returns1Point()
    {
        var points = PredictionScoringService.CalculatePoints(0, 1, 0, 3);
        Assert.Equal(1, points);
    }

    // ── 0 очков: неверный исход ───────────────────────────────────────────────

    [Fact]
    public void WrongOutcome_PredictWin_ActualDraw_Returns0()
    {
        var points = PredictionScoringService.CalculatePoints(2, 0, 1, 1);
        Assert.Equal(0, points);
    }

    [Fact]
    public void WrongOutcome_PredictDraw_ActualWin_Returns0()
    {
        var points = PredictionScoringService.CalculatePoints(1, 1, 2, 0);
        Assert.Equal(0, points);
    }

    [Fact]
    public void WrongOutcome_PredictLoss_ActualWin_Returns0()
    {
        var points = PredictionScoringService.CalculatePoints(0, 2, 3, 1);
        Assert.Equal(0, points);
    }
}
