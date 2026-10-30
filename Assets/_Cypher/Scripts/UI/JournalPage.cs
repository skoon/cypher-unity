using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>
    /// The journal, after the prototype's: the jobs on the left in their sections (active with the tracked one first, available, finished), the chosen job on the
    /// right (stage, objectives with distances, time left, ending, pay). Submit on an active job, or TRACK, tracks it.
    /// </summary>
    public sealed class JournalPage : IMenuPage
    {
        readonly VisualElement _list = Ui.Div("cy-journal__list");
        readonly ScrollView _listScroll = new ScrollView(ScrollViewMode.Vertical);
        readonly VisualElement _detail = Ui.Div("cy-journal__detail");
        readonly FocusGrid _grid;
        readonly List<ListRow> _rows = new List<ListRow>();
        RpgCore _core;
        double _px, _pz;
        string _signature;

        public VisualElement Root { get; } = Ui.Div("cy-page", "cy-journal");

        /// <summary>The job whose detail shows.</summary>
        public string Selected { get; private set; }

        public IReadOnlyList<ListRow> Rows => _rows;

        public JournalDetail Detail { get; private set; }

        public MenuButton TrackButton { get; private set; }

        public JournalPage()
        {
            var side = Ui.Div("cy-journal__side");
            side.Add(Ui.Text("JOBS", "cy-menu__sub"));
            _listScroll.AddToClassList("cy-journal__scroll");
            _listScroll.Add(_list);
            side.Add(_listScroll);
            Root.Add(side);
            Root.Add(_detail);
            _grid = new FocusGrid(Root);
        }

        public void Refresh(RpgCore core, double playerX, double playerZ)
        {
            _core = core;
            _px = playerX;
            _pz = playerZ;
            List<JournalEntry> entries = JournalModel.Entries(core.GetQuests());
            if (Selected == null || entries.All(e => e.Id != Selected)) Selected = entries.FirstOrDefault().Id;
            string sig = string.Join("~", entries.Select(e => $"{e.Id}|{e.Section}|{e.Progress}|{e.Tracked}"));
            if (sig != _signature)
            {
                _signature = sig;
                BuildList(entries);
            }
            ShowDetail();
        }

        void BuildList(List<JournalEntry> entries)
        {
            _list.Clear();
            _rows.Clear();
            string section = null;
            foreach (JournalEntry e in entries)
            {
                if (e.Section != section)
                {
                    section = e.Section;
                    _list.Add(Ui.Text(section, "cy-journal__section"));
                }
                var row = new ListRow(e.Id, "cy-job");
                if (e.Tracked) row.AddToClassList("cy-job--tracked");
                if (e.Section == "FINISHED") row.AddToClassList("cy-job--done");
                var head = Ui.Div("cy-job__head");
                head.Add(Ui.Text(e.Type, "cy-job__type"));
                head.Add(Ui.Text(e.Level > 0 ? $"LV {e.Level}" : "", "cy-job__level"));
                row.Add(head);
                row.Add(Ui.Display(e.Title, "cy-job__title"));
                row.Add(Ui.Text(e.Progress, "cy-job__progress"));
                row.Selected += r =>
                {
                    Selected = r.Id;
                    ShowDetail();
                };
                row.Chosen += r => Track(r.Id);
                _rows.Add(row);
                _list.Add(row);
            }
            _grid.SetRows(_rows.Select(r => new VisualElement[] { r }));
        }

        void ShowDetail()
        {
            foreach (ListRow r in _rows) r.EnableInClassList("cy-row--selected", r.Id == Selected);
            _detail.Clear();
            TrackButton = null;
            if (Selected == null)
            {
                _detail.Add(Ui.Text("NO JOBS YET", "cy-journal__empty"));
                Detail = null;
                return;
            }
            string state = _core.Quests.GetState(Selected).State;
            JsonObject quest = state == "inactive" ? _core.Quests.Available().First(q => (string)q["id"] == Selected) : _core.GetQuest(Selected);
            JournalDetail d = JournalModel.Detail(quest, _core.Quests.Def(Selected), _px, _pz);
            Detail = d;

            var head = Ui.Div("cy-jd__head");
            head.Add(Ui.Text(d.Type, "cy-jd__type"));
            if (d.Level > 0) head.Add(Ui.Text($"LEVEL {d.Level}", "cy-jd__level"));
            head.Add(Ui.Text(d.Status, "cy-jd__status", "cy-jd__status--" + d.Status.ToLowerInvariant()));
            if (d.Tracked) head.Add(Ui.Text("TRACKED", "cy-jd__tracked"));
            _detail.Add(head);
            _detail.Add(Ui.Display(d.Title, "cy-jd__title"));
            if (d.Giver.Length > 0) _detail.Add(Ui.Text("CLIENT // " + d.Giver, "cy-jd__client"));
            _detail.Add(Ui.Text(d.Summary, "cy-jd__summary"));
            if (d.Hint.Length > 0) _detail.Add(Ui.Text(d.Hint, "cy-jd__hint"));

            if (!string.IsNullOrEmpty(d.StageTitle) || !string.IsNullOrEmpty(d.StageText))
            {
                _detail.Add(Ui.Text(d.StageTitle.Length > 0 ? d.StageTitle : "NOW", "cy-menu__sub"));
                if (!string.IsNullOrEmpty(d.StageText)) _detail.Add(Ui.Text(d.StageText, "cy-jd__stage"));
            }
            if (d.Objectives.Count > 0)
            {
                _detail.Add(Ui.Text("OBJECTIVES" + (d.TimeLeft != null ? "   ·   TIME LEFT " + d.TimeLeft : ""), "cy-menu__sub"));
                foreach (var o in d.Objectives)
                {
                    var step = Ui.Div("cy-jd__step");
                    if (o.Done) step.AddToClassList("cy-jd__step--done");
                    if (o.Active) step.AddToClassList("cy-jd__step--active");
                    if (o.Optional) step.AddToClassList("cy-jd__step--optional");
                    step.Add(Ui.Div("cy-jd__box"));
                    step.Add(Ui.Text(o.Optional ? o.Text + "  (OPTIONAL)" : o.Text, "cy-jd__steptext"));
                    if (o.Distance.Length > 0) step.Add(Ui.Text(o.Distance, "cy-jd__dist"));
                    _detail.Add(step);
                }
            }
            if (!string.IsNullOrEmpty(d.OutcomeTitle))
            {
                _detail.Add(Ui.Text("ENDING // " + d.OutcomeTitle, "cy-menu__sub"));
                if (!string.IsNullOrEmpty(d.OutcomeSummary)) _detail.Add(Ui.Text(d.OutcomeSummary, "cy-jd__stage"));
            }
            if (d.Rewards.Count > 0)
            {
                _detail.Add(Ui.Text("PAID", "cy-menu__sub"));
                var pay = Ui.Div("cy-jd__rewards");
                foreach (string line in d.Rewards) pay.Add(Ui.Text(line, "cy-jd__reward"));
                _detail.Add(pay);
            }
            if (d.CanTrack)
            {
                TrackButton = new MenuButton("TRACK JOB", compact: true) { focusable = false };
                string id = d.Id;
                TrackButton.Pressed += () => Track(id);
                _detail.Add(TrackButton);
            }
            UiFonts.Apply(_detail);
        }

        void Track(string id)
        {
            if (_core.Quests.GetState(id).State != "active")
            {
                UiSound.Raise(UiSound.Error);
                return;
            }
            _core.TrackQuest(id);
            Selected = id;
            Refresh(_core, _px, _pz);
            _rows.FirstOrDefault(r => r.Id == id)?.Focus();   // the list was rebuilt
        }

        public void FocusDefault()
        {
            ListRow row = _rows.FirstOrDefault(r => r.Id == Selected) ?? _rows.FirstOrDefault();
            row?.Focus();
        }
    }
}
