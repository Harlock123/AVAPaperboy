namespace AVAPaperboy.Engine;

/// <summary>Up/down for faster and slower, left/right to steer, and the throw.</summary>
public readonly record struct Controls(int Dx, int Dy, bool Throw = false);

public sealed class Rider
{
    public const double Slow = 70, Normal = 150, Fast = 270, SteerSpeed = 210, Radius = 11;
    public double X = 330, Y;
    public double Z, Vz;
    public double Speed = Normal;
    public int Lean;
    public int Papers = 10;
    public int Lives;
    public bool ThrowHeld;
    public double Pedal;
    public double ThrowTime;
    public bool Airborne => Z > 0 || Vz > 0;
    /// <summary>On the ground after a crash, and for how long.</summary>
    public double Down;
    /// <summary>Back on the bike after a crash: a moment when nothing can knock him off.</summary>
    public double Safe;
    public string? CrashedInto;
}

public sealed class Paper
{
    public const double Across = 260, Up = 170, Gravity = 600, Height = 40;
    public double X, Y, Z, Vx, Vy, Vz, Age, Spin;
    public bool Landed, Done;
}

public enum Delivery { None, Doorstep, Mailbox }

public sealed class House
{
    public int Index;
    public bool Subscriber;
    public Delivery Delivered;
    /// <summary>A subscriber's window broken: they'll cancel, delivered or not.</summary>
    public bool Damaged;
    public bool MailboxSmashed;
    /// <summary>Front windows: two downstairs, two up.</summary>
    public bool[] Broken = new bool[4];
    public uint Wall, Roof, Trim;
    public int Style;
    /// <summary>This one's garden is full of gravestones.</summary>
    public bool Spooky;

    public double Y0 => Field.Lead + Index * Field.Lot;
    public double Near => Y0 + 40;
    public double Far => Y0 + 240;
    public double DoorY => Y0 + 130;
    public double MailboxY => Y0 + 172;
    /// <summary>Where along the street each front window is.</summary>
    public static readonly double[] WindowAt = [70, 196, 70, 196];
    public static readonly (double Lo, double Hi)[] WindowZ = [(26, 60), (26, 60), (78, 110), (78, 110)];
    public const double WindowHalf = 22, DoorHalf = 16, WallHeight = 124, DoorHeight = 66;
}

public enum ThingKind
{
    // Standing still.
    Hydrant, TrashCan, Drain, Gnome, Tombstone, Cone, Hedge, Bundle,
    // On the move.
    Car, Dog, Mower, Skater, Dancer, Tornado,
    // The training ground.
    Ramp, Target, Barrier,
}

public sealed class Thing
{
    public ThingKind Kind;
    public double X, Y, Vx, Vy, Age, Timer;
    /// <summary>Knocked over, run off, or picked up: harmless now.</summary>
    public bool Hit;
    public bool Gone;
    /// <summary>A car's colour, a dog's coat.</summary>
    public uint Colour;
    /// <summary>Where it started (a mower's lawn, a dog's kennel).</summary>
    public double HomeX, HomeY;
    public int Facing = 1;

    public double Radius => Kind switch
    {
        ThingKind.Car => 30,
        ThingKind.Hedge => 0,
        ThingKind.Tornado => 26,
        ThingKind.Ramp => 32,
        ThingKind.Barrier => 26,
        ThingKind.Dog or ThingKind.Mower or ThingKind.Skater or ThingKind.Dancer => 14,
        ThingKind.Drain => 14,
        _ => 11,
    };

    /// <summary>How long a car is along the street.</summary>
    public const double CarLength = 70;

    /// <summary>Moving hazards a paper can hit, and the points for it.</summary>
    public int Points => Kind switch
    {
        ThingKind.Dog => 200, ThingKind.Skater => 250, ThingKind.Dancer => 150, ThingKind.Mower => 100,
        ThingKind.Gnome or ThingKind.Tombstone => 100, ThingKind.TrashCan => 50, ThingKind.Car => 50,
        ThingKind.Target => 250, _ => 0,
    };

    public bool Moves => Kind is ThingKind.Car or ThingKind.Dog or ThingKind.Mower or ThingKind.Skater or ThingKind.Tornado;
}

public enum EffectKind { Score, Glass, Splash, Dust, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Z, Age, Life = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
