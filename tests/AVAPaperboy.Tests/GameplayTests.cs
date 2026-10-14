using AVAPaperboy.Engine;
using Engine = AVAPaperboy.Engine;

namespace AVAPaperboy.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avapaperboy-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game under way, the front page skipped, nothing in the street and no traffic unless a test puts it there.</summary>
    private static GameEngine Game(int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.StartGame();
        g.Go();
        g.Clear();
        g.Idle(1);
        return g;
    }

    private static Engine.Controls Press(int dx = 0, int dy = 0, bool toss = false) => new(dx, dy, toss);

    /// <summary>Rides until the bike is at <paramref name="y"/>, holding a line across the street.</summary>
    private static void RideTo(GameEngine g, double y, double x = 330)
    {
        for (int i = 0; i < 60 * 120 && g.Player.Y < y; i++)
            g.Run(1, Press(dx: Math.Abs(g.Player.X - x) > 3 ? Math.Sign(x - g.Player.X) : 0));
    }

    /// <summary>Throws as late as possible so the paper crosses the line at <paramref name="atY"/> when it reaches <paramref name="atX"/>.</summary>
    private static void ThrowFor(GameEngine g, double atX, double atY)
    {
        double t = (g.Player.X - 8 - atX) / Paper.Across;
        RideTo(g, atY - g.Player.Speed * t - 1, g.Player.X);
        g.Run(1, Press(toss: true));
        g.Idle(60);
    }

    private static House Subscriber(GameEngine g, int after = 1) => g.Houses.First(h => h.Subscriber && h.Index >= after && !h.Spooky);
    private static House NonSubscriber(GameEngine g, int after = 1) => g.Houses.First(h => !h.Subscriber && h.Index >= after && !h.Spooky);

    // ───────────────────────────── the street ─────────────────────────────

    [Fact]
    public void Ten_of_the_twenty_houses_take_the_paper_and_one_is_haunted()
    {
        var g = Game();
        Assert.Equal(Field.Houses, g.Houses.Count);
        Assert.Equal(10, g.Subscribers);
        Assert.Single(g.Houses, h => h.Spooky);
        Assert.False(g.Houses.Single(h => h.Spooky).Subscriber);
    }

    [Fact]
    public void The_street_gets_busier_as_the_week_goes_on()
    {
        int Count(int day) => Enumerable.Range(0, 10).Sum(s =>
        {
            var rng = new Random(s);
            var houses = Street.MakeHouses(rng);
            return Street.MakeThings(day, houses, rng).Count(t => t.Kind is not (ThingKind.Hedge or ThingKind.Bundle or ThingKind.Ramp or ThingKind.Target or ThingKind.Barrier));
        });
        Assert.True(Count(7) > Count(1) * 1.3);
        var rng = new Random(3);
        var h = Street.MakeHouses(rng);
        Assert.DoesNotContain(Street.MakeThings(1, h, rng), t => t.Kind == ThingKind.Tornado);
        Assert.Contains(Street.MakeThings(5, h, rng), t => t.Kind == ThingKind.Tornado);
    }

    [Fact]
    public void The_front_page_comes_first_then_the_round()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Phase.DayStart, g.Phase);
        Assert.Equal("MONDAY", g.DayName);
        g.Idle(60 * 5);
        Assert.Equal(Phase.Riding, g.Phase);
        Assert.True(g.Player.Y > 0);
    }

    // ───────────────────────────── riding ─────────────────────────────

    [Fact]
    public void Up_is_faster_down_is_slower_and_left_and_right_steer()
    {
        var g = Game();
        g.Run(60, Press(dy: -1));
        Assert.Equal(Rider.Fast, g.Player.Speed, 1);
        g.Run(60, Press(dy: 1));
        Assert.Equal(Rider.Slow, g.Player.Speed, 1);
        g.Run(60, default);
        Assert.Equal(Rider.Normal, g.Player.Speed, 1);
        double x = g.Player.X;
        g.Run(30, Press(dx: 1));
        Assert.True(g.Player.X > x + 90);
        g.Run(200, Press(dx: 1));
        Assert.Equal(Field.RideMax, g.Player.X);
        g.Run(400, Press(dx: -1));
        Assert.Equal(Field.RideMin, g.Player.X);
    }

    [Fact]
    public void Riding_into_a_hedge_crashes_but_the_gap_by_the_pavement_is_clear()
    {
        var g = Game();
        RideTo(g, Field.Lead + Field.Lot * 2 - 40, 280);
        Assert.Equal(Phase.Riding, g.Phase);
        RideTo(g, Field.Lead + Field.Lot * 3 - 60, 230);
        g.Idle(40);
        Assert.Equal(Phase.Crash, g.Phase);
        Assert.Equal("A HEDGE", g.Player.CrashedInto);
    }

    [Theory]
    [InlineData(ThingKind.Hydrant)]
    [InlineData(ThingKind.Drain)]
    [InlineData(ThingKind.Cone)]
    [InlineData(ThingKind.TrashCan)]
    public void Things_in_the_way_crash_the_bike_and_cost_a_life(ThingKind kind)
    {
        var g = Game();
        g.Put(kind, g.Player.X, g.Player.Y + 200);
        g.Idle(120);
        Assert.Equal(Phase.Crash, g.Phase);
        Assert.Equal(GameEngine.StartingLives - 1, g.Player.Lives);
        g.Idle(60 * 3);
        Assert.Equal(Phase.Riding, g.Phase);
        Assert.True(g.Player.Safe > 0);
    }

    [Fact]
    public void Cars_in_the_road_crash_the_bike()
    {
        var g = Game();
        g.Place(566, 1000);
        g.Put(ThingKind.Car, 566, 1400, -200);
        g.Idle(120);
        Assert.Equal(Phase.Crash, g.Phase);
        Assert.Equal("A CAR", g.Player.CrashedInto);
    }

    [Fact]
    public void Losing_the_last_bike_ends_the_game()
    {
        var g = Game();
        g.Player.Lives = 1;
        g.Score = 0;
        g.Put(ThingKind.Hydrant, g.Player.X, g.Player.Y + 100);
        g.Idle(60 * 4);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.Contains("HANGS UP", g.Headline);
    }

    [Fact]
    public void A_bundle_tops_up_the_bag()
    {
        var g = Game();
        g.Player.Papers = 3;
        g.Put(ThingKind.Bundle, g.Player.X, g.Player.Y + 100);
        g.Idle(60);
        Assert.Equal(13, g.Player.Papers);
        g.Put(ThingKind.Bundle, g.Player.X, g.Player.Y + 100);
        g.Idle(60);
        Assert.Equal(GameEngine.MaxPapers, g.Player.Papers);
    }

    // ───────────────────────────── papers ─────────────────────────────

    [Fact]
    public void A_throw_flies_to_the_left_and_uses_a_paper()
    {
        var g = Game();
        g.Run(1, Press(toss: true));
        Assert.Equal(9, g.Player.Papers);
        var p = Assert.Single(g.Papers);
        Assert.True(p.Vx < 0);
        // Holding the button doesn't throw again.
        g.Run(30, Press(toss: true));
        Assert.Single(g.Papers);
        g.Player.Papers = 0;
        g.Idle(1);
        g.Run(1, Press(toss: true));
        Assert.Single(g.Papers);
    }

    [Fact]
    public void A_paper_in_a_subscribers_mailbox_scores_250()
    {
        var g = Game();
        var h = Subscriber(g);
        ThrowFor(g, Field.MailboxX, h.MailboxY);
        Assert.Equal(Delivery.Mailbox, h.Delivered);
        Assert.Equal(250, g.Score);
    }

    [Fact]
    public void A_paper_at_a_subscribers_door_scores_100()
    {
        var g = Game();
        var h = Subscriber(g);
        // From the road, the paper lands on the path; from closer it hits the door.
        ThrowFor(g, Field.HouseFront, h.DoorY);
        Assert.Equal(Delivery.Doorstep, h.Delivered);
        Assert.Equal(100, g.Score);
    }

    [Fact]
    public void A_subscribers_window_broken_means_they_cancel()
    {
        var g = Game();
        var h = Subscriber(g);
        ThrowFor(g, Field.HouseFront, h.Y0 + House.WindowAt[0]);
        Assert.True(h.Broken[0]);
        Assert.True(h.Damaged);
        Assert.Equal(0, g.Score);
        g.FinishDay();
        Assert.Equal(Report.Damaged, g.Reports[h.Index]);
        Assert.False(h.Subscriber);
    }

    [Fact]
    public void Non_subscribers_windows_and_mailboxes_score()
    {
        var g = Game();
        var h = NonSubscriber(g);
        ThrowFor(g, Field.HouseFront, h.Y0 + House.WindowAt[0]);
        Assert.True(h.Broken[0]);
        Assert.Equal(100, g.Score);
        Assert.False(h.Damaged);
        var o = NonSubscriber(g, h.Index + 1);
        ThrowFor(g, Field.MailboxX, o.MailboxY);
        Assert.True(o.MailboxSmashed);
        Assert.Equal(200, g.Score);
    }

    [Fact]
    public void A_paper_on_the_nose_sends_a_dog_home()
    {
        var g = Game();
        var dog = g.Put(ThingKind.Dog, 250, g.Player.Y + 400);
        // A dog that's had its run and sits at home.
        dog.Timer = 3;
        ThrowFor(g, 250, dog.Y);
        Assert.True(dog.Hit);
        Assert.Equal(200, g.Score);
    }

    [Fact]
    public void Dogs_give_chase_but_a_bike_at_speed_outruns_them()
    {
        var g = Game();
        var dog = g.Put(ThingKind.Dog, 236, g.Player.Y + 300);
        g.Place(400, g.Player.Y);
        g.Run(60 * 2, Press(dy: -1));
        Assert.True(dog.Timer > 0, "the dog should have seen the bike");
        g.Run(60 * 3, Press(dy: -1));
        Assert.Equal(Phase.Riding, g.Phase);
        Assert.True(g.Player.Y - dog.Y > 150);
    }

    [Fact]
    public void A_paper_flies_into_a_tornado_and_is_gone()
    {
        var g = Game();
        g.Run(1, Press(toss: true));
        // Right in the paper's path as it crosses x = 300.
        g.Put(ThingKind.Tornado, 300, g.Player.Y + g.Player.Speed * (g.Player.X - 8 - 300) / Paper.Across);
        g.Idle(60);
        Assert.Empty(g.Papers);
        Assert.Equal(0, g.Score);
    }

    // ───────────────────────────── the end of the day ─────────────────────────────

    [Fact]
    public void Missed_subscribers_cancel_and_a_perfect_day_wins_a_new_one()
    {
        var g = Game();
        var subs = g.Houses.Where(h => h.Subscriber).ToList();
        foreach (var h in subs) h.Delivered = Delivery.Mailbox;
        subs[0].Delivered = Delivery.None;
        g.FinishDay();
        Assert.Equal(Report.Missed, g.Reports[subs[0].Index]);
        Assert.Equal(9, g.Subscribers);
        Assert.False(g.Perfect);
        Assert.Equal(900, g.DayBonus);
        g.Idle(60 * 8);
        Assert.Equal("TUESDAY", g.DayName);
        g.Go();
        foreach (var h in g.Houses.Where(h => h.Subscriber)) h.Delivered = Delivery.Doorstep;
        g.FinishDay();
        Assert.True(g.Perfect);
        Assert.Equal(9 * 200, g.DayBonus);
        Assert.Equal(10, g.Subscribers);
        Assert.Contains(Report.NewSubscriber, g.Reports);
    }

    [Fact]
    public void When_everyone_cancels_the_game_is_over()
    {
        var g = Game();
        g.FinishDay();
        Assert.Equal(0, g.Subscribers);
        g.Idle(60 * 8);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.Contains("CANCELS", g.Headline);
    }

    [Fact]
    public void Sundays_round_finishes_the_week()
    {
        var g = Game();
        for (int day = 1; day <= 7; day++)
        {
            Assert.Equal(day, g.Day);
            foreach (var h in g.Houses.Where(h => h.Subscriber)) h.Delivered = Delivery.Mailbox;
            g.FinishDay();
            g.Idle(60 * 8);
            g.Go();
        }
        Assert.Equal(Phase.WeekDone, g.Phase);
        g.Idle(60 * 7);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.Contains("OF THE WEEK", g.Headline);
    }

    // ───────────────────────────── the training ground ─────────────────────────────

    [Fact]
    public void Ramps_jump_and_the_finish_ends_the_day()
    {
        var g = Game();
        g.Place(400, Field.StreetEnd + 100);
        g.Put(ThingKind.Ramp, 400, Field.StreetEnd + 200);
        g.Idle(50);
        Assert.True(g.Player.Airborne);
        g.Put(ThingKind.Barrier, 400, g.Player.Y + 30);
        g.Idle(5);
        Assert.Equal(Phase.Riding, g.Phase);
        g.Idle(60 * 14);
        Assert.Equal(Phase.DayEnd, g.Phase);
        Assert.True(g.CourseFinished);
    }

    [Fact]
    public void A_target_hit_scores_and_a_spill_on_the_course_costs_no_bike()
    {
        var g = Game();
        g.Place(400, Field.StreetEnd + 100);
        var target = g.Put(ThingKind.Target, 214, Field.StreetEnd + 400);
        ThrowFor(g, 214, target.Y);
        Assert.True(target.Hit);
        Assert.Equal(1, g.TargetsHit);
        g.Put(ThingKind.Barrier, g.Player.X, g.Player.Y + 100);
        g.Idle(60);
        Assert.Equal(Phase.Crash, g.Phase);
        Assert.Equal(GameEngine.StartingLives, g.Player.Lives);
        g.Idle(60 * 3);
        Assert.Equal(Phase.DayEnd, g.Phase);
        Assert.False(g.CourseFinished);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_computer_paperboy_gets_through_monday()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 120 && g.Phase != Phase.DayEnd; t++) g.Tick(g.Pilot!.Decide(g));
            Assert.Equal(Phase.DayEnd, g.Phase);
            Assert.True(g.Reports.Count(r => r == Report.Delivered) >= 6, $"seed {seed}");
        }
    }

    [Fact]
    public void The_attract_mode_plays_itself_then_shows_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 20);
        Assert.True(g.Player.Y > 1000);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 20)) + 2);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void A_good_score_asks_for_initials()
    {
        var g = Game();
        g.Score = 99999;
        g.Player.Lives = 1;
        g.Put(ThingKind.Hydrant, g.Player.X, g.Player.Y + 100);
        g.Idle(60 * 8);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('P');
        g.Initials.Type('A');
        g.Initials.Type('P');
        g.Initials.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(new HighScoreEntry("PAP", 99999, 1), g.HighScores.Entries[0]);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(9, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 60; t++) g.Tick(g.Pilot!.Decide(g));
            return $"{g.Score}:{g.Player.Y:0}:{g.Player.X:0}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void High_scores_persist_in_their_folder()
    {
        string folder = TempFolder();
        var t = HighScoreTable.Load(folder);
        Assert.Equal(HighScoreTable.Size, t.Entries.Count);
        t.Insert("BIG", 123456, 7);
        t.Save();
        Assert.Equal(new HighScoreEntry("BIG", 123456, 7), HighScoreTable.Load(folder).Entries[0]);
    }
}
