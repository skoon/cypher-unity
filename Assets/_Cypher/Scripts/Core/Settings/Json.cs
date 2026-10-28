using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cypher.Core
{
    public sealed class JsonException : Exception
    {
        public int Position { get; }

        public JsonException(string message, int position) : base($"{message} (at character {position})")
        {
            Position = position;
        }
    }

    /// <summary>A JSON object that keeps insertion order, so a written file reads the way it was built.</summary>
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        readonly List<string> _keys = new List<string>();
        readonly Dictionary<string, object> _values = new Dictionary<string, object>();

        public int Count => _keys.Count;

        public IReadOnlyList<string> Keys => _keys;

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public bool TryGetValue(string key, out object value) => _values.TryGetValue(key, out value);

        public object this[string key]
        {
            get => _values.TryGetValue(key, out object v) ? v : throw new KeyNotFoundException($"No key '{key}'");
            set
            {
                if (!_values.ContainsKey(key)) _keys.Add(key);
                _values[key] = value;
            }
        }

        public void Add(string key, object value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (_values.ContainsKey(key)) throw new ArgumentException($"Duplicate key '{key}'", nameof(key));
            this[key] = value;
        }

        public bool Remove(string key)
        {
            if (!_values.Remove(key)) return false;
            _keys.Remove(key);
            return true;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (string key in _keys) yield return new KeyValuePair<string, object>(key, _values[key]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A small JSON reader and writer with no dependencies, so settings can be parsed and migrated in Core and in the
    /// fast test loop. The tree holds <see cref="JsonObject"/>, <c>List&lt;object&gt;</c>, string, double, bool and null.
    /// Strict: no comments, no trailing commas, no duplicate keys, no NaN or Infinity, nesting capped at
    /// <see cref="MaxDepth"/>. Every failure is a <see cref="JsonException"/> that says where.
    /// </summary>
    public static class Json
    {
        public const int MaxDepth = 64;

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var parser = new Parser(text);
            return parser.ParseDocument();
        }

        public static string Write(object value, bool indent = true)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, indent, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object value, bool indent, int depth)
        {
            if (depth > MaxDepth) throw new ArgumentException($"JSON nesting is deeper than {MaxDepth}");
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case double d:
                    WriteNumber(sb, d);
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    WriteNumber(sb, f);
                    break;
                case JsonObject obj:
                    WriteObject(sb, obj, indent, depth);
                    break;
                case IList<object> list:
                    WriteArray(sb, list, indent, depth);
                    break;
                default:
                    throw new ArgumentException($"Cannot write a {value.GetType().Name} as JSON");
            }
        }

        static void WriteObject(StringBuilder sb, JsonObject obj, bool indent, int depth)
        {
            if (obj.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> kv in obj)
            {
                if (!first) sb.Append(',');
                first = false;
                NewLine(sb, indent, depth + 1);
                WriteString(sb, kv.Key);
                sb.Append(indent ? ": " : ":");
                WriteValue(sb, kv.Value, indent, depth + 1);
            }
            NewLine(sb, indent, depth);
            sb.Append('}');
        }

        static void WriteArray(StringBuilder sb, IList<object> list, bool indent, int depth)
        {
            if (list.Count == 0)
            {
                sb.Append("[]");
                return;
            }
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NewLine(sb, indent, depth + 1);
                WriteValue(sb, list[i], indent, depth + 1);
            }
            NewLine(sb, indent, depth);
            sb.Append(']');
        }

        static void NewLine(StringBuilder sb, bool indent, int depth)
        {
            if (!indent) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("JSON cannot hold NaN or Infinity");
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else sb.Append(Shortest(d));
        }

        /// <summary>
        /// The fewest significant digits that read back as the same double, as JavaScript writes numbers. ("R" is not that on every runtime: Unity's Mono gives
        /// more digits for some values, so the same data would write differently there.)
        /// </summary>
        static string Shortest(double d)
        {
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            for (int digits = 15; digits <= 17; digits++)
            {
                string g = d.ToString("G" + digits, CultureInfo.InvariantCulture);
                if (double.Parse(g, NumberStyles.Float, CultureInfo.InvariantCulture) == d)
                {
                    s = g;
                    break;
                }
            }
            int e = s.IndexOf('E');
            if (e < 0) return s;
            // 1E-07 → 1e-7, 1.5E+21 → 1.5e+21
            string exponent = s.Substring(e + 1);
            char sign = exponent[0] == '-' ? '-' : '+';
            return s.Substring(0, e) + "e" + sign + exponent.TrimStart('+', '-').TrimStart('0');
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string text)
            {
                _s = text;
                _i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;   // a byte-order mark is not part of the document
            }

            public object ParseDocument()
            {
                SkipWhitespace();
                if (_i >= _s.Length) throw Error("Empty document");
                object value = ParseValue(0);
                SkipWhitespace();
                if (_i < _s.Length) throw Error("Unexpected characters after the document");
                return value;
            }

            JsonException Error(string message) => new JsonException(message, _i);

            void SkipWhitespace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
            }

            object ParseValue(int depth)
            {
                if (depth > MaxDepth) throw Error($"Nesting is deeper than {MaxDepth}");
                if (_i >= _s.Length) throw Error("Unexpected end of document");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error($"Unexpected character '{c}'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error($"Expected '{word}'");
                _i += word.Length;
            }

            JsonObject ParseObject(int depth)
            {
                var obj = new JsonObject();
                _i++;   // {
                SkipWhitespace();
                if (_i < _s.Length && _s[_i] == '}')
                {
                    _i++;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (_i >= _s.Length || _s[_i] != '"') throw Error("Expected a quoted key");
                    int keyAt = _i;
                    string key = ParseString();
                    if (obj.ContainsKey(key)) throw new JsonException($"Duplicate key '{key}'", keyAt);
                    SkipWhitespace();
                    if (_i >= _s.Length || _s[_i] != ':') throw Error("Expected ':' after the key");
                    _i++;
                    SkipWhitespace();
                    obj.Add(key, ParseValue(depth + 1));
                    SkipWhitespace();
                    if (_i >= _s.Length) throw Error("Unterminated object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return obj; }
                    throw Error("Expected ',' or '}'");
                }
            }

            List<object> ParseArray(int depth)
            {
                var list = new List<object>();
                _i++;   // [
                SkipWhitespace();
                if (_i < _s.Length && _s[_i] == ']')
                {
                    _i++;
                    return list;
                }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (_i >= _s.Length) throw Error("Unterminated array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw Error("Expected ',' or ']'");
                }
            }

            string ParseString()
            {
                _i++;   // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (_i >= _s.Length) throw Error("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) { _i--; throw Error("Raw control character in a string"); }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (_i >= _s.Length) throw Error("Unterminated escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u': sb.Append(ParseHex4()); break;
                        default: _i--; throw Error($"Bad escape '\\{e}'");
                    }
                }
            }

            char ParseHex4()
            {
                if (_i + 4 > _s.Length) throw Error("Truncated \\u escape");
                int v = 0;
                for (int k = 0; k < 4; k++)
                {
                    char h = _s[_i + k];
                    int d = h >= '0' && h <= '9' ? h - '0' : h >= 'a' && h <= 'f' ? h - 'a' + 10 : h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                    if (d < 0) { _i += k; throw Error("Bad hex digit in a \\u escape"); }
                    v = v * 16 + d;
                }
                _i += 4;
                return (char)v;
            }

            double ParseNumber()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                if (_i >= _s.Length || !IsDigit(_s[_i])) throw Error("Expected a digit");
                if (_s[_i] == '0')
                {
                    _i++;
                    if (_i < _s.Length && IsDigit(_s[_i])) throw Error("A number cannot have a leading zero");
                }
                else
                {
                    while (_i < _s.Length && IsDigit(_s[_i])) _i++;
                }
                if (_i < _s.Length && _s[_i] == '.')
                {
                    _i++;
                    if (_i >= _s.Length || !IsDigit(_s[_i])) throw Error("Expected a digit after the decimal point");
                    while (_i < _s.Length && IsDigit(_s[_i])) _i++;
                }
                if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    _i++;
                    if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    if (_i >= _s.Length || !IsDigit(_s[_i])) throw Error("Expected a digit in the exponent");
                    while (_i < _s.Length && IsDigit(_s[_i])) _i++;
                }
                double v;
                try
                {
                    v = double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    v = double.PositiveInfinity;   // older runtimes throw where newer ones return infinity
                }
                if (double.IsInfinity(v)) { _i = start; throw Error("Number is too large"); }
                return v;
            }

            static bool IsDigit(char c) => c >= '0' && c <= '9';
        }
    }
}
