using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cypher.Core
{
    /// <summary>
    /// The few JavaScript behaviours the RPG rules lean on, so the port gives the prototype's numbers exactly: <c>Math.round</c> (half up, not to even), rounding to
    /// decimal places, truthiness of a flag value, a number written as JavaScript writes it, and base 36.
    /// </summary>
    public static class RpgJs
    {
        /// <summary>JavaScript's <c>Math.round</c>: halves go up (towards +infinity), unlike <see cref="Math.Round(double)"/>.</summary>
        public static double Round(double v) => Math.Floor(v + 0.5);

        /// <summary><c>Math.round(v * 10^dp) / 10^dp</c>, as the prototype rounds to decimal places.</summary>
        public static double RoundTo(double v, int dp)
        {
            double f = Math.Pow(10, dp);
            return Round(v * f) / f;
        }

        /// <summary>JavaScript truthiness of a flag or JSON value: null, false, 0, NaN and "" are false.</summary>
        public static bool Truthy(object v)
        {
            switch (v)
            {
                case null: return false;
                case bool b: return b;
                case double d: return d != 0 && !double.IsNaN(d);
                case int i: return i != 0;
                case string s: return s.Length > 0;
                default: return true;
            }
        }

        /// <summary>A number as JavaScript's <c>String(n)</c> writes it, for the labels built from numbers (<c>5000€$</c>, <c>ITEM x2</c>).</summary>
        public static string Str(double v)
        {
            if (double.IsPositiveInfinity(v)) return "Infinity";
            if (v == Math.Floor(v) && Math.Abs(v) < 1e21) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary><c>n.toString(36)</c> for a non-negative integer.</summary>
        public static string Base36(uint n)
        {
            if (n == 0) return "0";
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var chars = new List<char>();
            while (n > 0)
            {
                chars.Add(digits[(int)(n % 36)]);
                n /= 36;
            }
            chars.Reverse();
            return new string(chars.ToArray());
        }

        /// <summary>Equality as JavaScript's <c>===</c> sees two JSON values (numbers by value, strings by content).</summary>
        public static bool StrictEquals(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is double da && b is double db) return da == db;
            if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.Ordinal);
            if (a is bool ba && b is bool bb) return ba == bb;
            return ReferenceEquals(a, b);
        }

        /// <summary>A JSON number as an integer, failing loudly on anything else.</summary>
        public static int Int(object v, string what)
        {
            if (v is double d && d == Math.Floor(d) && Math.Abs(d) <= int.MaxValue) return (int)d;
            throw new ArgumentException($"{what}: expected an integer, got {v ?? "null"}");
        }

        public static double Num(object v, string what)
        {
            if (v is double d && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
            throw new ArgumentException($"{what}: expected a finite number, got {v ?? "null"}");
        }

        /// <summary><c>asList(v)</c>: undefined or null is empty, an array is itself, anything else is a list of one.</summary>
        public static List<object> AsList(object v)
        {
            if (v == null) return new List<object>();
            if (v is List<object> list) return list;
            return new List<object> { v };
        }
    }
}
