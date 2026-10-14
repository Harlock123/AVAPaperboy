using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPaperboy.Engine;

namespace AVAPaperboy.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Dictionary<(uint, double), Pen> RoundCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPaperboy/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width);
        return p;
    }

    /// <summary>A thick pen with round ends, for limbs.</summary>
    private static Pen Limb(uint argb, double width)
    {
        if (!RoundCache.TryGetValue((argb, width), out var p))
            RoundCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Green = 0xFF70E070, Ink = 0xFF1C1A18, Newsprint = 0xFFF2EDE0;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xA0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, 1.2) : null, g);
    }

    private double _cam;

    /// <summary>A point of the street on screen.</summary>
    private Point P(double x, double y, double z = 0)
    {
        var (sx, sy) = Field.Project(x, y, z, _cam);
        return new Point(sx, sy);
    }

    /// <summary>A flat patch of ground, x0..x1 across and y0..y1 along.</summary>
    private void Patch(DrawingContext dc, uint fill, double x0, double x1, double y0, double y1, double z = 0) =>
        Poly(dc, fill, null, P(x0, y0, z), P(x1, y0, z), P(x1, y1, z), P(x0, y1, z));

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        _cam = g.Cam;
        bool street = g.State is GameState.Playing or GameState.Paused or GameState.Attract or GameState.GameOver
                      && g.Phase is Phase.Riding or Phase.Crash;
        DrawGround(dc);
        DrawScene(dc);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawFrontPage(dc, "EXTRA! EXTRA!", g.Headline, $"FINAL SCORE {g.Score}"); break;
            default:
                if (g.Phase == Phase.DayStart) DrawDayStart(dc);
                else if (g.Phase == Phase.DayEnd) DrawReport(dc);
                else if (g.Phase == Phase.WeekDone) DrawFrontPage(dc, "SUNDAY SPECIAL", "PAPERBOY OF THE WEEK!", "EVERY ROUND DELIVERED - 5000 BONUS");
                else if (street) DrawMessages(dc);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.Attract && g.Phase is Phase.Riding or Phase.Crash)
        {
            Text(dc, "DEMONSTRATION", new Point(Field.Width / 2, 110), 44, Yellow, center: true);
            Text(dc, "PRESS ENTER TO PLAY", new Point(Field.Width / 2, 146), 28, White, center: true);
        }
    }

    // ───────────────────────────── the ground ─────────────────────────────

    private void DrawGround(DrawingContext dc)
    {
        var g = Engine;
        double y0 = _cam - 350, y1 = _cam + 1500;
        dc.FillRectangle(Brush(0xFF4E8A40), new Rect(0, 0, Field.Width, Field.Height));
        // The far side: a park with trees, then the far pavement and the road.
        Patch(dc, 0xFF5A9A48, Field.FarKerb, 1800, y0, y1);
        Patch(dc, 0xFFB8B8B0, Field.FarKerb, Field.FarKerb + 50, y0, Math.Min(y1, Field.StreetEnd));
        Patch(dc, 0xFF4A4A54, Field.Kerb, Field.FarKerb, y0, Math.Min(y1, Field.StreetEnd));
        for (double y = Math.Floor(y0 / 80) * 80; y < Math.Min(y1, Field.StreetEnd); y += 80)
            Patch(dc, 0xFFE8D070, Field.RoadCentre - 3, Field.RoadCentre + 3, y, y + 40);
        // The pavement, with its joints.
        Patch(dc, 0xFFCAC6BA, Field.Pavement, Field.Kerb, y0, Math.Min(y1, Field.StreetEnd));
        for (double y = Math.Floor(y0 / 45) * 45; y < Math.Min(y1, Field.StreetEnd); y += 45)
            dc.DrawLine(Pen(0xFFA8A498, 1), P(Field.Pavement, y), P(Field.Kerb, y));
        dc.DrawLine(Pen(0xFF8A8678, 3), P(Field.Kerb, y0), P(Field.Kerb, Math.Min(y1, Field.StreetEnd)));
        // Back gardens, and each house's lawn: lush where they take the paper, parched where they don't.
        Patch(dc, 0xFF3E7234, -1200, 0, y0, Math.Min(y1, Field.StreetEnd));
        foreach (var h in g.Houses)
        {
            if (h.Y0 > y1 || h.Y0 + Field.Lot < y0) continue;
            uint lawn = h.Spooky ? 0xFF6A6A3A : h.Subscriber ? 0xFF5FAE4A : 0xFF8E9A4A;
            Patch(dc, lawn, 0, Field.Pavement, h.Y0, h.Y0 + Field.Lot);
            // The drive and the path to the door.
            Patch(dc, 0xFF9A968C, 20, Field.Pavement, h.Y0 + 252, h.Y0 + 292);
            Patch(dc, 0xFFD8CCB0, Field.HouseFront, Field.Pavement, h.DoorY - 12, h.DoorY + 12);
            Patch(dc, 0xFFB0A898, Field.Pavement, Field.Kerb, h.Y0 + 252, h.Y0 + 292);
            if (h.Subscriber) Patch(dc, 0xFF8A3A2A, Field.HouseFront, Field.HouseFront + 14, h.DoorY - 14, h.DoorY + 14);
        }
        // Before the first house, grass.
        if (y0 < Field.Lead) Patch(dc, 0xFF5A9A48, -1200, Field.Pavement, y0, Field.Lead);
        // The training ground: a dirt track with a fence.
        if (y1 > Field.StreetEnd)
        {
            Patch(dc, 0xFF3E7234, -1200, 1800, Field.StreetEnd, y1);
            Patch(dc, 0xFFC8A070, 180, 650, Field.StreetEnd, Field.StreetEnd + Field.CourseLength + 400);
            for (double y = Field.StreetEnd; y < Field.StreetEnd + Field.CourseLength + 400; y += 60)
                for (int side = 0; side < 2; side++)
                {
                    double x = side == 0 ? 180 : 650;
                    dc.DrawLine(Pen(0xFFF0F0F0, 3), P(x, y), P(x, y, 18));
                    dc.DrawLine(Pen(0xFFF0F0F0, 2), P(x, y, 16), P(x, y + 60, 16));
                }
            double fy = Field.StreetEnd + Field.CourseLength - 60;
            for (int i = 0; i < 16; i++)
                for (int j = 0; j < 2; j++)
                    Patch(dc, (i + j) % 2 == 0 ? 0xFF101010 : 0xFFF8F8F8, 180 + i * 29.4, 180 + (i + 1) * 29.4, fy + j * 12, fy + (j + 1) * 12);
        }
        // Trees in the park across the road.
        for (int i = (int)Math.Floor(y0 / 140); i < y1 / 140; i++)
        {
            uint h = Hash(i, 3, 17);
            double x = 760 + h % 260, y = i * 140 + (h >> 9) % 80;
            dc.DrawEllipse(Brush(0x40000000), null, P(x + 20, y - 10), 26, 12);
        }
    }

    // ───────────────────────────── the street, far to near ─────────────────────────────

    private void DrawScene(DrawingContext dc)
    {
        var g = Engine;
        var items = new List<(double Key, Action Draw)>();
        double lo = _cam - 300, hi = _cam + 1500;
        foreach (var h in g.Houses)
            if (h.Far > lo && h.Near < hi)
            {
                items.Add((h.Y0 + Field.Lot - 1, () => DrawHouse(dc, h)));
                items.Add((h.MailboxY, () => DrawMailbox(dc, h)));
            }
        foreach (var t in g.Things)
            if (!t.Gone && t.Y > lo && t.Y < hi) items.Add((t.Y, () => DrawThing(dc, t)));
        foreach (var p in g.Papers) items.Add((p.Y, () => DrawPaper(dc, p)));
        if (g.Phase is Phase.Riding or Phase.Crash) items.Add((g.Player.Y, () => DrawRider(dc, g.Player)));
        // Trees across the road.
        for (int i = (int)Math.Floor(lo / 140); i < hi / 140; i++)
        {
            uint hsh = Hash(i, 3, 17);
            double x = 760 + hsh % 260, y = i * 140 + (hsh >> 9) % 80;
            items.Add((y, () => DrawTree(dc, x, y, hsh)));
        }
        if (Field.StreetEnd > lo && Field.StreetEnd < hi) items.Add((Field.StreetEnd + 20, () => DrawArch(dc, Field.StreetEnd + 20, "TRAINING GROUND")));
        double finish = Field.StreetEnd + Field.CourseLength - 54;
        if (finish > lo && finish < hi) items.Add((finish, () => DrawArch(dc, finish, "FINISH")));
        foreach (var (_, draw) in items.OrderByDescending(i => i.Key)) draw();
        foreach (var e in g.Effects) DrawEffect(dc, e);
    }

    private void DrawTree(DrawingContext dc, double x, double y, uint h)
    {
        var foot = P(x, y);
        dc.DrawLine(Limb(0xFF6A4A2A, 8), foot, P(x, y, 50));
        uint leaf = (h & 1) == 0 ? 0xFF2E7A2E : 0xFF3A8A3A;
        dc.DrawEllipse(Brush(leaf), Pen(0xFF1E5A1E, 1.5), P(x, y, 84), 34, 36);
        dc.DrawEllipse(Brush(Shade(leaf, 1.25)), null, P(x - 8, y, 94), 16, 14);
    }

    private void DrawArch(DrawingContext dc, double y, string words)
    {
        foreach (double x in new[] { 190.0, 640 })
            dc.DrawLine(Limb(0xFF6A4A2A, 6), P(x, y), P(x, y, 130));
        Poly(dc, 0xFFF0E0B0, 0xFF3A2A1A, P(190, y, 132), P(640, y, 132), P(640, y, 100), P(190, y, 100));
        var mid = P(415, y, 116);
        Text(dc, words, mid, 28, 0xFFC03020, center: true, shadow: false);
    }

    private void DrawHouse(DrawingContext dc, House h)
    {
        double n = h.Near, f = h.Far, bx = Field.HouseBack, fx = Field.HouseFront, top = House.WallHeight;
        uint wall = h.Wall, roof = h.Roof, trim = h.Trim;
        // The near end, then the front, then the roof.
        Poly(dc, Shade(wall, 0.82), 0xFF201810, P(bx, n), P(fx, n), P(fx, n, top), P(bx, n, top));
        Poly(dc, wall, 0xFF201810, P(fx, n), P(fx, f), P(fx, f, top), P(fx, n, top));
        double ridge = top + 58, rx = (bx + fx) / 2;
        Poly(dc, roof, 0xFF201810, P(fx + 12, n - 10, top - 6), P(fx + 12, f + 10, top - 6), P(rx, f + 10, ridge), P(rx, n - 10, ridge));
        Poly(dc, Shade(wall, 0.72), 0xFF201810, P(bx, n, top), P(fx, n, top), P(rx, n, ridge));
        Poly(dc, Shade(roof, 0.8), 0xFF201810, P(bx - 12, n - 10, top - 6), P(rx, n - 10, ridge), P(rx, n - 4, ridge - 2), P(bx - 8, n - 4, top - 4));
        if (h.Style == 1)
        {
            // A brick chimney poking through the roof.
            double cy = h.Y0 + 190, c0 = rx + 8, c1 = rx + 30;
            Poly(dc, 0xFF8A3A2A, 0xFF201810, P(c0, cy, ridge - 30), P(c1, cy, ridge - 30), P(c1, cy, ridge + 14), P(c0, cy, ridge + 14));
            Poly(dc, 0xFF6A2A1E, 0xFF201810, P(c1, cy, ridge - 30), P(c1, cy + 22, ridge - 30), P(c1, cy + 22, ridge + 14), P(c1, cy, ridge + 14));
            Poly(dc, 0xFF3A2A22, 0xFF201810, P(c0, cy, ridge + 14), P(c1, cy, ridge + 14), P(c1, cy + 22, ridge + 14), P(c0, cy + 22, ridge + 14));
        }
        // A window on the near end.
        Poly(dc, 0xFF2A3A5A, trim, P(bx + 50, n, 54), P(bx + 110, n, 54), P(bx + 110, n, 96), P(bx + 50, n, 96));
        // The front windows.
        for (int i = 0; i < 4; i++)
        {
            double wy = h.Y0 + House.WindowAt[i];
            var (lo, hi) = House.WindowZ[i];
            var a = P(fx, wy - House.WindowHalf, lo);
            var b = P(fx, wy + House.WindowHalf, lo);
            var c = P(fx, wy + House.WindowHalf, hi);
            var d = P(fx, wy - House.WindowHalf, hi);
            if (h.Broken[i])
            {
                Poly(dc, 0xFF0C0C12, trim, a, b, c, d);
                Poly(dc, 0xC0C0E0F0, null, a, P(fx, wy - 12, lo), P(fx, wy - House.WindowHalf, lo + 18));
                Poly(dc, 0xC0C0E0F0, null, c, P(fx, wy + 10, hi), P(fx, wy + House.WindowHalf, hi - 14));
            }
            else
            {
                bool lit = !h.Subscriber && Hash(h.Index, i, 5) % 3 == 0;
                Poly(dc, lit ? 0xFFF0D890 : 0xFF6A9AC8, trim, a, b, c, d);
                dc.DrawLine(Pen(trim, 2), P(fx, wy, lo), P(fx, wy, hi));
                dc.DrawLine(Pen(0x60FFFFFF, 3), P(fx, wy - 14, lo + 6), P(fx, wy - 14, hi - 6));
            }
        }
        // The door.
        var d0 = P(fx, h.DoorY - House.DoorHalf);
        var d1 = P(fx, h.DoorY + House.DoorHalf);
        var d2 = P(fx, h.DoorY + House.DoorHalf, House.DoorHeight);
        var d3 = P(fx, h.DoorY - House.DoorHalf, House.DoorHeight);
        Poly(dc, h.Spooky ? 0xFF2A1A1A : Shade(roof, 1.1), 0xFF201810, d0, d1, d2, d3);
        dc.DrawEllipse(Brush(Yellow), null, P(fx, h.DoorY - 10, 32), 2, 2);
        if (h.Spooky)
            Text(dc, "KEEP OUT", P(fx, h.DoorY + 60, 84), 18, 0xFFC0C0C0, center: true, shadow: false);
        // House number.
        Text(dc, (h.Index * 2 + 1).ToString(), P(fx, h.DoorY - 30, 80), 18, h.Subscriber ? Ink : 0xFFB0A890, center: true, shadow: false);
    }

    private void DrawMailbox(DrawingContext dc, House h)
    {
        double x = Field.MailboxX, y = h.MailboxY;
        dc.DrawLine(Pen(0xFF5A4030, 3), P(x, y), P(x, y, h.MailboxSmashed ? 24 : 40));
        if (h.MailboxSmashed)
        {
            Poly(dc, 0xFF7A7A80, 0xFF202020, P(x - 14, y - 4, 4), P(x + 10, y - 6, 2), P(x + 10, y + 6, 10), P(x - 14, y + 6, 12));
            return;
        }
        uint box = h.Subscriber ? 0xFF2A5AB0 : 0xFF7A7A80;
        Poly(dc, box, 0xFF101010, P(x - 14, y - 6, 40), P(x + 14, y - 6, 40), P(x + 14, y - 6, 58), P(x - 14, y - 6, 58));
        Poly(dc, Shade(box, 1.2), 0xFF101010, P(x - 14, y - 6, 58), P(x + 14, y - 6, 58), P(x + 14, y + 6, 58), P(x - 14, y + 6, 58));
        Poly(dc, Shade(box, 0.8), 0xFF101010, P(x + 14, y - 6, 40), P(x + 14, y + 6, 40), P(x + 14, y + 6, 58), P(x + 14, y - 6, 58));
        if (h.Subscriber)
        {
            bool up = h.Delivered != Delivery.None;
            var root = P(x - 10, y + 6, 52);
            dc.DrawLine(Pen(0xFFD02020, 3), root, up ? P(x - 10, y + 6, 72) : P(x + 6, y + 6, 52));
            if (h.Delivered == Delivery.Mailbox) Poly(dc, Newsprint, null, P(x + 14, y - 3, 46), P(x + 20, y - 3, 46), P(x + 20, y + 3, 52), P(x + 14, y + 3, 52));
        }
    }

    // ───────────────────────────── things ─────────────────────────────

    private void DrawThing(DrawingContext dc, Thing t)
    {
        var at = P(t.X, t.Y);
        double x = at.X, y = at.Y;
        bool down = t.Hit;
        switch (t.Kind)
        {
            case ThingKind.Hedge:
                Poly(dc, 0xFF2A6A2A, 0xFF143A14, P(0, t.Y - 6, 26), P(252, t.Y - 6, 26), P(252, t.Y + 6, 26), P(0, t.Y + 6, 26));
                Poly(dc, 0xFF235A23, 0xFF143A14, P(0, t.Y - 6), P(252, t.Y - 6), P(252, t.Y - 6, 26), P(0, t.Y - 6, 26));
                Poly(dc, 0xFF1E4E1E, 0xFF143A14, P(252, t.Y - 6), P(252, t.Y + 6), P(252, t.Y + 6, 26), P(252, t.Y - 6, 26));
                break;
            case ThingKind.Hydrant:
                dc.DrawRectangle(Brush(0xFFD02A20), Pen(0xFF501010, 1.5), new Rect(x - 6, y - 22, 12, 22), 3, 3);
                dc.DrawEllipse(Brush(0xFFE04030), Pen(0xFF501010, 1.5), new Point(x, y - 23), 7, 4);
                dc.FillRectangle(Brush(0xFFD02A20), new Rect(x - 10, y - 15, 20, 5));
                break;
            case ThingKind.TrashCan:
                if (down)
                {
                    dc.DrawRectangle(Brush(0xFF8A8A92), Pen(0xFF303034, 1.5), new Rect(x - 16, y - 12, 28, 14), 3, 3);
                    dc.DrawEllipse(Brush(0xFFB0A070), null, new Point(x - 22, y), 8, 4);
                    break;
                }
                dc.DrawRectangle(Brush(0xFF8A8A92), Pen(0xFF303034, 1.5), new Rect(x - 9, y - 26, 18, 26), 2, 2);
                dc.DrawEllipse(Brush(0xFFA0A0A8), Pen(0xFF303034, 1.5), new Point(x, y - 27), 11, 4);
                break;
            case ThingKind.Drain:
                Poly(dc, 0xFF202024, 0xFF8A8A8A, P(t.X - 14, t.Y - 10), P(t.X + 14, t.Y - 10), P(t.X + 14, t.Y + 10), P(t.X - 14, t.Y + 10));
                for (int i = -2; i <= 2; i++) dc.DrawLine(Pen(0xFF6A6A6A, 1.5), P(t.X + i * 5, t.Y - 9), P(t.X + i * 5, t.Y + 9));
                break;
            case ThingKind.Gnome:
                if (down) { dc.DrawEllipse(Brush(0xFF3A5AD0), null, new Point(x, y - 4), 12, 5); dc.DrawEllipse(Brush(0xFFD03030), null, new Point(x - 12, y - 4), 6, 4); break; }
                dc.DrawRectangle(Brush(0xFF3A5AD0), null, new Rect(x - 6, y - 16, 12, 16), 3, 3);
                dc.DrawEllipse(Brush(0xFFF0F0F0), null, new Point(x, y - 17), 6, 5);
                dc.DrawEllipse(Brush(0xFFF0C090), null, new Point(x, y - 22), 4, 4);
                Poly(dc, 0xFFD03030, null, new Point(x - 6, y - 24), new Point(x + 6, y - 24), new Point(x + 1, y - 38));
                break;
            case ThingKind.Tombstone:
                if (down) { Poly(dc, 0xFF8A8A90, 0xFF303034, P(t.X - 26, t.Y - 8, 2), P(t.X, t.Y - 8, 2), P(t.X, t.Y + 8, 2), P(t.X - 26, t.Y + 8, 2)); break; }
                dc.DrawRectangle(Brush(0xFF8A8A90), Pen(0xFF303034, 1.5), new Rect(x - 10, y - 30, 20, 30), 8, 8);
                Text(dc, "RIP", new Point(x, y - 18), 14, 0xFF404048, center: true, shadow: false);
                break;
            case ThingKind.Cone:
                Poly(dc, 0xFFFF7A1A, 0xFF602A0A, new Point(x - 9, y), new Point(x + 9, y), new Point(x, y - 26));
                dc.DrawLine(Pen(0xFFF8F8F8, 3), new Point(x - 5, y - 10), new Point(x + 5, y - 10));
                break;
            case ThingKind.Bundle:
                for (int i = 0; i < 3; i++)
                    Poly(dc, Newsprint, 0xFF6A6458, P(t.X - 12, t.Y - 8, i * 5), P(t.X + 12, t.Y - 8, i * 5), P(t.X + 12, t.Y + 8, i * 5 + 4), P(t.X - 12, t.Y + 8, i * 5 + 4));
                dc.DrawLine(Pen(0xFF8A2A1A, 2), P(t.X, t.Y - 8, 14), P(t.X, t.Y + 8, 14));
                break;
            case ThingKind.Car: DrawCar(dc, t); break;
            case ThingKind.Dog: DrawDog(dc, t, x, y); break;
            case ThingKind.Mower:
            {
                Poly(dc, down ? 0xFF7A3A2A : 0xFFD03A2A, 0xFF301010, P(t.X - 12, t.Y - 10, 10), P(t.X + 12, t.Y - 10, 10), P(t.X + 12, t.Y + 10, 10), P(t.X - 12, t.Y + 10, 10));
                Poly(dc, 0xFF901A10, 0xFF301010, P(t.X - 12, t.Y - 10), P(t.X + 12, t.Y - 10), P(t.X + 12, t.Y - 10, 10), P(t.X - 12, t.Y - 10, 10));
                dc.DrawLine(Pen(0xFF303030, 2), P(t.X - t.Facing * 12, t.Y, 10), P(t.X - t.Facing * 30, t.Y, 34));
                dc.DrawLine(Pen(0xFF303030, 3), P(t.X - t.Facing * 30, t.Y - 8, 34), P(t.X - t.Facing * 30, t.Y + 8, 34));
                if (!down)
                    for (int i = 0; i < 4; i++)
                        dc.DrawEllipse(Brush(0xFF7AC05A), null, P(t.X + t.Facing * (16 + i * 4), t.Y + (Hash(i, (int)(t.Age * 10), 3) % 14) - 7, 4 + i * 3), 2, 2);
                break;
            }
            case ThingKind.Skater:
            {
                if (down)
                {
                    dc.DrawEllipse(Brush(0xFF3A8AD0), null, new Point(x - 8, y - 4), 12, 5);
                    dc.DrawEllipse(Brush(0xFFF0C090), null, new Point(x - 20, y - 4), 5, 5);
                    dc.FillRectangle(Brush(0xFFE0A020), new Rect(x + 6, y - 2, 18, 4));
                    break;
                }
                dc.FillRectangle(Brush(0xFFE0A020), new Rect(x - 10, y - 4, 20, 4));
                dc.DrawLine(Limb(0xFF2A2A60, 4), new Point(x - 4, y - 5), new Point(x - 1, y - 20));
                dc.DrawLine(Limb(0xFF2A2A60, 4), new Point(x + 4, y - 5), new Point(x + 1, y - 20));
                dc.DrawLine(Limb(0xFF3A8AD0, 8), new Point(x, y - 20), new Point(x, y - 34));
                double arm = Math.Sin(t.Age * 4) * 6;
                dc.DrawLine(Limb(0xFFF0C090, 3), new Point(x, y - 32), new Point(x - 12, y - 26 + arm));
                dc.DrawLine(Limb(0xFFF0C090, 3), new Point(x, y - 32), new Point(x + 12, y - 26 - arm));
                dc.DrawEllipse(Brush(0xFFF0C090), null, new Point(x, y - 40), 5, 5);
                dc.DrawEllipse(Brush(0xFFD03030), null, new Point(x, y - 43), 5, 3);
                break;
            }
            case ThingKind.Dancer:
            {
                if (down)
                {
                    dc.DrawEllipse(Brush(0xFFF0F0F0), null, new Point(x, y - 4), 12, 5);
                    dc.DrawEllipse(Brush(0xFF8A5A3A), null, new Point(x - 14, y - 4), 5, 5);
                    break;
                }
                // Spinning on his head.
                dc.DrawEllipse(Brush(0xFF8A5A3A), null, new Point(x, y - 5), 5, 5);
                dc.DrawLine(Limb(0xFFF0F0F0, 8), new Point(x, y - 10), new Point(x, y - 26));
                double a = t.Age * 9;
                for (int k = 0; k < 2; k++)
                {
                    double ang = a + k * Math.PI;
                    dc.DrawLine(Limb(0xFF2A2A2A, 4), new Point(x, y - 26), new Point(x + Math.Cos(ang) * 16, y - 34 + Math.Sin(ang) * 5));
                }
                dc.DrawLine(Limb(0xFF8A5A3A, 3), new Point(x, y - 12), new Point(x - 9, y - 2));
                dc.DrawLine(Limb(0xFF8A5A3A, 3), new Point(x, y - 12), new Point(x + 9, y - 2));
                break;
            }
            case ThingKind.Tornado:
                for (int i = 0; i < 9; i++)
                {
                    double r = 8 + i * 5, sway = Math.Sin(t.Age * 5 + i * 0.7) * (4 + i * 1.5);
                    dc.DrawEllipse(Brush((uint)(0x90 - i * 4) << 24 | 0x00707884), Pen(0x80505060, 1.5), new Point(x + sway + i * 2, y - 6 - i * 14), r, r * 0.35);
                }
                for (int i = 0; i < 4; i++)
                {
                    double ang = t.Age * 6 + i * 1.6;
                    dc.FillRectangle(Brush(0xFF8A6A3A), new Rect(x + Math.Cos(ang) * 30, y - 40 - i * 18 + Math.Sin(ang) * 6, 4, 3));
                }
                break;
            case ThingKind.Ramp:
                Poly(dc, 0xFFA87A4A, 0xFF3A2A1A, P(t.X - 30, t.Y), P(t.X + 30, t.Y), P(t.X + 30, t.Y + 34, 24), P(t.X - 30, t.Y + 34, 24));
                Poly(dc, 0xFF7A5A3A, 0xFF3A2A1A, P(t.X + 30, t.Y), P(t.X + 30, t.Y + 34), P(t.X + 30, t.Y + 34, 24));
                for (int i = 1; i < 4; i++) dc.DrawLine(Pen(0xFF7A5A3A, 1), P(t.X - 30, t.Y + i * 8.5, i * 6), P(t.X + 30, t.Y + i * 8.5, i * 6));
                break;
            case ThingKind.Target:
            {
                dc.DrawLine(Pen(0xFF5A4030, 4), at, P(t.X, t.Y, down ? 10 : 46));
                if (down) { dc.DrawEllipse(Brush(0xFFF0F0F0), Pen(0xFF202020, 1), P(t.X - 18, t.Y, 4), 14, 6); break; }
                var c = P(t.X, t.Y, 62);
                dc.DrawEllipse(Brush(0xFFF0F0F0), Pen(0xFF202020, 1.5), c, 9, 17);
                dc.DrawEllipse(Brush(0xFFD02020), null, c, 6, 11);
                dc.DrawEllipse(Brush(0xFFF0F0F0), null, c, 3, 6);
                break;
            }
            case ThingKind.Barrier:
                foreach (double dx in new[] { -24.0, 24 }) dc.DrawLine(Pen(0xFF404040, 3), P(t.X + dx, t.Y), P(t.X + dx, t.Y, 22));
                for (int i = 0; i < 6; i++)
                    Poly(dc, i % 2 == 0 ? 0xFFE02020 : 0xFFF8F8F8, null, P(t.X - 26 + i * 8.67, t.Y, 14), P(t.X - 26 + (i + 1) * 8.67, t.Y, 14), P(t.X - 26 + (i + 1) * 8.67, t.Y, 28), P(t.X - 26 + i * 8.67, t.Y, 28));
                break;
        }
    }

    private void DrawCar(DrawingContext dc, Thing t)
    {
        double hw = 22, hl = Thing.CarLength / 2, x = t.X, y = t.Y;
        uint body = t.Colour;
        foreach (double wy in new[] { y - hl + 14, y + hl - 14 })
        foreach (double wx in new[] { x - hw, x + hw })
            dc.DrawEllipse(Brush(0xFF161616), null, P(wx, wy, 7), 6, 7);
        // Body: near end, street side, top.
        Poly(dc, Shade(body, 0.8), 0xFF101010, P(x - hw, y - hl, 6), P(x + hw, y - hl, 6), P(x + hw, y - hl, 26), P(x - hw, y - hl, 26));
        Poly(dc, Shade(body, 0.65), 0xFF101010, P(x + hw, y - hl, 6), P(x + hw, y + hl, 6), P(x + hw, y + hl, 26), P(x + hw, y - hl, 26));
        Poly(dc, body, 0xFF101010, P(x - hw, y - hl, 26), P(x + hw, y - hl, 26), P(x + hw, y + hl, 26), P(x - hw, y + hl, 26));
        // Cabin, set towards the back.
        double c0 = y - 12 * t.Facing, c1 = y + 14 * t.Facing;
        double ca = Math.Min(c0, c1), cb = Math.Max(c0, c1);
        Poly(dc, 0xFF8AB8D8, 0xFF101010, P(x - hw + 4, ca, 26), P(x + hw - 4, ca, 26), P(x + hw - 4, ca, 42), P(x - hw + 4, ca, 42));
        Poly(dc, 0xFF7AA8C8, 0xFF101010, P(x + hw - 4, ca, 26), P(x + hw - 4, cb, 26), P(x + hw - 4, cb, 42), P(x + hw - 4, ca, 42));
        Poly(dc, Shade(body, 1.1), 0xFF101010, P(x - hw + 4, ca, 42), P(x + hw - 4, ca, 42), P(x + hw - 4, cb, 42), P(x - hw + 4, cb, 42));
        // Headlights at the front.
        double front = y + hl * t.Facing;
        if (t.Facing < 0)
            foreach (double lx in new[] { x - 14, x + 14 })
                dc.DrawEllipse(Brush(0xFFFFF0B0), null, P(lx, front, 16), 3, 3);
    }

    private static void DrawDog(DrawingContext dc, Thing t, double x, double y)
    {
        int f = t.Facing;
        uint coat = t.Colour;
        double run = t.Hit || t.Timer is > 0 and < 3 ? Math.Sin(t.Age * 22) * 4 : 0;
        dc.DrawLine(Limb(coat, 3), new Point(x - 8, y - 10), new Point(x - 8 + run, y));
        dc.DrawLine(Limb(coat, 3), new Point(x + 8, y - 10), new Point(x + 8 - run, y));
        dc.DrawEllipse(Brush(coat), Pen(0xFF101010, 1.2), new Point(x, y - 12), 13, 7);
        dc.DrawEllipse(Brush(coat), Pen(0xFF101010, 1.2), new Point(x + f * 13, y - 19), 7, 6);
        dc.DrawEllipse(Brush(0xFF101010), null, new Point(x + f * 19, y - 18), 2, 2);
        dc.DrawLine(Limb(coat, 3), new Point(x - f * 12, y - 14), new Point(x - f * 19, y - 22));
        dc.DrawEllipse(Brush(Shade(coat, 0.7)), null, new Point(x + f * 9, y - 23), 3, 4);
        if (t.Timer is > 0 and < 3 && !t.Hit && (int)(t.Age * 4) % 2 == 0) Text(dc, "WOOF!", new Point(x + f * 14, y - 40), 18, White, center: true);
    }

    private void DrawPaper(DrawingContext dc, Paper p)
    {
        if (p.Z > 0) dc.DrawEllipse(Brush(0x50000000), null, P(p.X, p.Y), 7, 3);
        var c = P(p.X, p.Y, p.Z + 4);
        double a = p.Landed ? 0.4 : p.Spin;
        double dx = Math.Cos(a) * 9, dy = Math.Sin(a) * 9;
        dc.DrawLine(Limb(0xFF4A4438, 8), new Point(c.X - dx, c.Y - dy), new Point(c.X + dx, c.Y + dy));
        dc.DrawLine(Limb(Newsprint, 6), new Point(c.X - dx, c.Y - dy), new Point(c.X + dx, c.Y + dy));
        dc.DrawLine(Pen(0xFFC02020, 2), new Point(c.X - dy * 0.2, c.Y + dx * 0.2 - 3), new Point(c.X + dy * 0.2, c.Y - dx * 0.2 + 3));
    }

    // ───────────────────────────── the paperboy ─────────────────────────────

    private void DrawRider(DrawingContext dc, Rider r)
    {
        if (r.Safe > 0 && (int)(_clock * 12) % 2 == 0) return;
        dc.DrawEllipse(Brush(0x50000000), null, P(r.X, r.Y), 16, 6);
        if (r.Down > 0) { DrawCrashed(dc, r); return; }
        double z = r.Z;
        // The bike: wheels behind and ahead along the street.
        var back = P(r.X, r.Y - 16, z + 9);
        var front = P(r.X, r.Y + 16, z + 9);
        foreach (var w in new[] { back, front })
        {
            dc.DrawEllipse(null, Pen(0xFF181818, 3.5), w, 9, 9);
            dc.DrawEllipse(null, Pen(0xFFB0B0B8, 1), w, 6, 6);
        }
        var seat = P(r.X, r.Y - 6, z + 26);
        var bars = P(r.X, r.Y + 12, z + 30);
        var crank = P(r.X, r.Y - 2, z + 10);
        dc.DrawLine(Pen(0xFFD02A2A, 3), back, crank);
        dc.DrawLine(Pen(0xFFD02A2A, 3), crank, seat);
        dc.DrawLine(Pen(0xFFD02A2A, 3), seat, bars);
        dc.DrawLine(Pen(0xFFD02A2A, 3), crank, bars);
        dc.DrawLine(Pen(0xFFD02A2A, 3), bars, front);
        dc.DrawLine(Pen(0xFF202020, 3), new Point(bars.X - 7, bars.Y), new Point(bars.X + 7, bars.Y));
        // The boy: legs on the pedals, body leaning with the steering.
        double lean = r.Lean * 4;
        var hip = new Point(seat.X + lean * 0.5, seat.Y - 4);
        var shoulder = new Point(hip.X + lean - 6, hip.Y - 24);
        for (int k = 0; k < 2; k++)
        {
            double a = r.Pedal + k * Math.PI;
            var pedal = new Point(crank.X + Math.Cos(a) * 6, crank.Y + Math.Sin(a) * 6);
            var knee = new Point((hip.X + pedal.X) / 2 - 7, (hip.Y + pedal.Y) / 2 - 5);
            dc.DrawLine(Limb(0xFF2A3A7A, 5), hip, knee);
            dc.DrawLine(Limb(0xFF2A3A7A, 5), knee, pedal);
        }
        dc.DrawLine(Limb(0xFF3A9A5A, 11), hip, shoulder);
        // The bag of papers on his hip.
        dc.DrawRectangle(Brush(0xFFE8E0C8), Pen(0xFF6A5A3A, 1.5), new Rect(hip.X + 2, hip.Y - 16, 12, 14), 2, 2);
        if (r.Papers > 0) dc.FillRectangle(Brush(Newsprint), new Rect(hip.X + 4, hip.Y - 20, 8, 5));
        // Arms: one to the bars, the other out to the left when throwing.
        dc.DrawLine(Limb(0xFFF0C090, 3.5), shoulder, new Point(bars.X + 4, bars.Y - 2));
        if (r.ThrowTime > 0.08) dc.DrawLine(Limb(0xFFF0C090, 3.5), shoulder, new Point(shoulder.X - 18, shoulder.Y + 2));
        else dc.DrawLine(Limb(0xFFF0C090, 3.5), shoulder, new Point(bars.X - 4, bars.Y));
        dc.DrawEllipse(Brush(0xFFF0C090), Pen(0xFF5A3A20, 1), new Point(shoulder.X, shoulder.Y - 8), 6, 6);
        dc.DrawEllipse(Brush(0xFFD02A2A), null, new Point(shoulder.X, shoulder.Y - 12), 6, 3.5);
        dc.FillRectangle(Brush(0xFFD02A2A), new Rect(shoulder.X - 9, shoulder.Y - 12, 6, 2.5));
    }

    private void DrawCrashed(DrawingContext dc, Rider r)
    {
        var at = P(r.X, r.Y);
        // The bike on its side, the boy sitting dazed.
        dc.DrawEllipse(null, Pen(0xFF181818, 3), new Point(at.X - 22, at.Y - 4), 9, 5);
        dc.DrawEllipse(null, Pen(0xFF181818, 3), new Point(at.X + 4, at.Y - 2), 9, 5);
        dc.DrawLine(Pen(0xFFD02A2A, 3), new Point(at.X - 22, at.Y - 4), new Point(at.X + 4, at.Y - 2));
        var hip = new Point(at.X + 18, at.Y - 6);
        dc.DrawLine(Limb(0xFF2A3A7A, 5), hip, new Point(hip.X + 14, hip.Y + 2));
        dc.DrawLine(Limb(0xFF3A9A5A, 10), hip, new Point(hip.X, hip.Y - 18));
        dc.DrawEllipse(Brush(0xFFF0C090), null, new Point(hip.X, hip.Y - 26), 6, 6);
        for (int i = 0; i < 3; i++)
        {
            double a = _clock * 5 + i * 2.1;
            Text(dc, "*", new Point(hip.X + Math.Cos(a) * 12, hip.Y - 38 + Math.Sin(a) * 4), 18, Yellow, center: true, shadow: false);
        }
    }

    private void DrawEffect(DrawingContext dc, Engine.Effect e)
    {
        double t = e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Score:
            case EffectKind.Word:
                Text(dc, e.Text, P(e.X, e.Y, e.Z + 20 + t * 40), e.Kind == EffectKind.Word ? 24 : 26, e.Colour, center: true);
                break;
            case EffectKind.Glass:
                for (int i = 0; i < 7; i++)
                {
                    var c = P(e.X + 10 + i * 4 * t, e.Y + (i - 3) * 6, e.Z - t * t * 60 + (i % 3) * 6);
                    dc.FillRectangle(Brush(0xC0C8E8F8), new Rect(c.X, c.Y, 4, 3));
                }
                break;
        }
    }

    // ───────────────────────────── words ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xF0101418), new Rect(0, 0, Field.Width, Field.Top));
        dc.FillRectangle(Brush(0xFF6A5A3A), new Rect(0, Field.Top - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(14, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(14, 42), 36, White);
        Text(dc, g.DayName, new Point(150, 16), 22, Yellow);
        Text(dc, $"SUBSCRIBERS {g.Subscribers}", new Point(150, 42), 22, White);
        // Papers in the bag.
        Text(dc, "PAPERS", new Point(330, 16), 22, Dim);
        for (int i = 0; i < g.Player.Papers; i++)
        {
            double x = 334 + (i % 10) * 13, y = 38 + i / 10 * 12;
            dc.DrawRectangle(Brush(Newsprint), Pen(0xFF6A6458, 1), new Rect(x, y, 9, 9), 2, 2);
        }
        // Bikes left.
        Text(dc, "BIKES", new Point(480, 16), 22, Dim);
        for (int i = 0; i < Math.Min(6, g.Player.Lives); i++)
        {
            double x = 492 + i * 26;
            dc.DrawEllipse(null, Pen(0xFFD0D0D0, 2), new Point(x - 6, 46), 5, 5);
            dc.DrawEllipse(null, Pen(0xFFD0D0D0, 2), new Point(x + 6, 46), 5, 5);
            dc.DrawLine(Pen(0xFFD02A2A, 2), new Point(x - 6, 46), new Point(x + 4, 39));
        }
        // The route: every house, how its delivery stands, and where the bike is.
        double rx = 640, rw = 370;
        Text(dc, Street.Name, new Point(rx, 16), 22, Dim);
        for (int i = 0; i < Field.Houses; i++)
        {
            var h = g.Houses[i];
            uint c = !h.Subscriber ? 0xFF3A3A42 : h.Damaged ? Red : h.Delivered != Delivery.None ? Green : Yellow;
            dc.FillRectangle(Brush(c), new Rect(rx + i * rw / Field.Houses, 34, rw / Field.Houses - 3, 14));
        }
        double along = Math.Clamp((g.Player.Y - Field.Lead) / (Field.Houses * Field.Lot), 0, 1.04);
        double mx = rx + along * rw;
        Poly(dc, White, null, new Point(mx, 50), new Point(mx - 6, 60), new Point(mx + 6, 60));
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is { } msg) Text(dc, msg, new Point(Field.Width / 2, 200), msg.Length > 18 ? 46 : 60, Yellow, center: true);
        if (g.InCourse && g.Phase == Phase.Riding)
            Text(dc, $"TARGETS HIT {g.TargetsHit}", new Point(Field.Width / 2, 96), 30, White, center: true);
    }

    /// <summary>A newspaper's front page across the screen.</summary>
    private Rect Paper(DrawingContext dc, string edition, double height = 560)
    {
        var page = new Rect(170, 90, 684, height);
        dc.FillRectangle(Brush(0x60000000), page.Translate(new Vector(10, 10)));
        dc.FillRectangle(Brush(Newsprint), page);
        dc.DrawRectangle(Pen(0xFFB8B0A0, 2), page);
        Text(dc, "THE DAILY AVA", new Point(page.Center.X, page.Y + 50), 76, Ink, center: true, shadow: false);
        dc.FillRectangle(Brush(Ink), new Rect(page.X + 24, page.Y + 92, page.Width - 48, 3));
        Text(dc, edition, new Point(page.X + 28, page.Y + 110), 22, Ink, shadow: false);
        Text(dc, "PRICE 25c", new Point(page.Right - 28, page.Y + 110), 22, Ink, right: true, shadow: false);
        dc.FillRectangle(Brush(Ink), new Rect(page.X + 24, page.Y + 124, page.Width - 48, 1.5));
        return page;
    }

    private void DrawFrontPage(DrawingContext dc, string edition, string headline, string sub)
    {
        var page = Paper(dc, edition, 420);
        Text(dc, headline, new Point(page.Center.X, page.Y + 200), headline.Length > 22 ? 46 : 58, Ink, center: true, shadow: false);
        Text(dc, sub, new Point(page.Center.X, page.Y + 270), 32, 0xFF5A5040, center: true, shadow: false);
        Text(dc, $"{Street.Name} - {Engine.DayName}", new Point(page.Center.X, page.Y + 330), 26, 0xFF5A5040, center: true, shadow: false);
    }

    private void DrawTitle(DrawingContext dc)
    {
        var page = Paper(dc, $"{Street.Name} EDITION", 600);
        double cx = page.Center.X;
        Text(dc, "PAPERBOY STARTS ROUND", new Point(cx, page.Y + 168), 50, Ink, center: true, shadow: false);
        Text(dc, "AVA PAPERBOY - AFTER THE 1985 ARCADE CLASSIC", new Point(cx, page.Y + 210), 24, 0xFF5A5040, center: true, shadow: false);
        dc.FillRectangle(Brush(Ink), new Rect(page.X + 24, page.Y + 232, page.Width - 48, 1.5));
        string[] left =
        [
            "DELIVER TO EVERY",
            "SUBSCRIBER ON THE",
            "STREET: THE MAILBOX",
            "SCORES 250, THE",
            "DOORSTEP 100.",
            "",
            "MISS ONE AND THEY",
            "CANCEL. BREAK THEIR",
            "WINDOW AND THEY",
            "CANCEL TOO.",
        ];
        string[] right =
        [
            "NON-SUBSCRIBERS",
            "ARE FAIR GAME:",
            "WINDOWS 100 EACH.",
            "",
            "MIND THE DOGS, CARS,",
            "MOWERS, SKATERS AND",
            "WORSE. PICK UP MORE",
            "PAPERS ON THE WAY.",
            "",
            "SEVEN DAYS. GOOD LUCK.",
        ];
        for (int i = 0; i < left.Length; i++)
        {
            Text(dc, left[i], new Point(page.X + 40, page.Y + 262 + i * 26), 23, Ink, shadow: false);
            Text(dc, right[i], new Point(cx + 20, page.Y + 262 + i * 26), 23, Ink, shadow: false);
        }
        dc.FillRectangle(Brush(0xFFB8B0A0), new Rect(cx - 1, page.Y + 250, 2, 260));
        Text(dc, "ARROWS: STEER, FASTER, SLOWER    Z / SPACE: THROW", new Point(cx, page.Y + 540), 24, 0xFF5A5040, center: true, shadow: false);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(Field.Width / 2, 726), 44, Yellow, center: true);
    }

    private void DrawDayStart(DrawingContext dc)
    {
        var g = Engine;
        var page = Paper(dc, $"{g.DayName} EDITION", 470);
        double cx = page.Center.X;
        Text(dc, g.Headline, new Point(cx, page.Y + 175), g.Headline.Length > 24 ? 40 : 48, Ink, center: true, shadow: false);
        Text(dc, $"PAPERBOY DUE ON {Street.Name} - {g.Subscribers} HOMES TAKE THE PAPER", new Point(cx, page.Y + 225), 24, 0xFF5A5040, center: true, shadow: false);
        DrawStreetMap(dc, page.X + 52, page.Y + 270, page.Width - 104, i => g.Houses[i].Subscriber ? 0xFF2A5AB0 : 0xFFB8B0A0);
        Text(dc, "BLUE: SUBSCRIBERS", new Point(cx, page.Y + 360), 24, 0xFF2A5AB0, center: true, shadow: false);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO RIDE", new Point(cx, page.Y + 420), 28, Ink, center: true, shadow: false);
    }

    /// <summary>The street as a row of little houses.</summary>
    private void DrawStreetMap(DrawingContext dc, double x, double y, double width, Func<int, uint> colour, Func<int, string?>? label = null)
    {
        double w = width / Field.Houses;
        for (int i = 0; i < Field.Houses; i++)
        {
            double hx = x + i * w;
            uint c = colour(i);
            dc.FillRectangle(Brush(c), new Rect(hx + 3, y + 20, w - 6, 30));
            Poly(dc, Shade(c, 0.7), null, new Point(hx + 1, y + 20), new Point(hx + w - 1, y + 20), new Point(hx + w / 2, y + 4));
            if (label?.Invoke(i) is { } s) Text(dc, s, new Point(hx + w / 2, y + 66), 18, c, center: true, shadow: false);
        }
    }

    private void DrawReport(DrawingContext dc)
    {
        var g = Engine;
        var page = Paper(dc, $"{g.DayName} - DELIVERY REPORT", 520);
        double cx = page.Center.X;
        int delivered = g.Reports.Count(r => r == Report.Delivered);
        int cancelled = g.Reports.Count(r => r is Report.Missed or Report.Damaged);
        string head = g.Perfect ? "PERFECT DELIVERY!" : cancelled > 0 ? $"{cancelled} CANCEL THEIR PAPER" : "ROUND COMPLETE";
        Text(dc, head, new Point(cx, page.Y + 172), 50, g.Perfect ? 0xFF2A7A2A : Ink, center: true, shadow: false);
        DrawStreetMap(dc, page.X + 52, page.Y + 220, page.Width - 104,
            i => g.Reports[i] switch
            {
                Report.Delivered => 0xFF2A9A3A, Report.Missed => 0xFFC02020, Report.Damaged => 0xFFC02020, Report.NewSubscriber => 0xFFD0A020, _ => 0xFFB8B0A0,
            },
            i => g.Reports[i] switch { Report.Missed => "X", Report.Damaged => "!", Report.NewSubscriber => "NEW", Report.Delivered => "OK", _ => null });
        Text(dc, $"DELIVERED {delivered}    BONUS {g.DayBonus}{(g.Perfect ? " (DOUBLED)" : "")}", new Point(cx, page.Y + 340), 30, Ink, center: true, shadow: false);
        if (g.CourseFinished) Text(dc, $"TRAINING GROUND: TARGETS HIT {g.TargetsHit}, FINISHED", new Point(cx, page.Y + 380), 24, 0xFF5A5040, center: true, shadow: false);
        else if (g.InCourse) Text(dc, $"TRAINING GROUND: TARGETS HIT {g.TargetsHit}, THEN A SPILL", new Point(cx, page.Y + 380), 24, 0xFF5A5040, center: true, shadow: false);
        if (g.Perfect && g.Reports.Contains(Report.NewSubscriber)) Text(dc, "A NEIGHBOUR SIGNS UP!", new Point(cx, page.Y + 420), 28, 0xFFB08010, center: true, shadow: false);
        Text(dc, "X  MISSED      !  WINDOW BROKEN", new Point(cx, page.Y + 470), 22, 0xFF5A5040, center: true, shadow: false);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC8000010), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "BEST PAPERBOYS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   DAY", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            string day = Street.Days[Math.Clamp(e.Stage - 1, 0, 6)][..3];
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {day}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC8000010), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
