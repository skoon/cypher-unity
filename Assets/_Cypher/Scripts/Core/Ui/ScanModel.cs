using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>What the scanner can tag.</summary>
    public enum ScanKind { Place, Person, Vehicle }

    /// <summary>A thing the scanner can tag: what it is, what is known about it, where its middle is and how tall it stands (for the bracket).</summary>
    public readonly struct ScanContact
    {
        public readonly ScanKind Kind;
        public readonly string Name, Sub, Action;
        public readonly double X, Y, Z, Height;

        public ScanContact(ScanKind kind, string name, string sub, string action, double x, double y, double z, double height)
        {
            Kind = kind; Name = name; Sub = sub; Action = action; X = x; Y = y; Z = z; Height = height;
        }
    }

    /// <summary>
    /// The optical scanner (hold F), after the prototype's <c>ui/Scanner.js</c>, with real contacts in place of its invented ones: the named places (what they
    /// are and what can be done there), the people and the vehicles around. The charge eases in and out at the prototype's rates; tags appear past a quarter of
    /// it, on the nearest few contacts in range and in view. Pure.
    /// </summary>
    public static class ScanModel
    {
        public const int MaxTags = 7;
        public const double Range = 70, MinRange = 0.8;
        public const double ChargeUp = 5.5, ChargeDown = 9;
        public const double TagsFrom = 0.25, LockedFrom = 0.9;

        /// <summary>Ease towards <paramref name="target"/> at <paramref name="rate"/> per second (the prototype's <c>damp</c>).</summary>
        public static double Damp(double current, double target, double rate, double dt) => current + (target - current) * (1 - Math.Exp(-rate * dt));

        /// <summary>The charge after <paramref name="dt"/> seconds of holding (or not holding) the button.</summary>
        public static double Charge(double charge, bool held, double dt) => Damp(charge, held ? 1 : 0, held ? ChargeUp : ChargeDown, dt);

        /// <summary>The contacts to tag: in range, passing <paramref name="inView"/>, nearest first, at most <see cref="MaxTags"/>.</summary>
        public static List<(ScanContact Contact, double Distance)> Pick(IEnumerable<ScanContact> contacts, double eyeX, double eyeY, double eyeZ, Func<ScanContact, bool> inView)
        {
            var found = new List<(ScanContact, double)>();
            foreach (ScanContact c in contacts)
            {
                double dx = c.X - eyeX, dy = c.Y - eyeY, dz = c.Z - eyeZ;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d > Range || d < MinRange) continue;
                if (!inView(c)) continue;
                found.Add((c, d));
            }
            return found.OrderBy(f => f.Item2).Take(MaxTags).ToList();
        }

        /// <summary>The state line: acquiring while charging, the count once locked, standby when let go.</summary>
        public static string State(bool held, double charge, int contacts) =>
            !held ? "STANDBY" : charge > LockedFrom ? $"{contacts} CONTACT{(contacts == 1 ? "" : "S")}" : "ACQUIRING";

        /// <summary>The readout: the nearest three by name and distance.</summary>
        public static string Readout(IReadOnlyList<(ScanContact Contact, double Distance)> found) => found.Count == 0
            ? "NO CONTACTS IN RANGE"
            : string.Join("   //   ", found.Take(3).Select(f => $"{f.Contact.Name} · {Math.Round(f.Distance).ToString(CultureInfo.InvariantCulture)}M"));

        /// <summary>The named places as contacts: name, kind, and what can be done there.</summary>
        public static List<ScanContact> Places(RpgCore core)
        {
            var list = new List<ScanContact>();
            foreach (RpgLocation l in core.GetLocationList())
            {
                (string Kind, string Label)? act = core.InteractionAt(l.Key);
                list.Add(new ScanContact(ScanKind.Place, l.Name.ToUpperInvariant(), (l.Kind ?? "place").ToUpperInvariant(), act.HasValue ? act.Value.Label.ToUpperInvariant() : "",
                    l.X, l.Y + 1.6, l.Z, 3.2));
            }
            return list;
        }

        public static ScanContact Person(double x, double y, double z) => new ScanContact(ScanKind.Person, "CIVILIAN", "NIGHT SECTOR // NO THREAT", "", x, y + 0.95, z, 1.85);

        public static ScanContact Vehicle(string type, double x, double y, double z) =>
            new ScanContact(ScanKind.Vehicle, (type ?? "vehicle").Replace('_', ' ').ToUpperInvariant(), "GROUND TRAFFIC // NO THREAT", "", x, y + 0.8, z, 1.6);
    }
}
