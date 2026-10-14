namespace AVAPaperboy.Engine;

/// <summary>
/// The computer paperboy: picks a line along the street that misses everything coming (looking a
/// few seconds ahead at where each thing will be when he gets there), slows down when nothing
/// does, throws when a paper will land in a subscriber's mailbox or at their door, breaks the odd
/// non-subscriber's window when he has papers to spare, and on the training ground lines up the
/// ramps and the targets. Used for the demonstration and the tests.
/// </summary>
public sealed class RiderPilot(int seed)
{
    private readonly Random _rng = new(seed);
    private bool _held;
    private double _lane = 330;
    /// <summary>Houses with a paper already on its way, and until when.</summary>
    private readonly Dictionary<int, double> _pending = [];
    private double _clock;

    private static readonly double[] StreetLanes = [228, 330, 400, 460, 520, 600];
    private static readonly double[] CourseLanes = [230, 290, 350, 410, 470, 530, 590];

    public Controls Decide(GameEngine g)
    {
        var p = g.Player;
        if (g.Phase != Phase.Riding) { _held = false; return default; }
        _clock += Field.Step;
        var lanes = g.InCourse || p.Y > Field.StreetEnd - 150 ? CourseLanes : StreetLanes;
        double best = double.MaxValue, bestCost = 0;
        foreach (double lane in lanes)
        {
            double cost = Danger(g, lane, p.Speed) + Math.Abs(lane - 330) * 0.5 + Math.Abs(lane - _lane) * 0.3 + Bonus(g, lane);
            if (best == double.MaxValue || cost < bestCost) { best = lane; bestCost = cost; }
        }
        _lane = best;
        int dx = Math.Abs(best - p.X) > 5 ? Math.Sign(best - p.X) : 0;
        int dy = 0;
        // Nowhere safe at this speed: try slower. A dog on his heels: pedal hard.
        if (bestCost > 4000 && Danger(g, best, Rider.Slow) < bestCost) dy = 1;
        if (g.Things.Any(t => t.Kind == ThingKind.Dog && !t.Hit && t.Timer is > 0 and < 3 && t.Y < p.Y + 20 && t.Y > p.Y - 120)) dy = -1;
        var target = _held ? null : WantThrow(g);
        bool throwNow = target is not null;
        if (target is { } h0) _pending[h0] = _clock + 1.0;
        _held = throwNow;
        return new Controls(dx, dy, throwNow);
    }

    /// <summary>Where the bike will be across the street after <paramref name="t"/> seconds, steering for the lane.</summary>
    private static double XAt(double x, double lane, double t) => x + Math.Clamp(lane - x, -Rider.SteerSpeed * Math.Max(0, t), Rider.SteerSpeed * Math.Max(0, t));

    private static double Danger(GameEngine g, double lane, double speed)
    {
        var p = g.Player;
        double cost = 0;
        if (p.Safe > 0.5) return 0;
        foreach (var t in g.Things)
        {
            if (t.Gone || t.Y < p.Y - 80 || t.Y > p.Y + 700) continue;
            if (t.Kind is ThingKind.Bundle or ThingKind.Ramp) continue;
            if (t.Hit && t.Kind != ThingKind.Car && t.Kind != ThingKind.Hedge) continue;
            double closing = speed - t.Vy;
            if (t.Kind == ThingKind.Hedge)
            {
                if (t.Y < p.Y) continue;
                double th = (t.Y - p.Y) / speed;
                if (th < 2.5 && XAt(p.X, lane, th) < 262) cost += 6000 / (th + 0.2);
                continue;
            }
            if (closing <= 1) continue;
            double reachY = t.Kind == ThingKind.Car ? Thing.CarLength / 2 + 16 : t.Radius + 14;
            double reachX = (t.Kind == ThingKind.Car ? 26 : t.Radius) + Rider.Radius + 10;
            // Check the bike against it over the time they overlap along the street.
            double t0 = Math.Max(0, (t.Y - reachY - p.Y) / closing), t1 = (t.Y + reachY - p.Y) / closing;
            if (t1 < 0 || t0 > 3) continue;
            for (double s = t0; s <= t1; s += 0.05)
            {
                double bx = XAt(p.X, lane, s), tx = t.X;
                bool hit = t.Kind switch
                {
                    ThingKind.Mower => bx > 196 && bx < 304,
                    ThingKind.Tornado => Math.Abs(bx - 300) < 100 + reachX,
                    ThingKind.Dog => Math.Abs(bx - tx) < reachX + 34,
                    _ => Math.Abs(bx - tx) < reachX,
                };
                if (hit) { cost += 8000 / (s + 0.25); break; }
            }
        }
        foreach (var h in g.Houses)
        {
            if (h.MailboxY < p.Y - 12 || h.MailboxY > p.Y + 500) continue;
            double s = (h.MailboxY - p.Y) / speed;
            if (Math.Abs(XAt(p.X, lane, s) - Field.MailboxX) < 30) cost += 8000 / (s + 0.25);
        }
        return cost;
    }

    /// <summary>The ramps on the training ground are worth lining up with; papers worth picking up.</summary>
    private static double Bonus(GameEngine g, double lane)
    {
        var p = g.Player;
        double b = 0;
        foreach (var t in g.Things)
        {
            if (t.Gone || t.Y < p.Y || t.Y > p.Y + 400) continue;
            if (t.Kind == ThingKind.Ramp && Math.Abs(t.X - lane) < 26) b -= 150;
            if (t.Kind == ThingKind.Bundle && p.Papers < 14 && Math.Abs(t.X - lane) < 20) b -= 400;
        }
        // On the street, stay where the papers reach, and on the pavement for a subscriber coming up.
        if (!g.InCourse && lane > 480) b += 80;
        if (!g.InCourse && p.Papers > 0 && Math.Abs(lane - 330) > 10
            && g.Houses.Any(h => h.Subscriber && h.Delivered == Delivery.None && h.MailboxY - p.Y is > 60 and < 420)) b += 90;
        return b;
    }

    /// <summary>Whether a paper thrown now would land well.</summary>
    private int? WantThrow(GameEngine g)
    {
        var p = g.Player;
        if (p.Papers <= 0 || p.ThrowTime > 0) return null;
        bool Open(House h) => h.Subscriber && h.Delivered == Delivery.None && !(_pending.TryGetValue(h.Index, out double until) && until > _clock);
        double x0 = p.X - 8, z0 = p.Z + Paper.Height;
        double Z(double t) => z0 + Paper.Up * t - Paper.Gravity / 2 * t * t;
        // To a subscriber's mailbox.
        double tm = (x0 - Field.MailboxX) / Paper.Across;
        if (tm > 0)
        {
            double y = p.Y + p.Speed * tm, z = Z(tm);
            if (z is > 0 and < 60)
                foreach (var h in g.Houses)
                    if (Open(h) && Math.Abs(h.MailboxY - y) < 7) return h.Index;
        }
        // Their front door, or a window of a house that doesn't take the paper.
        double tf = (x0 - Field.HouseFront) / Paper.Across;
        if (tf > 0)
        {
            double y = p.Y + p.Speed * tf, z = Z(tf);
            double ym = p.Y + p.Speed * Math.Max(0, tm);
            foreach (var h in g.Houses)
            {
                if (y < h.Near || y > h.Far) continue;
                // Not if it would hit some other mailbox on the way.
                if (tm > 0 && Z(tm) < 66 && g.Houses.Any(o => Math.Abs(o.MailboxY - ym) < 16)) return null;
                if (Open(h) && Math.Abs(h.DoorY - y) < 10 && z > 0 && z < House.DoorHeight - 4) return h.Index;
                bool spare = p.Papers > 4 + g.Houses.Count(o => o.Subscriber && o.Delivered == Delivery.None && o.MailboxY > p.Y);
                if (!h.Subscriber && spare && _rng.Next(4) > 0)
                    for (int i = 0; i < 4; i++)
                    {
                        var (lo, hi) = House.WindowZ[i];
                        if (!h.Broken[i] && Math.Abs(h.Y0 + House.WindowAt[i] - y) < 10 && z > lo + 4 && z < hi - 4) return -1;
                    }
            }
        }
        // Landing on the path to a subscriber's door, from out on the road.
        double tl = (Paper.Up + Math.Sqrt(Paper.Up * Paper.Up + 2 * Paper.Gravity * z0)) / Paper.Gravity;
        double xl = x0 - Paper.Across * tl, yl = p.Y + p.Speed * tl;
        if (xl > Field.HouseFront + 4 && xl < Field.HouseFront + 50 && (tm <= 0 || Z(tm) > 70 || !g.Houses.Any(o => Math.Abs(o.MailboxY - (p.Y + p.Speed * tm)) < 16)))
            foreach (var h in g.Houses)
                if (Open(h) && Math.Abs(h.DoorY - yl) < 18) return h.Index;
        // A dog in the way.
        foreach (var t in g.Things)
        {
            if (t.Kind != ThingKind.Dog || t.Hit) continue;
            double td = (x0 - t.X) / Paper.Across;
            if (td <= 0 || td > 0.6) continue;
            double dy = p.Y + p.Speed * td - t.Y;
            if (Math.Abs(dy) < 14 && Z(td) is > 0 and < 50 && p.Papers > 3) return -1;
        }
        // The training ground's targets.
        if (g.InCourse || p.Y > Field.StreetEnd - 300)
        {
            foreach (var t in g.Things)
            {
                if (t.Kind != ThingKind.Target || t.Hit) continue;
                double tt = (x0 - t.X) / Paper.Across;
                if (tt <= 0) continue;
                if (Math.Abs(p.Y + p.Speed * tt - t.Y) < 8 && Z(tt) is > 0 and < 60) return -1;
            }
        }
        return null;
    }
}
