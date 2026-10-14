using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPaperboy.Engine;
using AVAPaperboy.Views;

namespace AVAPaperboy.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set PAPERBOY_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapaperboy-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("PAPERBOY_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPaperboy-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer paperboy ride until something worth a picture is on screen.</summary>
    private static void RideUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 400)
    {
        for (int t = 0; t < 60 * maxSeconds && !done(g); t++)
        {
            g.Tick(g.Pilot!.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
            if (g.State != GameState.Playing) g.StartGame();
        }
        c.Advance(0.016);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(0.6);
        Shot(w, c, "title");
        g.HighScores.Insert("SUN", 61250, 7);
        g.HighScores.Insert("AVA", 42000, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void On_the_round()
    {
        var (w, c, g) = Open(4);
        g.StartGame();
        g.Idle(30);
        c.Advance(0.6);
        Shot(w, c, "monday");
        RideUntil(g, c, g => g.Phase == Phase.Riding && g.Player.Y > 900 && g.Papers.Any(p => !p.Landed && p.X < 270));
        Shot(w, c, "throw");
        RideUntil(g, c, g => g.Phase == Phase.Riding && g.Things.Any(t => t.Kind == ThingKind.Dog && t.Timer is > 0.4 and < 2.5 && !t.Hit && t.Y > g.Player.Y - 30 && t.Y < g.Player.Y + 200 && t.X > 340));
        Shot(w, c, "dog");
        RideUntil(g, c, g => g.Phase == Phase.Riding && g.Things.Count(t => t.Kind == ThingKind.Car && t.Y - g.Player.Y is > 100 and < 700) >= 2 && g.Houses.Any(h => h.Broken.Any(b => b) && h.Far > g.Cam && h.Near < g.Cam + 500));
        Shot(w, c, "traffic");
        RideUntil(g, c, g => g.Phase == Phase.Riding && g.InCourse && g.Player.Airborne && g.Player.Z > 20);
        Shot(w, c, "training");
        RideUntil(g, c, g => g.Phase == Phase.DayEnd && g.PhaseTime > 1);
        Shot(w, c, "report");
        RideUntil(g, c, g => g.Day >= 4 && g.Phase == Phase.Riding && g.Things.Any(t => t.Kind == ThingKind.Tornado && t.Y - g.Player.Y is > 150 and < 600));
        Shot(w, c, "tornado");
        RideUntil(g, c, g => g.Phase == Phase.Crash && g.Player.Down < 1.6);
        Shot(w, c, "crash");
    }
}
