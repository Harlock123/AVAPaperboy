namespace AVAPaperboy.Engine;

/// <summary>
/// Larkspur Lane: twenty houses down one side of a street, the same every day of the week, and the
/// things in the way, which get worse as the week goes on. At the end of the street is the training
/// ground.
/// </summary>
public static class Street
{
    public const string Name = "LARKSPUR LANE";
    public static readonly string[] Days = ["MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY"];

    private static readonly (uint Wall, uint Roof, uint Trim)[] Bright =
    [
        (0xFFF2D7A0, 0xFFB0463A, 0xFFFFFFFF), (0xFFA8D0E8, 0xFF3E5A8A, 0xFFFFFFFF), (0xFFF0B8B8, 0xFF7A3A4A, 0xFFFFF4E0),
        (0xFFC8E0A8, 0xFF4A6A3A, 0xFFFFFFFF), (0xFFFFE890, 0xFF8A5A2A, 0xFFFFFFFF), (0xFFE0C8F0, 0xFF5A3A7A, 0xFFFFFFFF),
    ];
    private static readonly (uint Wall, uint Roof, uint Trim)[] Drab =
    [
        (0xFF6A6458, 0xFF2E2A28, 0xFF4A4640), (0xFF5A5A62, 0xFF26262C, 0xFF46464E), (0xFF6E5A4A, 0xFF32261E, 0xFF52443A),
    ];

    /// <summary>The houses, and which take the paper to begin with: ten of the twenty.</summary>
    public static List<House> MakeHouses(Random rng)
    {
        var houses = new List<House>();
        var subscribers = Enumerable.Range(0, Field.Houses).OrderBy(_ => rng.Next()).Take(10).ToHashSet();
        for (int i = 0; i < Field.Houses; i++)
            houses.Add(new House { Index = i, Subscriber = subscribers.Contains(i), Style = rng.Next(3) });
        // One non-subscriber keeps a garden of gravestones.
        var spooky = houses.Where(h => !h.Subscriber && h.Index > 2).OrderBy(_ => rng.Next()).First();
        spooky.Spooky = true;
        Paint(houses, rng);
        return houses;
    }

    /// <summary>Subscribers' houses are bright and kept up; the others are drab.</summary>
    public static void Paint(List<House> houses, Random rng)
    {
        foreach (var h in houses)
        {
            var p = h.Spooky ? (0xFF4A4A56u, 0xFF1E1E26u, 0xFF6A6A7Au) : h.Subscriber ? Bright[(h.Index * 7 + h.Style) % Bright.Length] : Drab[h.Index % Drab.Length];
            (h.Wall, h.Roof, h.Trim) = p;
        }
    }

    /// <summary>Everything in the way on a given day.</summary>
    public static List<Thing> MakeThings(int day, List<House> houses, Random rng)
    {
        var things = new List<Thing>();
        void Add(ThingKind k, double x, double y, uint colour = 0) => things.Add(new Thing { Kind = k, X = x, Y = y, HomeX = x, HomeY = y, Colour = colour });
        // Hedges between the gardens, with a gap by the pavement.
        for (int i = 0; i <= Field.Houses; i++) Add(ThingKind.Hedge, 0, Field.Lead + i * Field.Lot);
        // Each lot gets at most a couple of things, never side by side across the bike's way.
        foreach (var h in houses)
        {
            if (h.Index == 0) continue;
            // Gnomes and gravestones on the lawns.
            if (h.Spooky)
                foreach (double dy in new[] { 60.0, 104, 212 }) Add(ThingKind.Tombstone, 222 + rng.Next(30), h.Y0 + dy);
            else if (!h.Subscriber && rng.Next(3) == 0) Add(ThingKind.Gnome, 230 + rng.Next(30), h.Y0 + 60 + rng.Next(40));
            int r = rng.Next(100);
            double slot = h.Y0 + 230 + rng.Next(50);
            if (r < 22 + day * 3) Add(ThingKind.Hydrant, 316, slot);
            else if (r < 34 + day * 4) Add(ThingKind.TrashCan, 344, slot);
            else if (r < 44 + day * 5) Add(ThingKind.Drain, 392 + rng.Next(40), slot);
            else if (day >= 3 && r < 50 + day * 5) { Add(ThingKind.Cone, 420, slot); Add(ThingKind.Cone, 470, slot + 20); }
            // Dogs and mowers on some lawns, dancers on the pavement.
            int r2 = rng.Next(100);
            if (!h.Subscriber && !h.Spooky && r2 < 18 + day * 4) Add(ThingKind.Dog, 236, h.Y0 + 90, rng.Next(2) == 0 ? 0xFF8A5A2Au : 0xFF2A2A2Au);
            else if (day >= 2 && r2 > 88 - day * 2) Add(ThingKind.Mower, 230, h.Y0 + 190);
            else if (day >= 3 && r2 > 40 && r2 < 40 + day * 2) Add(ThingKind.Dancer, 330, h.Y0 + 60);
        }
        // Bundles of papers to pick up along the way.
        foreach (int lot in new[] { 5, 11, 16 }.Select(l => Math.Min(Field.Houses - 1, l + rng.Next(2))))
            Add(ThingKind.Bundle, 330, houses[lot].Y0 + 120);
        // The tornado comes later in the week.
        if (day >= 4) Add(ThingKind.Tornado, 300, houses[9 + rng.Next(4)].Y0);
        // The training ground: ramps to jump, barriers to dodge, targets to hit.
        double y = Field.StreetEnd + 220;
        int n = 0;
        while (y < Field.StreetEnd + Field.CourseLength - 200)
        {
            switch (n % 3)
            {
                case 0: Add(ThingKind.Ramp, 360 + rng.Next(160), y); break;
                case 1: Add(ThingKind.Barrier, 300 + rng.Next(240), y); Add(ThingKind.Target, 214, y + 40); break;
                default: Add(ThingKind.Target, 214, y); Add(ThingKind.Barrier, 300 + rng.Next(240), y + 60); break;
            }
            y += 170;
            n++;
        }
        things.Sort((a, b) => a.Y.CompareTo(b.Y));
        return things;
    }

    /// <summary>A headline for the front page.</summary>
    public static readonly string[] Headlines =
    [
        "MAYOR OPENS NEW DUCK POND", "LOCAL CAT ELECTED TO COUNCIL", "GIANT MARROW WINS SHOW", "BAKERY RUNS OUT OF BUNS",
        "HEDGE TRIMMED, NEIGHBOURS STUNNED", "LIBRARY BOOK RETURNED 40 YEARS LATE", "GNOME THEFTS BAFFLE POLICE",
        "TOWN CLOCK FIVE MINUTES FAST", "RECORD CROWD AT TUBA RECITAL", "SQUIRREL STEALS SANDWICH",
    ];
}
