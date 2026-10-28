using System;
using System.IO;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>Saves on disk: a file per slot in one folder, written whole or not at all, and the core's save carrying where the player stood.</summary>
    public class RpgSaveStorageTests
    {
        string _folder;

        [SetUp]
        public void SetUp() => _folder = Path.Combine(Path.GetTempPath(), "cypher-save-tests-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        [Test]
        public void ASlotIsAFileThatReadsBackAndListsBySlot()
        {
            var storage = new FolderSaveStorage(_folder);
            var saves = new SaveManager(storage, () => 1000);
            Assert.IsFalse(saves.Has("auto"), "an empty folder (not even made yet) has no saves");
            Assert.IsEmpty(saves.List());
            saves.Write("auto", new JsonObject { ["flags"] = new JsonObject { ["a"] = true } });
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "save_auto.json")));
            Assert.AreEqual(true, ((JsonObject)saves.Read("auto")["flags"])["a"]);
            saves.Write("second", new JsonObject());
            CollectionAssert.AreEquivalent(new[] { "auto", "second" }, saves.List().Select(s => s.Slot));
            Assert.IsFalse(Directory.GetFiles(_folder).Any(f => f.EndsWith(".tmp", StringComparison.Ordinal)), "no temporary file is left");
            saves.Remove("second");
            Assert.IsFalse(saves.Has("second"));
            Assert.IsTrue(saves.Has("auto"));
        }

        [Test]
        public void WritingAgainReplacesTheWholeFile()
        {
            var saves = new SaveManager(new FolderSaveStorage(_folder), () => 1000);
            saves.Write("auto", new JsonObject { ["big"] = new string('x', 5000) });
            saves.Write("auto", new JsonObject { ["small"] = 1.0 });
            JsonObject read = saves.Read("auto");
            Assert.IsFalse(read.ContainsKey("big"));
            Assert.AreEqual(1.0, read["small"]);
        }

        [Test]
        public void ASlotNameThatIsNotAFileNameOrAForeignKeyIsRefused()
        {
            var storage = new FolderSaveStorage(_folder);
            Assert.Throws<ArgumentException>(() => storage.SetItem(RpgSave.KeyPrefix + "../escape", "{}"));
            Assert.Throws<ArgumentException>(() => storage.SetItem(RpgSave.KeyPrefix + "a/b", "{}"));
            Assert.Throws<ArgumentException>(() => storage.GetItem("other:key"));
            Assert.Throws<ArgumentException>(() => new FolderSaveStorage(" "));
        }

        [Test]
        public void ACorruptFileThrowsRatherThanReadingAsEmpty()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "save_auto.json"), "{half a save");
            var saves = new SaveManager(new FolderSaveStorage(_folder), () => 1000);
            StringAssert.Contains("corrupt", Assert.Throws<InvalidOperationException>(() => saves.Read("auto")).Message);
        }

        [Test]
        public void TheCoresSaveCarriesTheWorldTheGameGivesItAndALoadHandsItBack()
        {
            var storage = new FolderSaveStorage(_folder);
            var core = new RpgCore(storage: storage, clock: () => 1000) { AutosaveEnabled = false };
            Assert.IsFalse(core.Serialize().ContainsKey("world"), "nothing is added unless the game asks");
            core.CaptureWorld = () => new JsonObject { ["player"] = new JsonObject { ["x"] = 12.5, ["z"] = -3.0, ["yaw"] = 90.0 } };
            core.AddXp(500);
            core.Save("auto");

            var loaded = new RpgCore(storage: storage, clock: () => 2000) { AutosaveEnabled = false };
            Assert.IsNull(loaded.LoadedWorld);
            Assert.IsTrue(loaded.Load("auto").Ok);
            Assert.AreEqual(core.Stats.Xp, loaded.Stats.Xp);
            Assert.AreEqual(12.5, ((JsonObject)loaded.LoadedWorld["player"])["x"]);
        }
    }
}
