using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>One line of the job tracker.</summary>
    public readonly struct TrackerStep
    {
        public readonly string Text;
        public readonly bool Optional, Active;
        /// <summary>Metres to the step's place, or null when it has none.</summary>
        public readonly double? Distance;
        /// <summary>Where the step's place is, or null when it has none.</summary>
        public readonly (double X, double Z)? Place;

        public TrackerStep(string text, bool optional, bool active, double? distance, (double X, double Z)? place = null)
        {
            Text = text; Optional = optional; Active = active; Distance = distance; Place = place;
        }
    }

    /// <summary>A notice on the HUD: a kind (which colours it), a line and an optional second line.</summary>
    public readonly struct HudToast
    {
        public readonly string Kind, Text, Sub;
        public readonly double ExpiresAt;

        public HudToast(string kind, string text, string sub, double expiresAt)
        {
            Kind = kind; Text = text; Sub = sub; ExpiresAt = expiresAt;
        }
    }

    /// <summary>The notices beside the HUD: newest first, at most a few, each for a few seconds. A repeat of the newest replaces it rather than stacking.</summary>
    public sealed class HudToasts
    {
        public const int MaxVisible = 5;
        public const double LifetimeSeconds = 4.5;

        readonly List<HudToast> _toasts = new List<HudToast>();

        public void Push(string kind, string text, string sub, double now)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_toasts.Count > 0 && _toasts[0].Kind == kind && _toasts[0].Text == text && _toasts[0].Sub == sub) _toasts.RemoveAt(0);
            _toasts.Insert(0, new HudToast(kind, text, sub, now + LifetimeSeconds));
            if (_toasts.Count > MaxVisible) _toasts.RemoveRange(MaxVisible, _toasts.Count - MaxVisible);
        }

        public IReadOnlyList<HudToast> Visible(double now)
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                if (_toasts[i].ExpiresAt <= now) _toasts.RemoveAt(i);
            }
            return _toasts;
        }

        public void Clear() => _toasts.Clear();
    }

    /// <summary>
    /// The arithmetic of the HUD, kept pure so it is tested without Unity: bearings for the compass (0 is north, which is +Z, the way a new game faces), the
    /// minimap's turn with the player, distance and money labels as the prototype writes them, and which job steps the tracker lists.
    /// </summary>
    public static class HudModel
    {
        const double Rad2Deg = 180 / Math.PI;

        public static readonly IReadOnlyList<(int Degrees, string Label)> Cardinals = new[]
        {
            (0, "N"), (45, "NE"), (90, "E"), (135, "SE"), (180, "S"), (225, "SW"), (270, "W"), (315, "NW"),
        };

        /// <summary>A heading in degrees from 0 to 360 (yaw 0 faces +Z, north; yaw grows clockwise seen from above, towards +X).</summary>
        public static double Bearing(double yawRadians)
        {
            double d = yawRadians * Rad2Deg % 360;
            return d < 0 ? d + 360 : d;
        }

        /// <summary>The bearing from one point to another.</summary>
        public static double BearingTo(double fromX, double fromZ, double toX, double toZ) => Bearing(Math.Atan2(toX - fromX, toZ - fromZ));

        /// <summary>How far round a bearing is from the heading, -180 to 180 (negative is to the left).</summary>
        public static double Relative(double bearing, double heading)
        {
            double d = (bearing - heading) % 360;
            if (d > 180) d -= 360;
            if (d < -180) d += 360;
            return d;
        }

        /// <summary>An offset in the world as the minimap shows it, turning with the player: right of the player, and ahead of the player (up on the map).</summary>
        public static (double Right, double Ahead) OnMap(double dx, double dz, double yawRadians)
        {
            double s = Math.Sin(yawRadians), c = Math.Cos(yawRadians);
            return (dx * c - dz * s, dx * s + dz * c);
        }

        /// <summary><c>123M</c>, or <c>1.2KM</c> from a kilometre, as the prototype's tracker writes a distance.</summary>
        public static string Distance(double metres) =>
            metres >= 1000 ? (metres / 1000).ToString("0.0", CultureInfo.InvariantCulture) + "KM" : RpgJs.Round(metres).ToString(CultureInfo.InvariantCulture) + "M";

        /// <summary>A number that changes exactly when <see cref="Distance"/>'s text would, so a label is rewritten only then.</summary>
        public static long DistanceKey(double metres) => metres >= 1000 ? 1_000_000_000 + (long)Math.Round(metres / 100, MidpointRounding.AwayFromZero) : (long)RpgJs.Round(metres);

        /// <summary><c>1,250 €$</c>.</summary>
        public static string Money(double amount) => ((long)RpgJs.Round(amount)).ToString("#,0", CultureInfo.InvariantCulture) + " €$";

        /// <summary>A job type as the tracker heads it.</summary>
        public static string JobType(string type)
        {
            switch (type)
            {
                case "main": return "MAIN JOB";
                case "side": return "SIDE JOB";
                case "gig": return "GIG";
                default: return "JOB";
            }
        }

        /// <summary>
        /// The steps the tracker shows for a job snapshot (see <see cref="QuestLog.Snapshot"/>): the visible objectives not yet done, required ones first, at most four;
        /// the first required one is the active step. A step at a place has its distance from the player.
        /// </summary>
        public static List<TrackerStep> Steps(JsonObject quest, double playerX, double playerZ, int max = 4)
        {
            var steps = new List<TrackerStep>();
            if (quest == null) return steps;
            var open = ((List<object>)quest["objectives"]).Cast<JsonObject>().Where(o => !(bool)o["done"] && (bool)o["visible"]).ToList();
            var ordered = open.Where(o => !(bool)o["optional"]).Concat(open.Where(o => (bool)o["optional"])).Take(max);
            bool first = true;
            foreach (JsonObject o in ordered)
            {
                bool optional = (bool)o["optional"];
                double? dist = null;
                (double, double)? place = null;
                if (o["location"] is JsonObject loc)
                {
                    double x = (double)loc["x"], z = (double)loc["z"];
                    double dx = x - playerX, dz = z - playerZ;
                    dist = Math.Sqrt(dx * dx + dz * dz);
                    place = (x, z);
                }
                steps.Add(new TrackerStep((string)o["text"], optional, first && !optional, dist, place));
                if (!optional) first = false;
            }
            return steps;
        }

        /// <summary>The marker the waypoint points at: the tracked job's first open step that has a place, or null.</summary>
        public static JsonObject TrackedMarker(IReadOnlyList<JsonObject> markers)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i].TryGetValue("tracked", out object t) && t is bool b && b) return markers[i];
            }
            return null;
        }
    }

    /// <summary>
    /// The crosshair, after the prototype's <c>ui/hud/Crosshair.js</c>: each shot blooms it open, the bloom settles, and the blades sit at a gap that follows the
    /// gun's real spread projected to the screen (opened 24 px more while reloading). Pixels are the HUD's own (the panel's height).
    /// </summary>
    public sealed class Reticle
    {
        public double Gap { get; private set; } = 7;
        public double Bloom { get; private set; }

        /// <summary>A shot (or, at a third, a dry fire).</summary>
        public void Kick(double amount = 1) => Bloom = Math.Min(2.4, Bloom + 0.55 * amount);

        /// <summary>The spread's half-angle on screen, in pixels, for a vertical field of view and a screen height.</summary>
        public static double SpreadPixels(double halfAngle, double fovDegrees, double screenHeight) =>
            Math.Tan(halfAngle) * (screenHeight / 2) / Math.Tan(fovDegrees * Math.PI / 360);

        public void Update(double dt, double spreadPixels, bool reloading)
        {
            Bloom = Damp(Bloom, 0, 7, dt);
            double target = 5 + spreadPixels + (reloading ? 24 : 0) + Bloom * 16;
            Gap = Damp(Gap, target, 16, dt);
        }

        /// <summary>The gap as drawn: kept between 2 and 90 pixels.</summary>
        public double DrawnGap => Math.Max(2, Math.Min(90, Gap));

        static double Damp(double a, double b, double lambda, double dt) => a + (b - a) * (1 - Math.Exp(-lambda * dt));
    }
}
