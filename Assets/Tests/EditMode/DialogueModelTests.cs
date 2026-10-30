using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The conversation screen's view of the core's dialogue: speaker, line, numbered answers with their checks, the typewriter, and the last line kept after the end.</summary>
    public class DialogueModelTests
    {
        static RpgCore Core() => new RpgCore(null, 20771, new MemorySaveStorage(), () => 1700000000000) { AutosaveEnabled = false };

        [Test]
        public void ANodeShowsItsSpeakerLineAndAnswersInOrder()
        {
            RpgCore core = Core();
            core.StartDialogue("dlg_kaz");
            var m = new DialogueModel();
            m.Show(core.GetDialogueNode());
            Assert.IsNotEmpty(m.Speaker);
            Assert.AreEqual(m.Speaker, m.Speaker.ToUpperInvariant());
            Assert.AreEqual((string)core.GetDialogueNode()["text"], m.Text);
            CollectionAssert.AreEqual(core.Dialogue.Options().Select(o => (string)o["id"]), m.Choices.Select(c => c.Id));
            Assert.IsFalse(m.Ended);

            DialogueChoiceView read = m.Choices.Single(c => c.Id == "k_o_read");
            Assert.AreEqual("INTELLIGENCE 6", read.Gate);
            Assert.IsTrue(read.Locked);
            Assert.AreEqual("INSUFFICIENT", read.Verdict);
            DialogueChoiceView open = m.Choices.First(c => c.Gate.Length == 0);
            Assert.AreEqual("", open.Verdict);
        }

        [Test]
        public void ThePayloadOfANodeEventWorksAsWellAsTheNode()
        {
            RpgCore core = Core();
            core.StartDialogue("dlg_kaz");
            var m = new DialogueModel();
            m.Show(new JsonObject { ["node"] = core.GetDialogueNode() });
            Assert.AreEqual((string)core.GetDialogueNode()["text"], m.Text);
        }

        [Test]
        public void NumberKeysPickOnlyUnlockedAnswersThatExist()
        {
            RpgCore core = Core();
            core.StartDialogue("dlg_kaz");
            var m = new DialogueModel();
            m.Show(core.GetDialogueNode());
            int locked = m.Choices.ToList().FindIndex(c => c.Locked) + 1;
            Assert.Greater(locked, 0, "Kaz has a locked answer for a new character");
            Assert.IsNull(m.ForKey(locked));
            int free = m.Choices.ToList().FindIndex(c => !c.Locked) + 1;
            Assert.AreEqual(m.Choices[free - 1].Id, m.ForKey(free).Value.Id);
            Assert.IsNull(m.ForKey(0));
            Assert.IsNull(m.ForKey(m.Choices.Count + 1));
            Assert.IsNull(m.ForKey(DialogueModel.NumberKeys + 1));
        }

        [Test]
        public void TheTypewriterRevealsAtThePrototypesSpeed()
        {
            var m = new DialogueModel();
            m.Show(new JsonObject { ["text"] = new string('x', 100), ["options"] = new List<object>() });
            Assert.AreEqual(0, m.Revealed(0));
            Assert.AreEqual(0, m.Revealed(-1));
            Assert.AreEqual(62, m.Revealed(1));
            Assert.AreEqual(100, m.Revealed(10));
        }

        [Test]
        public void TheLastLineStaysAfterTheEndOnlyWhenItHasWords()
        {
            var m = new DialogueModel();
            m.Show(new JsonObject { ["speaker"] = "Kaz", ["text"] = "Go on, then.", ["options"] = new List<object>() });
            Assert.IsTrue(m.End());
            Assert.IsTrue(m.Ended);
            Assert.IsEmpty(m.Choices);
            Assert.AreEqual("Go on, then.", m.Text);

            var silent = new DialogueModel();
            silent.Show(new JsonObject { ["text"] = "", ["options"] = new List<object>() });
            Assert.IsFalse(silent.End());
        }
    }
}
