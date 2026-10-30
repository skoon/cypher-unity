using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>An answer in a conversation: its number, the check in front of it if any (with whether the player clears it), and the words.</summary>
    public sealed class ChoiceButton : FocusControl
    {
        public DialogueChoiceView Choice { get; }

        public event Action<DialogueChoiceView> Picked;

        public ChoiceButton(int number, DialogueChoiceView choice)
        {
            Choice = choice;
            AddToClassList("cy-choice");
            if (choice.Gate.Length > 0) AddToClassList("cy-choice--gated");
            if (choice.Ends) AddToClassList("cy-choice--exit");
            Add(Ui.Text(number.ToString(), "cy-choice__n"));
            var body = Ui.Div("cy-choice__body");
            if (choice.Gate.Length > 0)
            {
                var gate = Ui.Div("cy-choice__gate");
                gate.Add(Ui.Text(choice.Gate, "cy-choice__check"));
                gate.Add(Ui.Text(choice.Verdict, "cy-choice__verdict"));
                body.Add(gate);
            }
            body.Add(Ui.Text(choice.Text, "cy-choice__text"));
            Add(body);
            // a locked answer can be read and focused, but not given
            if (choice.Locked)
            {
                AddToClassList("cy-choice--locked");
                ActivateSound = UiSound.Error;
            }
        }

        protected override void OnActivate()
        {
            if (!Choice.Locked) Picked?.Invoke(Choice);
        }
    }

    /// <summary>
    /// A conversation, after the prototype's <c>ui/Dialogue.js</c>: a portrait, the speaker and the conversation's title, the line typed out at the prototype's speed
    /// (Space or a click shows it all), and the numbered answers (1 to 5, or arrows and Submit), checks marked passed or insufficient. When the conversation ends on a
    /// line with nothing to answer, the line stays with LEAVE until the player has read it.
    /// </summary>
    public sealed class ConversationScreen : MenuScreen
    {
        readonly DialogueModel _model = new DialogueModel();
        readonly Label _speaker = Ui.Display("", "cy-talk__speaker");
        readonly Label _title = Ui.Text("", "cy-talk__title");
        readonly Label _text = Ui.Text("", "cy-talk__text");
        readonly VisualElement _choices = Ui.Div("cy-talk__choices");
        readonly List<FocusControl> _buttons = new List<FocusControl>();
        readonly FocusGrid _grid;
        double _typing;

        /// <summary>The player gave an answer.</summary>
        public event Action<string> AnswerChosen;

        /// <summary>The player left the ended conversation's last line.</summary>
        public event Action LeaveChosen;

        public DialogueModel Model => _model;
        public string ShownText => _text.text;
        public IReadOnlyList<FocusControl> Buttons => _buttons;

        public ConversationScreen()
        {
            Root = Ui.Div("cy-screen", "cy-talk");
            var panel = Ui.Div("cy-talk__panel");
            var portrait = Ui.Div("cy-talk__portrait");
            portrait.generateVisualContent += DrawPortrait;
            panel.Add(portrait);
            var right = Ui.Div("cy-talk__right");
            var plate = Ui.Div("cy-talk__plate");
            plate.Add(_speaker);
            plate.Add(_title);
            right.Add(plate);
            _text.RegisterCallback<ClickEvent>(_ => Skip());
            right.Add(_text);
            right.Add(_choices);
            var foot = Ui.Div("cy-talk__foot");
            foot.Add(Ui.Text("1-5 ANSWER  ·  SPACE SKIP  ·  ESC LEAVE", "cy-talk__hint"));
            right.Add(foot);
            panel.Add(right);
            Root.Add(panel);
            _grid = new FocusGrid(Root);
        }

        /// <summary>Show a node of the core's conversation.</summary>
        public void Show(JsonObject node)
        {
            _model.Show(node);
            _speaker.text = _model.Speaker;
            _title.text = _model.Title;
            _typing = 0;
            _text.text = "";
            Build();
        }

        /// <summary>The conversation ended. True when the screen stays for its last line.</summary>
        public bool End()
        {
            bool stay = _model.End();
            if (stay) Build();
            return stay;
        }

        void Build()
        {
            _choices.Clear();
            _buttons.Clear();
            if (_model.Ended)
            {
                var leave = new MenuButton("LEAVE", compact: true) { ActivateSound = UiSound.Back };
                leave.Pressed += () => LeaveChosen?.Invoke();
                _buttons.Add(leave);
                _choices.Add(leave);
            }
            else
            {
                for (int i = 0; i < _model.Choices.Count; i++)
                {
                    var b = new ChoiceButton(i + 1, _model.Choices[i]);
                    b.Picked += c => AnswerChosen?.Invoke(c.Id);
                    _buttons.Add(b);
                    _choices.Add(b);
                }
            }
            var rows = new List<IEnumerable<VisualElement>>();
            foreach (FocusControl b in _buttons) rows.Add(new VisualElement[] { b });
            _grid.SetRows(rows);
            UiFonts.Apply(_choices);
            if (IsShown) Root.schedule.Execute(FocusDefault);
        }

        public void Skip() => _typing = 1e6;

        /// <summary>Type the line on and take the number keys; <paramref name="dt"/> in real seconds.</summary>
        public void Update(double dt)
        {
            _typing += dt;
            int shown = _model.Revealed(_typing);
            if (_text.text.Length != shown) _text.text = _model.Text.Substring(0, shown);
            Root.EnableInClassList("cy-talk--typing", shown < _model.Text.Length);

            Keyboard k = Keyboard.current;
            if (k == null) return;
            if (k.spaceKey.wasPressedThisFrame) Skip();
            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
            for (int i = 0; i < digits.Length; i++)
            {
                if (!k[digits[i]].wasPressedThisFrame) continue;
                if (_model.Ended)
                {
                    if (i == 0) LeaveChosen?.Invoke();
                }
                else if (_model.ForKey(i + 1) is DialogueChoiceView c)
                {
                    UiSound.Raise(UiSound.Select);
                    AnswerChosen?.Invoke(c.Id);
                }
                else if (i < _model.Choices.Count)
                {
                    UiSound.Raise(UiSound.Error);
                }
            }
        }

        public override void FocusDefault()
        {
            foreach (FocusControl b in _buttons)
            {
                if (b is ChoiceButton c && c.Choice.Locked) continue;
                b.Focus();
                return;
            }
            if (_buttons.Count > 0) _buttons[0].Focus();
        }

        /// <summary>A face drawn in code, as the prototype's is: a bevelled frame, shoulders, a head, a dark crop of hair, a magenta eye implant and jaw plating.</summary>
        static void DrawPortrait(MeshGenerationContext ctx)
        {
            Rect r = ctx.visualElement.contentRect;
            if (r.width <= 0 || r.height <= 0) return;
            Painter2D p = ctx.painter2D;
            float sx = r.width / 160f, sy = r.height / 200f;
            Vector2 P(float x, float y) => new Vector2(x * sx, y * sy);
            void Poly(Color c, params float[] xy)
            {
                p.fillColor = c;
                p.BeginPath();
                p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                p.ClosePath();
                p.Fill();
            }
            var cyan = new Color(0.18f, 0.95f, 1f);
            var deep = new Color(0.11f, 0.5f, 0.61f);
            float[] frame = { 16, 6, 144, 6, 154, 16, 154, 184, 144, 194, 16, 194, 6, 184, 6, 16 };
            Poly(new Color(0.03f, 0.075f, 0.11f), frame);
            Poly(new Color(deep.r, deep.g, deep.b, 0.55f), 12, 200, 20, 168, 50, 156, 80, 156, 110, 156, 140, 168, 148, 200);   // shoulders
            Poly(new Color(cyan.r * 0.8f, cyan.g * 0.8f, cyan.b * 0.8f, 0.9f), 80, 34, 104, 40, 114, 72, 108, 100, 92, 118, 80, 120, 68, 118, 52, 100, 46, 72, 56, 40);   // head
            Poly(new Color(0.016f, 0.027f, 0.05f, 0.9f), 46, 66, 52, 44, 66, 34, 80, 32, 94, 34, 108, 44, 114, 66, 100, 56, 80, 52, 60, 56);   // hair
            Poly(new Color(1f, 0.18f, 0.58f, 0.9f), 88, 70, 114, 70, 114, 82, 88, 82);   // optic implant
            Poly(new Color(0.04f, 0.09f, 0.125f, 0.85f), 58, 106, 102, 106, 96, 120, 64, 120);   // jaw plating
            Poly(cyan, 44, 92, 49, 92, 49, 97, 44, 97);
            Poly(cyan, 44, 100, 49, 100, 49, 105, 44, 105);
            p.lineWidth = 1.4f;
            p.strokeColor = new Color(cyan.r, cyan.g, cyan.b, 0.55f);
            p.BeginPath();
            p.MoveTo(P(frame[0], frame[1]));
            for (int i = 2; i < frame.Length; i += 2) p.LineTo(P(frame[i], frame[i + 1]));
            p.ClosePath();
            p.Stroke();
            p.lineWidth = 2.4f;
            p.strokeColor = new Color(1f, 0.18f, 0.58f, 0.9f);
            p.BeginPath();
            p.MoveTo(P(6, 60));
            p.LineTo(P(6, 16));
            p.LineTo(P(16, 6));
            p.LineTo(P(56, 6));
            p.Stroke();
        }
    }
}
