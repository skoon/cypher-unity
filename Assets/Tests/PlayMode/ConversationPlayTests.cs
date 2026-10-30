#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using Cypher.Rpg;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>
    /// The conversation screen in the real Slice scene: talking to Kaz opens it and holds the player (not time), the line types out, an answer moves the
    /// conversation on, Esc ends it and gives the player back, a pause keeps it, and E does nothing while it is open.
    /// </summary>
    public class ConversationPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate(saves: new MemorySaveStorage());

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static RpgService Rpg => Object.FindAnyObjectByType<RpgService>();

        static UiRoot Ui => Object.FindAnyObjectByType<UiRoot>();

        static FirstPersonController Player => Object.FindAnyObjectByType<FirstPersonController>();

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator TalkToKaz()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            RpgLocation noodle = Rpg.Core.GetLocation("noodle");
            CityNav nav = CityNav.Build(Object.FindAnyObjectByType<Cypher.City.CityBuilder>().Slice.Plan);
            Player.Teleport(new Vector3((float)noodle.X, (float)nav.WalkY + 0.05f, (float)noodle.Z), 0f);
            Physics.SyncTransforms();
            yield return Frames(3);
            Assert.IsTrue(Rpg.Core.Interact().Ok, "Kaz is there to talk to");
            yield return Frames(3);
        }

        [UnityTest]
        public IEnumerator TalkingOpensTheConversationAndHoldsThePlayerButNotTime()
        {
            yield return TalkToKaz();
            Assert.AreEqual(UiScreen.Dialogue, Ui.Top);
            Assert.IsTrue(Ui.Conversation.IsShown);
            Assert.AreEqual(FlowModal.Interaction, SliceTestScene.Flow.Modal);
            Assert.IsFalse(Player.enabled, "the player stands still");
            Assert.AreEqual(1f, Time.timeScale, "the world goes on");
            Assert.AreEqual((string)Rpg.Core.GetDialogueNode()["text"], Ui.Conversation.Model.Text);
            Assert.Less(Ui.Conversation.ShownText.Length, Ui.Conversation.Model.Text.Length, "the line is still being typed");
            Ui.Conversation.Skip();
            yield return null;
            Assert.AreEqual(Ui.Conversation.Model.Text, Ui.Conversation.ShownText);
            CollectionAssert.AreEqual(Rpg.Core.Dialogue.Options().Select(o => (string)o["id"]), Ui.Conversation.Buttons.Cast<ChoiceButton>().Select(b => b.Choice.Id));
        }

        [UnityTest]
        public IEnumerator AnAnswerMovesTheConversationOnAndALockedOneDoesNot()
        {
            yield return TalkToKaz();
            string node = (string)Rpg.Core.GetDialogueNode()["nodeId"];
            ChoiceButton locked = Ui.Conversation.Buttons.Cast<ChoiceButton>().First(b => b.Choice.Locked);
            locked.Activate();
            yield return null;
            Assert.AreEqual(node, (string)Rpg.Core.GetDialogueNode()["nodeId"], "a locked answer is not given");

            ChoiceButton next = Ui.Conversation.Buttons.Cast<ChoiceButton>().First(b => !b.Choice.Locked && !b.Choice.Ends);
            next.Activate();
            yield return null;
            Assert.AreNotEqual(node, (string)Rpg.Core.GetDialogueNode()["nodeId"]);
            Assert.AreEqual((string)Rpg.Core.GetDialogueNode()["text"], Ui.Conversation.Model.Text, "the screen follows");
        }

        [UnityTest]
        public IEnumerator EscEndsTheConversationAndGivesThePlayerBack()
        {
            yield return TalkToKaz();
            Ui.OnBack(fromBButton: false);
            yield return null;
            Assert.IsFalse(Rpg.Core.Dialogue.Active);
            Assert.AreEqual(UiScreen.None, Ui.Top);
            Assert.IsFalse(Ui.Conversation.IsShown);
            Assert.AreEqual(FlowModal.None, SliceTestScene.Flow.Modal);
            Assert.IsTrue(Player.enabled);
            Assert.AreEqual(FlowState.Playing, SliceTestScene.Flow.State, "Esc left the conversation; it did not pause");
        }

        [UnityTest]
        public IEnumerator APauseKeepsTheConversation()
        {
            yield return TalkToKaz();
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            yield return null;
            Assert.AreEqual(UiScreen.Pause, Ui.Top);
            Assert.IsFalse(Ui.Conversation.IsShown);
            Assert.IsTrue(SliceTestScene.Flow.Resume());
            yield return null;
            Assert.AreEqual(UiScreen.Dialogue, Ui.Top, "back to the conversation");
            Assert.IsTrue(Ui.Conversation.IsShown);
            Assert.IsFalse(Player.enabled, "still held");
            Assert.IsTrue(Rpg.Core.Dialogue.Active);
        }

        [UnityTest]
        public IEnumerator TheTitleEndsAnOpenConversationsScreen()
        {
            yield return TalkToKaz();
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle());
            yield return Frames(2);
            Assert.AreEqual(UiScreen.None, Ui.GameScreen);
            Assert.AreEqual(FlowModal.None, SliceTestScene.Flow.Modal);
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(3);
            Assert.AreEqual(UiScreen.None, Ui.Top, "a new game starts with no screen open");
            Assert.IsTrue(Player.enabled);
        }
    }
}
#endif
