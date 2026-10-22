using System;
using System.IO;
using System.Text;

namespace Cypher.Core
{
    /// <summary>Where the settings text lives. I/O failures (disk full, permissions) are never swallowed.</summary>
    public interface ISettingsStorage
    {
        bool Exists { get; }

        /// <summary>Throws if there is nothing stored.</summary>
        string ReadAll();

        /// <summary>Replaces the stored text; a crash part-way must leave the old text intact.</summary>
        void WriteAll(string text);

        /// <summary>
        /// Moves the stored text out of the way, keeping it for the user, and returns where it went.
        /// <paramref name="label"/> becomes part of the name, e.g. "bad" or "newer".
        /// </summary>
        string SetAside(string label);
    }

    public sealed class MemorySettingsStorage : ISettingsStorage
    {
        string _text;

        public MemorySettingsStorage(string text = null)
        {
            _text = text;
        }

        public bool Exists => _text != null;

        /// <summary>Text moved aside, newest last, as <c>label: text</c>.</summary>
        public System.Collections.Generic.List<string> SetAsideLog { get; } = new System.Collections.Generic.List<string>();

        public string ReadAll() => _text ?? throw new InvalidOperationException("Nothing is stored.");

        public void WriteAll(string text) => _text = text ?? throw new ArgumentNullException(nameof(text));

        public string SetAside(string label)
        {
            if (_text == null) throw new InvalidOperationException("Nothing is stored.");
            SetAsideLog.Add($"{label}: {_text}");
            _text = null;
            return $"memory:{label}{SetAsideLog.Count}";
        }
    }

    /// <summary>A single JSON file, written through a temporary file so a crash cannot leave half a settings file.</summary>
    public sealed class FileSettingsStorage : ISettingsStorage
    {
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public string Path { get; }

        public FileSettingsStorage(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path must not be empty", nameof(path));
            Path = path;
        }

        public bool Exists => File.Exists(Path);

        public string ReadAll() => File.ReadAllText(Path, Encoding.UTF8);

        public void WriteAll(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            Directory.CreateDirectory(folder);

            string temp = Path + ".tmp";
            File.WriteAllText(temp, text, Utf8NoBom);
            if (File.Exists(Path)) File.Replace(temp, Path, null);
            else File.Move(temp, Path);
        }

        public string SetAside(string label)
        {
            if (!File.Exists(Path)) throw new FileNotFoundException("Nothing is stored.", Path);
            string target = Path + "." + label;
            for (int n = 2; File.Exists(target); n++) target = Path + "." + label + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            File.Move(Path, target);
            return target;
        }
    }
}
