using System;
using System.IO;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class FileSettingsStorageTests
    {
        string _dir;
        string _path;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cypher-settings-test-" + Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_dir, "nested", "settings.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void WritingCreatesTheFolderAndReadingReturnsTheText()
        {
            var s = new FileSettingsStorage(_path);
            Assert.IsFalse(s.Exists);
            s.WriteAll("{\"a\": \"café 日本\"}");
            Assert.IsTrue(s.Exists);
            Assert.AreEqual("{\"a\": \"café 日本\"}", s.ReadAll());
        }

        [Test]
        public void TheFileIsUtf8WithoutAByteOrderMark()
        {
            new FileSettingsStorage(_path).WriteAll("x");
            byte[] bytes = File.ReadAllBytes(_path);
            Assert.AreEqual(new byte[] { (byte)'x' }, bytes);
        }

        [Test]
        public void OverwritingReplacesTheTextAndLeavesNoTemporaryFile()
        {
            var s = new FileSettingsStorage(_path);
            s.WriteAll("first");
            s.WriteAll("second");
            Assert.AreEqual("second", s.ReadAll());
            CollectionAssert.AreEqual(new[] { _path }, Directory.GetFiles(Path.GetDirectoryName(_path)));
        }

        [Test]
        public void SettingAsideMovesTheFileKeepingItsTextAndNumbersRepeats()
        {
            var s = new FileSettingsStorage(_path);
            s.WriteAll("one");
            string first = s.SetAside("bad");
            Assert.AreEqual(_path + ".bad", first);
            Assert.IsFalse(s.Exists);
            Assert.AreEqual("one", File.ReadAllText(first));

            s.WriteAll("two");
            string second = s.SetAside("bad");
            Assert.AreEqual(_path + ".bad2", second);

            s.WriteAll("three");
            Assert.AreEqual(_path + ".bad3", s.SetAside("bad"));
            Assert.AreEqual("one", File.ReadAllText(first), "earlier set-asides are never overwritten");
            Assert.AreEqual("two", File.ReadAllText(second));

            s.WriteAll("four");
            Assert.AreEqual(_path + ".newer", s.SetAside("newer"));
        }

        [Test]
        public void ReadingOrSettingAsideWithNothingStoredFailsLoudly()
        {
            var s = new FileSettingsStorage(_path);
            Assert.Throws<DirectoryNotFoundException>(() => s.ReadAll());
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            Assert.Throws<FileNotFoundException>(() => s.ReadAll());
            Assert.Throws<FileNotFoundException>(() => s.SetAside("bad"));
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentException>(() => new FileSettingsStorage(""));
            Assert.Throws<ArgumentException>(() => new FileSettingsStorage("  "));
            Assert.Throws<ArgumentNullException>(() => new FileSettingsStorage(_path).WriteAll(null));
        }

        [Test]
        public void AStoreOnARealFileRoundTripsAndRecoversFromACorruptOne()
        {
            var storage = new FileSettingsStorage(_path);
            var store = new SettingsStore(storage);

            var custom = new GameSettings();
            custom.Graphics.Quality = QualityPreset.Low;
            custom.Audio.Voice = 0.4;
            custom.Controls.SetOverride("jump:0", "<Keyboard>/z");
            store.Save(custom);
            Assert.IsTrue(custom.SameValues(store.Load().Settings));

            File.WriteAllText(_path, "{ corrupt");
            SettingsLoadResult r = store.Load();
            Assert.AreEqual(SettingsLoadOutcome.Recovered, r.Outcome);
            Assert.AreEqual(_path + ".bad", r.SetAsideTo);
            Assert.AreEqual("{ corrupt", File.ReadAllText(_path + ".bad"));
            Assert.IsFalse(File.Exists(_path));

            store.Save(r.Settings);
            Assert.AreEqual(SettingsLoadOutcome.Loaded, store.Load().Outcome);
        }

        [Test]
        public void MemoryStorageBehavesLikeTheFileOne()
        {
            var m = new MemorySettingsStorage();
            Assert.IsFalse(m.Exists);
            Assert.Throws<InvalidOperationException>(() => m.ReadAll());
            Assert.Throws<InvalidOperationException>(() => m.SetAside("bad"));
            m.WriteAll("a");
            Assert.AreEqual("a", m.ReadAll());
            Assert.AreEqual("memory:bad1", m.SetAside("bad"));
            Assert.IsFalse(m.Exists);
            Assert.Throws<ArgumentNullException>(() => m.WriteAll(null));
        }
    }
}
