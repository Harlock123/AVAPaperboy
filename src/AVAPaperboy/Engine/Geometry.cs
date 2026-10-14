namespace AVAPaperboy.Engine;

public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>
/// The street, seen from above and to one side. X runs across it (the back gardens at 0, the house
/// fronts, the lawns, the pavement, the road and the far kerb); Y runs along it, away from the
/// viewer; Z is height. On screen the street runs diagonally up to the left.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The bottom of the score bar.</summary>
    public const double Top = 64;

    // Across the street.
    public const double HouseBack = 30, HouseFront = 190;
    public const double MailboxX = 284;
    public const double Pavement = 300, Kerb = 360, RoadCentre = 500, FarKerb = 640;
    /// <summary>How far left and right the bike can go: from the lawns to the far kerb.</summary>
    public const double RideMin = 200, RideMax = 630;

    // Along it.
    public const double Lot = 300;
    public const int Houses = 20;
    /// <summary>Clear road before the first house.</summary>
    public const double Lead = 360;
    public static double StreetEnd => Lead + Houses * Lot;
    public const double CourseLength = 1700;

    /// <summary>Where on screen a point of the street is, with the camera at <paramref name="cam"/>.</summary>
    public static (double X, double Y) Project(double x, double y, double z, double cam) =>
        (330 + x - (y - cam) * 0.5, 760 - (y - cam) * 0.68 - z);
}
