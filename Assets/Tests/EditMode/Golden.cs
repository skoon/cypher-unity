using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace Cypher.Tests
{
    /// <summary>Reads fixtures written by tools/gen-golden.mjs from the web prototype.</summary>
    static class Golden
    {
        // [CallerFilePath] resolves at compile time, so fixtures are found next to this
        // file in both the Unity Editor and an out-of-Unity dotnet run.
        public static string[] Lines(string name, [CallerFilePath] string sourceFile = "") =>
            File.ReadAllLines(Path.Combine(Path.GetDirectoryName(sourceFile), "Fixtures", name));

        public static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        public static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

        public static List<string[]> Rows(string[] lines, string tag)
        {
            var rows = new List<string[]>();
            foreach (string line in lines)
            {
                if (line.StartsWith(tag + " ", StringComparison.Ordinal))
                {
                    rows.Add(line.Split(' '));
                }
            }
            return rows;
        }
    }
}
