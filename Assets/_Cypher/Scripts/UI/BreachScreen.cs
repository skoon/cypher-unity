using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>A cell of the code matrix: its code, lit when it can be picked, struck out once picked.</summary>
    public sealed class BreachCellButton : FocusControl
    {
        readonly Label _code;

        public int Row { get; }
        public int Column { get; }

        public event Action<BreachCellButton> Picked;

        public BreachCellButton(int row, int column)
        {
            Row = row;
            Column = column;
            AddToClassList("cy-cell");
            _code = Ui.Text("", "cy-cell__code");
            Add(_code);
            ActivateSound = null;   // the screen plays the right sound for a good or a bad pick
        }

        public string Code
        {
            get => _code.text;
            set => _code.text = value;
        }

        public void SetLook(BreachCell look, bool onLine)
        {
            EnableInClassList("cy-cell--spent", look == BreachCell.Spent);
            EnableInClassList("cy-cell--live", look == BreachCell.Live);
            EnableInClassList("cy-cell--dim", look == BreachCell.Dim);
            EnableInClassList("cy-cell--line", onLine);
        }

        protected override void OnActivate() => Picked?.Invoke(this);
    }

    /// <summary>
    /// The breach protocol, after the prototype's <c>ui/Breach.js</c>: the timer, the buffer, the code matrix (picks alternate between the lit row and column), the
    /// daemons with the codes the buffer already matches lit, and a log. The core keeps the puzzle and the clock; this screen draws them and sends the picks. When
    /// the breach is over the outcome shows for a moment and the screen closes itself, as the prototype's does.
    /// </summary>
    public sealed class BreachScreen : MenuScreen
    {
        /// <summary>How long the outcome stays up before the screen closes (the prototype's 1.8 s).</summary>
        public const double OutcomeSeconds = 1.8;

        const int LogLines = 7;

        readonly BreachModel _model = new BreachModel();
        readonly Label _target = Ui.Text("", "cy-breach__target");
        readonly VisualElement _timeFill = Ui.Div("cy-breach__timefill");
        readonly Label _timeNum = Ui.Text("", "cy-breach__timenum");
        readonly Label _bufferCount = Ui.Text("", "cy-breach__sub");
        readonly VisualElement _buffer = Ui.Div("cy-breach__buffer");
        readonly VisualElement _matrix = Ui.Div("cy-breach__matrix");
        readonly VisualElement _daemons = Ui.Div("cy-breach__daemons");
        readonly VisualElement _log = Ui.Div("cy-breach__log");
        readonly Label _outcome = Ui.Display("", "cy-breach__outcome");
        readonly FocusGrid _grid;
        readonly List<List<BreachCellButton>> _cells = new List<List<BreachCellButton>>();
        readonly HashSet<string> _announced = new HashSet<string>();
        bool _ended;
        double _outcomeLeft;

        /// <summary>The player picked a cell.</summary>
        public event Action<int, int> CellPicked;

        /// <summary>The outcome has been shown long enough.</summary>
        public event Action Finished;

        public BreachModel Model => _model;
        public IReadOnlyList<IReadOnlyList<BreachCellButton>> Cells => _cells;
        public string Outcome => _outcome.text;

        public BreachScreen()
        {
            Root = Ui.Div("cy-screen", "cy-scrim", "cy-breach");
            var panel = Ui.Div("cy-breach__panel");

            var head = Ui.Div("cy-breach__head");
            head.Add(Ui.Display("BREACH PROTOCOL", "cy-breach__title"));
            head.Add(_target);
            head.Add(Ui.Text("ESC ABORT", "cy-breach__esc"));
            panel.Add(head);

            var timer = Ui.Div("cy-breach__timer");
            timer.Add(Ui.Text("BREACH TIME REMAINING", "cy-breach__sub"));
            var bar = Ui.Div("cy-breach__timebar");
            bar.Add(_timeFill);
            timer.Add(bar);
            timer.Add(_timeNum);
            panel.Add(timer);

            var body = Ui.Div("cy-breach__body");
            var left = Ui.Div("cy-breach__left");
            left.Add(_bufferCount);
            left.Add(_buffer);
            left.Add(Ui.Text("CODE MATRIX", "cy-breach__sub"));
            left.Add(_matrix);
            body.Add(left);
            var right = Ui.Div("cy-breach__right");
            right.Add(Ui.Text("SEQUENCE REQUIRED TO UPLOAD", "cy-breach__sub"));
            right.Add(_daemons);
            right.Add(_log);
            body.Add(right);
            panel.Add(body);
            panel.Add(_outcome);
            Root.Add(panel);
            _grid = new FocusGrid(Root);
        }

        /// <summary>A new breach: build the matrix and the daemon list.</summary>
        public void Begin(JsonObject session)
        {
            _model.Show(session);
            _target.text = "// " + _model.TargetName;
            _matrix.Clear();
            _cells.Clear();
            for (int r = 0; r < _model.Size; r++)
            {
                var rowEl = Ui.Div("cy-breach__row");
                var row = new List<BreachCellButton>();
                for (int c = 0; c < _model.Size; c++)
                {
                    var cell = new BreachCellButton(r, c) { Code = _model.Matrix[r][c] };
                    cell.Picked += OnPicked;
                    row.Add(cell);
                    rowEl.Add(cell);
                }
                _cells.Add(row);
                _matrix.Add(rowEl);
            }
            _grid.SetRows(_cells);
            _log.Clear();
            _announced.Clear();
            Log("CONNECTION ESTABLISHED", "ok");
            Log($"DAEMONS AVAILABLE: {_model.Daemons.Count}", "");
            _outcome.text = "";
            _ended = false;
            _outcomeLeft = 0;
            Root.EnableInClassList("cy-breach--done", false);
            Root.EnableInClassList("cy-breach--won", false);
            Draw();
            UiFonts.Apply(Root);
        }

        /// <summary>The breach moved on (a pick, or its end): redraw from the core's view, log what changed.</summary>
        public void Sync(JsonObject session)
        {
            int before = _model.Picks.Count;
            _model.Show(session);
            for (int i = before; i < _model.Picks.Count; i++) Log($"INJECT {_model.Picks[i].Code} @ {_model.Picks[i].R}{_model.Picks[i].C}", "");
            foreach (BreachDaemonView d in _model.Daemons)
            {
                if (d.Solved && _announced.Add(d.Id + ":up")) Log($"{d.Name} :: UPLOADED", "ok");
                else if (d.Failed && _announced.Add(d.Id + ":out")) Log($"{d.Name} :: BUFFER OVERFLOW", "bad");
            }
            if (!_model.Running && !_ended)
            {
                _ended = true;
                _outcome.text = _model.Outcome();
                Log(_model.Outcome(), _model.State == "done" ? "ok" : "bad");
                _outcomeLeft = OutcomeSeconds;
                Root.EnableInClassList("cy-breach--done", true);
                Root.EnableInClassList("cy-breach--won", _model.State == "done");
                UiSound.Raise(_model.State == "done" ? UiSound.Confirm : UiSound.Error);
            }
            Draw();
            UiFonts.Apply(Root);
            // the picked cell is spent now: move on to a live one, as the next pick must be one
            if (_model.Running && Root.panel?.focusController?.focusedElement is BreachCellButton f && _model.Cell(f.Row, f.Column) != BreachCell.Live) FocusDefault();
        }

        void OnPicked(BreachCellButton cell)
        {
            if (_model.Cell(cell.Row, cell.Column) != BreachCell.Live)
            {
                UiSound.Raise(UiSound.Error);
                return;
            }
            UiSound.Raise(UiSound.Select);
            CellPicked?.Invoke(cell.Row, cell.Column);
        }

        void Draw()
        {
            for (int r = 0; r < _cells.Count; r++)
            {
                for (int c = 0; c < _cells[r].Count; c++) _cells[r][c].SetLook(_model.Cell(r, c), _model.OnLine(r, c));
            }
            _bufferCount.text = $"BUFFER  {_model.Buffer.Count}/{_model.BufferSize}";
            _buffer.Clear();
            for (int i = 0; i < _model.BufferSize; i++)
            {
                var slot = Ui.Div("cy-breach__slot");
                if (i < _model.Buffer.Count)
                {
                    slot.AddToClassList("cy-breach__slot--filled");
                    slot.Add(Ui.Text(_model.Buffer[i], "cy-breach__slotcode"));
                }
                else if (i == _model.Buffer.Count && _model.Running)
                {
                    slot.AddToClassList("cy-breach__slot--next");
                }
                _buffer.Add(slot);
            }
            _daemons.Clear();
            foreach (BreachDaemonView d in _model.Daemons)
            {
                var row = Ui.Div("cy-daemon");
                if (d.Solved) row.AddToClassList("cy-daemon--solved");
                if (d.Failed) row.AddToClassList("cy-daemon--failed");
                var codes = Ui.Div("cy-daemon__codes");
                for (int k = 0; k < d.Sequence.Count; k++)
                {
                    Label code = Ui.Text(d.Sequence[k], "cy-daemon__code");
                    if (k < d.Matched) code.AddToClassList("cy-daemon__code--hot");
                    codes.Add(code);
                }
                row.Add(codes);
                var meta = Ui.Div("cy-daemon__meta");
                meta.Add(Ui.Text(d.Name, "cy-daemon__name"));
                meta.Add(Ui.Text(d.Desc, "cy-daemon__desc"));
                row.Add(meta);
                _daemons.Add(row);
            }
        }

        void Log(string text, string tone)
        {
            Label line = Ui.Text("> " + text, "cy-breach__line");
            if (tone.Length > 0) line.AddToClassList("cy-breach__line--" + tone);
            _log.Add(line);
            while (_log.childCount > LogLines) _log.RemoveAt(0);
        }

        /// <summary>The clock (read from the core every frame, not only on picks) and the outcome's countdown; <paramref name="dt"/> in real seconds.</summary>
        public void Update(double timeLeft, double dt)
        {
            double share = _model.TimeLimit > 0 ? Math.Max(0, Math.Min(1, timeLeft / _model.TimeLimit)) : 0;
            _timeFill.style.width = Length.Percent((float)(share * 100));
            _timeNum.text = timeLeft.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            Root.EnableInClassList("cy-breach--urgent", _model.Running && share < BreachModel.UrgentShare);
            if (_ended && _outcomeLeft > 0)
            {
                _outcomeLeft -= dt;
                if (_outcomeLeft <= 0) Finished?.Invoke();
            }
        }

        public override void FocusDefault()
        {
            for (int r = 0; r < _cells.Count; r++)
            {
                for (int c = 0; c < _cells[r].Count; c++)
                {
                    if (_model.Cell(r, c) != BreachCell.Live) continue;
                    _cells[r][c].Focus();
                    return;
                }
            }
            _grid.FocusFirst();
        }
    }
}
