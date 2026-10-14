using AVAPaperboy.Engine;

namespace AVAPaperboy.Tests;

/// <summary>How the computer paperboy gets on over the week (set PAPERBOY_PROBE to run it, for tuning).</summary>
public sealed class PaperboyProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_delivers()
    {
        if (Environment.GetEnvironmentVariable("PAPERBOY_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "paperboy-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            var crashes = new Dictionary<string, int>();
            var days = new List<string>();
            var last = g.Phase;
            int t = 0;
            for (; t < 60 * 900 && g.State == GameState.Playing; t++)
            {
                g.Tick(g.Pilot!.Decide(g));
                if (g.Phase == Phase.Crash && last != Phase.Crash)
                    crashes[g.Player.CrashedInto ?? "?"] = crashes.GetValueOrDefault(g.Player.CrashedInto ?? "?") + 1;
                if (g.Phase == Phase.DayEnd && last != Phase.DayEnd)
                    days.Add($"{g.DayName[..3]} {g.Reports.Count(r => r == Report.Delivered)}/{g.Reports.Count(r => r != Report.NotSubscriber && r != Report.NewSubscriber)} t{g.TargetsHit}{(g.CourseFinished ? "F" : "")}");
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: {t / 60}s score {g.Score} {g.State} {g.Phase} lives {g.Player.Lives} [{string.Join(", ", days)}] crashes: {string.Join(", ", crashes.Select(kv => kv.Key + " " + kv.Value))}");
        }
    }
}
