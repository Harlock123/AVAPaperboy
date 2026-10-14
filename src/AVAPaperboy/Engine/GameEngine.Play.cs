namespace AVAPaperboy.Engine;

/// <summary>How each house did on the day's delivery report.</summary>
public enum Report { NotSubscriber, Delivered, Missed, Damaged, NewSubscriber }

public sealed partial class GameEngine
{
    public const double Gravity = 900;
    public const int MaxPapers = 20;

    public Rider Player { get; private set; } = new();
    public List<House> Houses { get; private set; } = [];
    public List<Thing> Things { get; private set; } = [];
    public List<Paper> Papers { get; } = [];
    public int Day { get; private set; } = 1;
    public string DayName => Street.Days[Math.Clamp(Day - 1, 0, 6)];
    public int Score { get; internal set; }
    /// <summary>How far along the street the view is.</summary>
    public double Cam { get; private set; }
    public string Headline { get; private set; } = "";
    public int DayBonus { get; private set; }
    public bool Perfect { get; private set; }
    public Report[] Reports { get; } = new Report[Field.Houses];
    public int TargetsHit { get; private set; }
    public bool CourseFinished { get; private set; }
    public bool InCourse => Player.Y > Field.StreetEnd;
    public int Subscribers => Houses.Count(h => h.Subscriber);
    /// <summary>The computer paperboy, for the demonstration (and the tests).</summary>
    public RiderPilot? Pilot { get; private set; }

    private double _carTimer, _skaterTimer;

    /// <summary>Test hook: no cars or skaters turn up.</summary>
    internal bool Calm { get; set; }

    private double Rand(double lo, double hi) => lo + _rng.NextDouble() * (hi - lo);

    private void NewGame(bool attract)
    {
        _rng = new Random(_seed + (int)(StateTime * 997) + (attract ? 1 : 0));
        Houses = Street.MakeHouses(_rng);
        Score = 0;
        Player = new Rider { Lives = StartingLives };
        Pilot = new RiderPilot(_rng.Next());
        Effects.Clear();
        StartDay(1);
    }

    private void StartDay(int day)
    {
        Day = day;
        foreach (var h in Houses)
        {
            h.Delivered = Delivery.None;
            h.Damaged = false;
            h.MailboxSmashed = false;
            Array.Clear(h.Broken);
        }
        Street.Paint(Houses, _rng);
        Things = Street.MakeThings(day, Houses, _rng);
        Papers.Clear();
        Player.X = 330;
        Player.Y = 0;
        Player.Z = Player.Vz = 0;
        Player.Speed = Rider.Normal;
        Player.Papers = 10;
        Player.Down = 0;
        Player.Safe = 0;
        Cam = Player.Y - 150;
        TargetsHit = 0;
        CourseFinished = false;
        _carTimer = 3;
        _skaterTimer = 6;
        Headline = Street.Headlines[_rng.Next(Street.Headlines.Length)];
        SetPhase(Phase.DayStart);
    }

    private void AddScore(int points, double x, double y, double z = 30)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Z = z, Text = points.ToString(), Life = 1, Colour = 0xFFFFE14A });
    }

    // ───────────────────────────── the step ─────────────────────────────

    private void Simulate(double dt, Controls c, bool startPressed)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.DayStart:
                if (PhaseTime > 4.5 || startPressed && PhaseTime > 0.4)
                {
                    SetPhase(Phase.Riding);
                    Music(true);
                    Play(Sfx.Bell);
                }
                return;
            case Phase.Riding:
                Ride(c, dt);
                break;
            case Phase.Crash:
                Player.Down -= dt;
                if (Player.Down <= 0) GetUp();
                break;
            case Phase.DayEnd:
                if (PhaseTime > 7 || startPressed && PhaseTime > 1) AfterReport();
                return;
            case Phase.WeekDone:
                if (PhaseTime > 6 || startPressed && PhaseTime > 1) GameOver("PAPERBOY OF THE WEEK!");
                return;
        }
        if (State is not (GameState.Playing or GameState.Attract)) return;
        MoveThings(dt);
        MovePapers(dt);
        if (Phase == Phase.Riding && !Calm && Player.Y < Field.StreetEnd - 300) Traffic(dt);
        Things.RemoveAll(t => t.Gone || t.Y < Cam - 600 && t.Moves);
    }

    // ───────────────────────────── the bike ─────────────────────────────

    private void Ride(Controls c, double dt)
    {
        var p = Player;
        double want = c.Dy < 0 ? Rider.Fast : c.Dy > 0 ? Rider.Slow : Rider.Normal;
        double rate = want < p.Speed ? 420 : 240;
        p.Speed += Math.Clamp(want - p.Speed, -rate * dt, rate * dt);
        p.Lean = c.Dx;
        p.X = Math.Clamp(p.X + c.Dx * Rider.SteerSpeed * (p.Airborne ? 0.5 : 1) * dt, Field.RideMin, Field.RideMax);
        double prevY = p.Y;
        p.Y += p.Speed * dt;
        p.Pedal += p.Speed * dt * 0.06;
        if (p.Safe > 0) p.Safe -= dt;
        if (p.ThrowTime > 0) p.ThrowTime -= dt;
        if (p.Airborne)
        {
            p.Vz -= Gravity * dt;
            p.Z += p.Vz * dt;
            if (p.Z <= 0)
            {
                p.Z = 0;
                p.Vz = 0;
                Play(Sfx.Land);
            }
        }
        if (c.Throw && !p.ThrowHeld && p.ThrowTime <= 0)
        {
            if (p.Papers > 0) Throw();
            p.ThrowTime = 0.22;
        }
        p.ThrowHeld = c.Throw;
        Cam = p.Y - 150;
        Collide(prevY);
        if (Phase == Phase.Riding && p.Y >= Field.StreetEnd + Field.CourseLength - 60)
        {
            CourseFinished = true;
            AddScore(1000, p.X, p.Y, 60);
            Play(Sfx.Finish);
            EndDay();
        }
    }

    private void Throw()
    {
        var p = Player;
        p.Papers--;
        Papers.Add(new Paper { X = p.X - 8, Y = p.Y, Z = p.Z + Paper.Height, Vx = -Paper.Across, Vy = p.Speed, Vz = Paper.Up });
        Play(Sfx.Throw);
    }

    private void Collide(double prevY)
    {
        var p = Player;
        bool low = p.Z < 18;
        foreach (var t in Things)
        {
            if (t.Gone || Math.Abs(t.Y - p.Y) > 120) continue;
            switch (t.Kind)
            {
                case ThingKind.Hedge:
                    if (low && p.X < 252 && prevY < t.Y && p.Y >= t.Y && p.Safe <= 0) { Crash("A HEDGE"); return; }
                    continue;
                case ThingKind.Bundle:
                    if (Math.Abs(t.X - p.X) < 24 && Math.Abs(t.Y - p.Y) < 24 && p.Z < 30)
                    {
                        t.Gone = true;
                        p.Papers = Math.Min(MaxPapers, p.Papers + 10);
                        Play(Sfx.Pickup);
                        Effects.Add(new Effect { Kind = EffectKind.Word, X = t.X, Y = t.Y, Z = 40, Text = "+10 PAPERS", Life = 1.2, Colour = 0xFFFFFFFF });
                    }
                    continue;
                case ThingKind.Ramp:
                    if (low && Math.Abs(t.X - p.X) < t.Radius && p.Y >= t.Y - 6 && p.Y <= t.Y + 30 && !p.Airborne)
                    {
                        p.Vz = 260 + p.Speed * 0.5;
                        p.Z = 1;
                        AddScore(100, p.X, p.Y, 50);
                        Play(Sfx.Ramp);
                    }
                    continue;
            }
            if (t.Hit || p.Safe > 0 || !low) continue;
            double reachY = t.Kind == ThingKind.Car ? Thing.CarLength / 2 + 6 : t.Radius + Rider.Radius * 0.6;
            double reachX = t.Kind == ThingKind.Car ? 26 : t.Radius + Rider.Radius * 0.6;
            if (Math.Abs(t.X - p.X) < reachX && Math.Abs(t.Y - p.Y) < reachY)
            {
                Crash(NameOf(t.Kind));
                return;
            }
        }
        if (!low || p.Safe > 0) return;
        foreach (var h in Houses)
            if (Math.Abs(h.MailboxY - p.Y) < 10 && Math.Abs(Field.MailboxX - p.X) < 12) { Crash("A MAILBOX"); return; }
    }

    public static string NameOf(ThingKind k) => k switch
    {
        ThingKind.Hydrant => "A FIRE HYDRANT", ThingKind.TrashCan => "A DUSTBIN", ThingKind.Drain => "A DRAIN",
        ThingKind.Gnome => "A GARDEN GNOME", ThingKind.Tombstone => "A GRAVESTONE", ThingKind.Cone => "THE ROADWORKS",
        ThingKind.Car => "A CAR", ThingKind.Dog => "A DOG", ThingKind.Mower => "A LAWNMOWER", ThingKind.Skater => "A SKATEBOARDER",
        ThingKind.Dancer => "A BREAKDANCER", ThingKind.Tornado => "A TORNADO", ThingKind.Barrier => "A BARRIER",
        ThingKind.Target => "A TARGET", _ => "SOMETHING",
    };

    private void Crash(string what)
    {
        var p = Player;
        p.CrashedInto = what;
        p.Down = 2.2;
        p.Speed = 0;
        p.Z = p.Vz = 0;
        Play(Sfx.Crash);
        SetPhase(Phase.Crash);
        if (!InCourse) p.Lives--;
        Say(InCourse ? "OFF THE COURSE!" : $"CRASHED INTO {what}", 2.2);
    }

    /// <summary>Back on the bike: a clear way ahead and a moment's grace. On the training ground, a crash ends the day.</summary>
    private void GetUp()
    {
        var p = Player;
        if (InCourse) { EndDay(); return; }
        if (p.Lives <= 0) { GameOver("PAPERBOY HANGS UP HIS BAG"); return; }
        foreach (var t in Things.Where(t => t.Moves && t.Y > p.Y - 100 && t.Y < p.Y + 500)) t.Gone = true;
        p.X = 330;
        // Clear of whatever he hit.
        if (Things.Any(t => !t.Hit && !t.Gone && t.Kind != ThingKind.Hedge && Math.Abs(t.Y - p.Y) < 40 && Math.Abs(t.X - p.X) < 30)) p.X = 470;
        p.Speed = Rider.Slow;
        p.Safe = 1.6;
        SetPhase(Phase.Riding);
    }

    // ───────────────────────────── papers ─────────────────────────────

    private House? HouseAt(double y) => Houses.FirstOrDefault(h => y >= h.Near && y <= h.Far);

    private void MovePapers(double dt)
    {
        foreach (var pp in Papers)
        {
            pp.Age += dt;
            if (pp.Landed)
            {
                if (pp.Age > 6 || pp.Y < Cam - 200) pp.Done = true;
                continue;
            }
            double px = pp.X;
            pp.X += pp.Vx * dt;
            pp.Y += pp.Vy * dt;
            pp.Vz -= Paper.Gravity * dt;
            pp.Z += pp.Vz * dt;
            pp.Spin += dt * 14;
            if (HitMailbox(pp, px) || HitThing(pp) || HitHouse(pp, px)) continue;
            if (pp.Z <= 0) Land(pp);
        }
        Papers.RemoveAll(pp => pp.Done);
    }

    private bool HitMailbox(Paper pp, double px)
    {
        if (pp.Z > 66 || !(px >= Field.MailboxX - 8 && pp.X <= Field.MailboxX + 8)) return false;
        var h = Houses.FirstOrDefault(h => Math.Abs(h.MailboxY - pp.Y) < 16);
        if (h is null) return false;
        pp.Done = true;
        if (h.Subscriber)
        {
            if (h.Delivered == Delivery.None)
            {
                h.Delivered = Delivery.Mailbox;
                AddScore(250, Field.MailboxX, h.MailboxY, 60);
            }
            Play(Sfx.Mailbox);
        }
        else if (!h.MailboxSmashed)
        {
            h.MailboxSmashed = true;
            AddScore(100, Field.MailboxX, h.MailboxY, 60);
            Play(Sfx.Thud);
        }
        else Play(Sfx.Thud);
        return true;
    }

    private bool HitThing(Paper pp)
    {
        if (pp.Z > 70) return false;
        foreach (var t in Things)
        {
            if (t.Gone || t.Kind is ThingKind.Hedge or ThingKind.Bundle or ThingKind.Ramp or ThingKind.Drain) continue;
            double ry = t.Kind == ThingKind.Car ? Thing.CarLength / 2 : t.Radius + 6;
            if (Math.Abs(t.X - pp.X) > t.Radius + 6 || Math.Abs(t.Y - pp.Y) > ry) continue;
            if (t.Hit && t.Kind != ThingKind.Car) continue;
            pp.Done = true;
            switch (t.Kind)
            {
                case ThingKind.Tornado:
                    // Whisked away.
                    Play(Sfx.Wind);
                    return true;
                case ThingKind.Car:
                    AddScore(t.Points, t.X, t.Y, 40);
                    Play(Sfx.Thud);
                    return true;
                case ThingKind.Target:
                    TargetsHit++;
                    Play(Sfx.Target);
                    break;
                case ThingKind.Dog:
                    Play(Sfx.Yelp);
                    break;
                default:
                    Play(Sfx.Thud);
                    break;
            }
            t.Hit = true;
            t.Timer = 0;
            if (t.Points > 0) AddScore(t.Points, t.X, t.Y, 40);
            return true;
        }
        return false;
    }

    private bool HitHouse(Paper pp, double px)
    {
        if (!(px > Field.HouseFront && pp.X <= Field.HouseFront) || HouseAt(pp.Y) is not { } h) return false;
        double along = pp.Y - h.Y0;
        for (int i = 0; i < 4; i++)
        {
            var (lo, hi) = House.WindowZ[i];
            if (Math.Abs(along - House.WindowAt[i]) > House.WindowHalf || pp.Z < lo || pp.Z > hi) continue;
            pp.Done = true;
            if (h.Broken[i]) { Play(Sfx.Thud); return true; }
            h.Broken[i] = true;
            Play(Sfx.Glass);
            Effects.Add(new Effect { Kind = EffectKind.Glass, X = Field.HouseFront, Y = pp.Y, Z = pp.Z, Life = 0.6 });
            if (h.Subscriber)
            {
                h.Damaged = true;
                Effects.Add(new Effect { Kind = EffectKind.Word, X = Field.HouseFront + 30, Y = pp.Y, Z = pp.Z + 30, Text = "HEY!", Life = 1.2, Colour = 0xFFFF5A4A });
            }
            else AddScore(100, Field.HouseFront, pp.Y, pp.Z);
            return true;
        }
        if (Math.Abs(pp.Y - h.DoorY) < House.DoorHalf && pp.Z < House.DoorHeight)
        {
            pp.Done = true;
            Deliver(h, Delivery.Doorstep);
            return true;
        }
        // The wall: it drops at the foot of it.
        Play(Sfx.Thud);
        pp.X = Field.HouseFront + 2;
        pp.Vx = 30;
        pp.Vy *= 0.3;
        if (pp.Vz > 0) pp.Vz = 0;
        return false;
    }

    private void Deliver(House h, Delivery how)
    {
        if (h.Subscriber)
        {
            if (h.Delivered == Delivery.None)
            {
                h.Delivered = how;
                AddScore(how == Delivery.Mailbox ? 250 : 100, Field.HouseFront + 20, h.DoorY, 40);
            }
            Play(Sfx.Door);
        }
        else Play(Sfx.Thud);
    }

    private void Land(Paper pp)
    {
        pp.Z = 0;
        pp.Landed = true;
        pp.Age = 0;
        // On the step or the path up to the door counts as delivered.
        if (HouseAt(pp.Y) is { } h && Math.Abs(pp.Y - h.DoorY) < 30 && pp.X >= Field.HouseFront && pp.X < Field.HouseFront + 60)
            Deliver(h, Delivery.Doorstep);
    }

    // ───────────────────────────── the neighbourhood ─────────────────────────────

    private void Traffic(double dt)
    {
        _carTimer -= dt;
        _skaterTimer -= dt;
        if (_carTimer <= 0)
        {
            _carTimer = Rand(3.0, 5.5) / (1 + Day * 0.1);
            bool oncoming = _rng.Next(3) > 0;
            uint[] colours = [0xFFD03A3A, 0xFF3A6AD0, 0xFFE0C040, 0xFF40A060, 0xFFE8E8E8, 0xFF8A4AC0];
            Things.Add(new Thing
            {
                Kind = ThingKind.Car, X = oncoming ? 566 : 432, Y = Player.Y + 1150,
                Vy = oncoming ? -(150 + Day * 15) : 70, Colour = colours[_rng.Next(colours.Length)], Facing = oncoming ? -1 : 1,
            });
        }
        if (_skaterTimer <= 0)
        {
            _skaterTimer = Rand(7, 11) / (1 + Day * 0.12);
            Things.Add(new Thing { Kind = ThingKind.Skater, X = 334, Y = Player.Y + 1100, Vy = -(100 + Day * 8), Facing = -1 });
        }
    }

    private void MoveThings(double dt)
    {
        var p = Player;
        foreach (var t in Things)
        {
            if (t.Gone) continue;
            t.Age += dt;
            if (t.Y > Cam + 1400 && t.Kind != ThingKind.Car && t.Kind != ThingKind.Skater) continue;
            switch (t.Kind)
            {
                case ThingKind.Car:
                    t.Y += t.Vy * dt;
                    if (t.Timer == 0 && Math.Abs(t.X - p.X) < 50 && t.Y - p.Y is > 0 and < 320)
                    {
                        t.Timer = 1;
                        Play(Sfx.Horn);
                    }
                    break;
                case ThingKind.Skater:
                    if (!t.Hit) t.Y += t.Vy * dt;
                    break;
                case ThingKind.Mower:
                    if (t.Hit) break;
                    if (t.Vx == 0) t.Vx = 55;
                    t.X += t.Vx * dt;
                    if (t.X > 292 || t.X < 206) { t.Vx = -t.Vx; t.X = Math.Clamp(t.X, 206, 292); }
                    t.Facing = Math.Sign(t.Vx);
                    break;
                case ThingKind.Dog:
                    MoveDog(t, dt);
                    break;
                case ThingKind.Tornado:
                    t.X = 300 + Math.Sin(t.Age * 0.8) * 100;
                    t.Y -= 30 * dt;
                    if (t.Timer == 0 && t.Y - p.Y < 600) { t.Timer = 1; Play(Sfx.Wind); }
                    break;
            }
        }
    }

    /// <summary>Dogs sit on their lawns until the bike comes by, then give chase for a few seconds. A paper on the nose sends them home.</summary>
    private void MoveDog(Thing d, double dt)
    {
        var p = Player;
        if (d.Hit)
        {
            if (d.X > 120) d.X -= 160 * dt;
            return;
        }
        bool near = p.Y > d.HomeY - 240 && p.Y < d.HomeY + 200;
        if (d.Timer < 3 && near && Phase == Phase.Riding)
        {
            if (d.Timer == 0) Play(Sfx.Bark);
            d.Timer += dt;
            var to = new Vec(p.X - d.X, p.Y - d.Y);
            double len = Math.Max(1, to.Length);
            double speed = 112 + Day * 5;
            d.X = Math.Min(380, d.X + to.X / len * speed * dt);
            d.Y += to.Y / len * speed * dt;
            d.Facing = to.X >= 0 ? 1 : -1;
        }
        else if (d.Timer > 0)
        {
            // Bored: trots home.
            d.Timer = Math.Max(d.Timer, 3);
            var to = new Vec(d.HomeX - d.X, d.HomeY - d.Y);
            double len = to.Length;
            if (len > 2) { d.X += to.X / len * 80 * dt; d.Y += to.Y / len * 80 * dt; }
        }
    }

    // ───────────────────────────── the end of the day ─────────────────────────────

    private void EndDay()
    {
        Music(false);
        int delivered = 0, subs = 0;
        foreach (var h in Houses)
        {
            if (!h.Subscriber) { Reports[h.Index] = Report.NotSubscriber; continue; }
            subs++;
            if (h.Damaged) Reports[h.Index] = Report.Damaged;
            else if (h.Delivered == Delivery.None) Reports[h.Index] = Report.Missed;
            else { Reports[h.Index] = Report.Delivered; delivered++; }
        }
        Perfect = subs > 0 && delivered == subs;
        DayBonus = delivered * 100 * (Perfect ? 2 : 1);
        Score += DayBonus;
        foreach (var h in Houses.Where(h => Reports[h.Index] is Report.Missed or Report.Damaged)) h.Subscriber = false;
        if (Perfect && Houses.Where(h => !h.Subscriber && !h.Spooky).ToList() is { Count: > 0 } free)
        {
            var h = free[_rng.Next(free.Count)];
            h.Subscriber = true;
            Reports[h.Index] = Report.NewSubscriber;
        }
        Play(Reports.Any(r => r is Report.Missed or Report.Damaged) ? Sfx.Cancel : Sfx.DayEnd);
        if (Perfect) Play(Sfx.Subscribe);
        Papers.Clear();
        SetPhase(Phase.DayEnd);
    }

    private void AfterReport()
    {
        if (Subscribers == 0) { GameOver("EVERY SUBSCRIBER CANCELS!"); return; }
        if (Day >= 7)
        {
            Score += 5000;
            SetPhase(Phase.WeekDone);
            Play(Sfx.Victory);
            return;
        }
        StartDay(Day + 1);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    /// <summary>Test hook: skip the front page.</summary>
    internal void Go()
    {
        if (Phase != Phase.DayStart) return;
        SetPhase(Phase.Riding);
    }

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    internal Thing Put(ThingKind kind, double x, double y, double vy = 0)
    {
        var t = new Thing { Kind = kind, X = x, Y = y, HomeX = x, HomeY = y, Vy = vy, Colour = 0xFFD03A3A };
        Things.Add(t);
        return t;
    }

    /// <summary>Test hook: clears the street of everything but the hedges.</summary>
    internal void Clear() => Things.RemoveAll(t => t.Kind != ThingKind.Hedge);

    /// <summary>Test hook: puts the bike at a place on the street.</summary>
    internal void Place(double x, double y)
    {
        Player.X = x;
        Player.Y = y;
        Cam = y - 150;
    }

    /// <summary>Test hook: ends the day now.</summary>
    internal void FinishDay() => EndDay();
}
