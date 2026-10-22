using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class SettingsStoreTests
    {
        static GameSettings Custom()
        {
            var s = new GameSettings();
            s.Graphics.Quality = QualityPreset.High;
            s.Graphics.FieldOfView = 88;
            s.Audio.Music = 0.25;
            s.Gameplay.InvertY = true;
            s.Controls.SetOverride("jump:0", "<Keyboard>/z");
            return s;
        }

        [Test]
        public void NoFileGivesDefaultsWithoutANotice()
        {
            var store = new SettingsStore(new MemorySettingsStorage());
            SettingsLoadResult r = store.Load();
            Assert.AreEqual(SettingsLoadOutcome.Defaults, r.Outcome);
            Assert.IsNull(r.Notice);
            Assert.IsTrue(r.Settings.SameValues(new GameSettings()));
        }

        [Test]
        public void SavedSettingsLoadBackIdentically()
        {
            var storage = new MemorySettingsStorage();
            var store = new SettingsStore(storage);
            store.Save(Custom());
            Assert.IsTrue(storage.Exists);

            SettingsLoadResult r = new SettingsStore(storage).Load();
            Assert.AreEqual(SettingsLoadOutcome.Loaded, r.Outcome);
            Assert.IsNull(r.Notice);
            Assert.IsTrue(Custom().SameValues(r.Settings));
        }

        [Test]
        public void SavingWritesTheCurrentVersion()
        {
            var storage = new MemorySettingsStorage();
            new SettingsStore(storage).Save(new GameSettings());
            var root = (JsonObject)Json.Parse(storage.ReadAll());
            Assert.AreEqual((double)SettingsStore.CurrentVersion, Convert.ToDouble(root["version"]));
        }

        [TestCase("")]
        [TestCase("not json at all")]
        [TestCase("{\"version\":1,")]
        [TestCase("[]")]
        [TestCase("42")]
        [TestCase("{}")]
        [TestCase("{\"version\":\"1\"}")]
        [TestCase("{\"version\":0}")]
        [TestCase("{\"version\":1.5}")]
        [TestCase("{\"version\":-2}")]
        [TestCase("{\"version\":1,\"graphics\":{\"fieldOfView\":500}}")]
        [TestCase("{\"version\":1,\"audio\":{\"master\":\"loud\"}}")]
        public void AnUnusableFileIsSetAsideAndDefaultsAreUsedWithANotice(string text)
        {
            var storage = new MemorySettingsStorage(text);
            SettingsLoadResult r = new SettingsStore(storage).Load();

            Assert.AreEqual(SettingsLoadOutcome.Recovered, r.Outcome);
            Assert.IsTrue(r.Settings.SameValues(new GameSettings()));
            Assert.IsNotNull(r.Notice);
            StringAssert.Contains("default settings are in use", r.Notice);
            StringAssert.Contains(r.SetAsideTo, r.Notice, "the notice says where the file went");
            Assert.IsFalse(storage.Exists, "the unusable file is out of the way");
            Assert.AreEqual(1, storage.SetAsideLog.Count);
            Assert.AreEqual("bad: " + text, storage.SetAsideLog[0], "and kept intact for the user");
        }

        [Test]
        public void TheNoticeSaysWhatWasWrong()
        {
            var storage = new MemorySettingsStorage("{\"version\":1,\"graphics\":{\"fieldOfView\":500}}");
            StringAssert.Contains("graphics.fieldOfView: must be between 60 and 110, got 500", new SettingsStore(storage).Load().Notice);

            storage = new MemorySettingsStorage("{ nope }");
            StringAssert.Contains("could not be read", new SettingsStore(storage).Load().Notice);
        }

        [Test]
        public void AFileFromANewerBuildIsKeptUnderADifferentLabel()
        {
            string text = "{\"version\":3,\"graphics\":{\"quality\":\"Ludicrous\"}}";
            var storage = new MemorySettingsStorage(text);
            SettingsLoadResult r = new SettingsStore(storage, currentVersion: 2).Load();

            Assert.AreEqual(SettingsLoadOutcome.Recovered, r.Outcome);
            StringAssert.Contains("newer version", r.Notice);
            StringAssert.Contains("version 3", r.Notice);
            Assert.AreEqual("newer: " + text, storage.SetAsideLog[0]);
            Assert.IsFalse(storage.Exists);
        }

        [Test]
        public void SavingAfterARecoveryWritesAFreshFile()
        {
            var storage = new MemorySettingsStorage("garbage");
            var store = new SettingsStore(storage);
            store.Load();
            store.Save(Custom());
            Assert.IsTrue(Custom().SameValues(store.Load().Settings));
            Assert.AreEqual(1, storage.SetAsideLog.Count, "the bad file was only moved once");
        }

        [Test]
        public void OlderFilesAreMigratedStepByStepInOrder()
        {
            var calls = new List<int>();
            var migrations = new Dictionary<int, Func<JsonObject, JsonObject>>
            {
                [1] = doc => { calls.Add(1); return doc; },
                [2] = doc => { calls.Add(2); return doc; },
            };
            var storage = new MemorySettingsStorage("{\"version\":1,\"graphics\":{\"fieldOfView\":81}}");
            SettingsLoadResult r = new SettingsStore(storage, 3, migrations).Load();

            CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
            Assert.AreEqual(SettingsLoadOutcome.Migrated, r.Outcome);
            Assert.IsNull(r.Notice);
            Assert.AreEqual(81.0, r.Settings.Graphics.FieldOfView);
            Assert.IsTrue(storage.Exists, "migration does not touch the stored file");
        }

        [Test]
        public void AMigrationCanReshapeTheDocument()
        {
            // A made-up v1 -> v2 change: field of view used to be called "fov".
            var migrations = new Dictionary<int, Func<JsonObject, JsonObject>>
            {
                [1] = doc =>
                {
                    var g = (JsonObject)doc["graphics"];
                    g["fieldOfView"] = g["fov"];
                    g.Remove("fov");
                    return doc;
                },
            };
            var storage = new MemorySettingsStorage("{\"version\":1,\"graphics\":{\"fov\":95}}");
            SettingsLoadResult r = new SettingsStore(storage, 2, migrations).Load();
            Assert.AreEqual(SettingsLoadOutcome.Migrated, r.Outcome);
            Assert.AreEqual(95.0, r.Settings.Graphics.FieldOfView);
        }

        [Test]
        public void ASavedMigratedFileLoadsAsCurrent()
        {
            var migrations = new Dictionary<int, Func<JsonObject, JsonObject>> { [1] = doc => doc };
            var storage = new MemorySettingsStorage("{\"version\":1}");
            var store = new SettingsStore(storage, 2, migrations);
            SettingsLoadResult r = store.Load();
            store.Save(r.Settings);
            Assert.AreEqual(SettingsLoadOutcome.Loaded, store.Load().Outcome);
        }

        [Test]
        public void AMissingMigrationIsABugAndPropagates()
        {
            var storage = new MemorySettingsStorage("{\"version\":1}");
            var store = new SettingsStore(storage, 2);
            InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => store.Load());
            StringAssert.Contains("No settings migration from version 1 to 2", e.Message);
            Assert.IsTrue(storage.Exists, "the file is not moved aside for a bug in the game");
        }

        [Test]
        public void AMigrationThatThrowsOrReturnsNothingPropagates()
        {
            var thrower = new Dictionary<int, Func<JsonObject, JsonObject>> { [1] = doc => throw new IndexOutOfRangeException("boom") };
            Assert.Throws<IndexOutOfRangeException>(() => new SettingsStore(new MemorySettingsStorage("{\"version\":1}"), 2, thrower).Load());

            var nothing = new Dictionary<int, Func<JsonObject, JsonObject>> { [1] = doc => null };
            Assert.Throws<InvalidOperationException>(() => new SettingsStore(new MemorySettingsStorage("{\"version\":1}"), 2, nothing).Load());
        }

        [Test]
        public void AMigrationFindingBadDataSetsTheFileAside()
        {
            var migrations = new Dictionary<int, Func<JsonObject, JsonObject>>
            {
                [1] = doc => throw new SettingsFormatException("graphics", "cannot be upgraded"),
            };
            var storage = new MemorySettingsStorage("{\"version\":1}");
            SettingsLoadResult r = new SettingsStore(storage, 2, migrations).Load();
            Assert.AreEqual(SettingsLoadOutcome.Recovered, r.Outcome);
            StringAssert.Contains("graphics: cannot be upgraded", r.Notice);
        }

        [Test]
        public void IoErrorsAreNotSwallowed()
        {
            var store = new SettingsStore(new FailingStorage());
            Assert.Throws<System.IO.IOException>(() => store.Load());
            Assert.Throws<System.IO.IOException>(() => store.Save(new GameSettings()));
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => new SettingsStore(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SettingsStore(new MemorySettingsStorage(), 0));
            Assert.Throws<ArgumentNullException>(() => new SettingsStore(new MemorySettingsStorage()).Save(null));
        }

        sealed class FailingStorage : ISettingsStorage
        {
            public bool Exists => true;
            public string ReadAll() => throw new System.IO.IOException("disk on fire");
            public void WriteAll(string text) => throw new System.IO.IOException("disk full");
            public string SetAside(string label) => throw new System.IO.IOException("nope");
        }
    }
}
