using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cypher.Core;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>
    /// The character sheet, after the prototype's: the level, XP and street cred, the attributes with their pips and a + for each point to spend; the chosen
    /// attribute's perk tree (the core's real one, by tier), or the chrome in each slot of the body; and the chosen perk or chrome with what it needs and the
    /// button that buys or removes it.
    /// </summary>
    public sealed class CharacterPage : IMenuPage
    {
        public const string ChromeKey = "chrome";

        readonly Label _level = Ui.Display("", "cy-ch__level");
        readonly VisualElement _xpFill = Ui.Div("cy-ch__fill");
        readonly Label _xp = Ui.Text("", "cy-ch__xp");
        readonly VisualElement _credFill = Ui.Div("cy-ch__fill", "cy-ch__fill--cred");
        readonly Label _cred = Ui.Text("", "cy-ch__xp");
        readonly Label _attrHead = Ui.Text("", "cy-menu__sub");
        readonly VisualElement _attrs = Ui.Div("cy-ch__attrs");
        readonly Label _middleHead = Ui.Text("", "cy-menu__sub");
        readonly VisualElement _middle = Ui.Div("cy-ch__middle");
        readonly ScrollView _middleScroll = new ScrollView(ScrollViewMode.Vertical);
        readonly VisualElement _detail = Ui.Div("cy-ch__detail");
        readonly FocusGrid _grid;
        readonly List<ListRow> _attrRows = new List<ListRow>();
        readonly List<MenuButton> _plusButtons = new List<MenuButton>();
        readonly List<ListRow> _middleRows = new List<ListRow>();
        readonly List<MenuButton> _detailButtons = new List<MenuButton>();
        RpgCore _core;

        public VisualElement Root { get; } = Ui.Div("cy-page", "cy-ch");

        /// <summary>The attribute whose tree shows, or <see cref="ChromeKey"/>.</summary>
        public string Section { get; private set; } = "body";

        /// <summary>The perk or chrome whose detail shows.</summary>
        public string Selected { get; private set; }

        public IReadOnlyList<ListRow> AttributeRows => _attrRows;
        public IReadOnlyList<MenuButton> PlusButtons => _plusButtons;
        public IReadOnlyList<ListRow> MiddleRows => _middleRows;
        public IReadOnlyList<MenuButton> DetailButtons => _detailButtons;

        /// <summary>A rule refused; the screen shows why.</summary>
        public event Action<string> Refused;

        public CharacterPage()
        {
            var side = Ui.Div("cy-ch__side");
            var card = Ui.Div("cy-ch__card");
            card.Add(_level);
            card.Add(Ui.Text("LEVEL", "cy-ch__levellabel"));
            var xpBar = Ui.Div("cy-ch__bar");
            xpBar.Add(_xpFill);
            card.Add(_xp);
            card.Add(xpBar);
            var credBar = Ui.Div("cy-ch__bar");
            credBar.Add(_credFill);
            card.Add(_cred);
            card.Add(credBar);
            side.Add(card);
            side.Add(_attrHead);
            side.Add(_attrs);
            Root.Add(side);
            var main = Ui.Div("cy-ch__main");
            main.Add(_middleHead);
            _middleScroll.AddToClassList("cy-ch__scroll");
            _middleScroll.Add(_middle);
            main.Add(_middleScroll);
            Root.Add(main);
            Root.Add(_detail);
            _grid = new FocusGrid(Root);
        }

        public void Refresh(RpgCore core, double playerX, double playerZ)
        {
            _core = core;
            CharacterModel.Sheet s = CharacterModel.Read(core);
            _level.text = s.Level.ToString(CultureInfo.InvariantCulture);
            _xp.text = s.Level >= s.LevelCap ? "XP  MAX" : $"XP  {s.XpInto:#,0} / {s.XpToNext:#,0}";
            _xpFill.style.width = Length.Percent((float)(s.XpShare * 100));
            _cred.text = $"STREET CRED  {s.StreetCred}";
            _credFill.style.width = Length.Percent((float)(s.CredShare * 100));
            _attrHead.text = $"ATTRIBUTES   {s.AttributePoints} PT   ·   PERKS   {s.PerkPoints} PT";
            BuildAttributes(s);
            if (Section == ChromeKey) BuildChrome();
            else BuildTree();
            BuildGrid();
            UiFonts.Apply(Root);
        }

        void BuildAttributes(CharacterModel.Sheet s)
        {
            _attrs.Clear();
            _attrRows.Clear();
            _plusButtons.Clear();
            foreach (AttributeView a in s.Attributes)
            {
                var line = Ui.Div("cy-ch__attrline");
                var row = new ListRow(a.Key, "cy-attr");
                if (a.Key == Section) row.AddToClassList("cy-row--selected");
                row.Add(Ui.Text(a.Label, "cy-attr__label"));
                var pips = Ui.Div("cy-attr__pips");
                for (int i = 0; i < a.Max; i++) pips.Add(Ui.Div("cy-pip", i < a.Value ? "cy-pip--on" : "cy-pip--off"));
                row.Add(pips);
                row.Add(Ui.Text(a.Value.ToString(CultureInfo.InvariantCulture), "cy-attr__value"));
                row.Selected += r => Show(r.Id);
                row.Chosen += r => Show(r.Id);
                var plus = new MenuButton("+", compact: true) { ActivateSound = null };
                plus.AddToClassList("cy-attr__plus");
                plus.SetEnabled(a.CanRaise);
                string key = a.Key;
                plus.Pressed += () => Raise(key);
                _attrRows.Add(row);
                _plusButtons.Add(plus);
                line.Add(row);
                line.Add(plus);
                _attrs.Add(line);
            }
            var chrome = new ListRow(ChromeKey, "cy-attr", "cy-attr--chrome");
            if (Section == ChromeKey) chrome.AddToClassList("cy-row--selected");
            chrome.Add(Ui.Text("CHROME", "cy-attr__label"));
            (int used, int cap, _) = CharacterModel.Chrome(_core);
            chrome.Add(Ui.Text($"{used}/{cap}", "cy-attr__value"));
            chrome.Selected += r => Show(r.Id);
            chrome.Chosen += r => Show(r.Id);
            _attrRows.Add(chrome);
            _attrs.Add(chrome);
        }

        /// <summary>Show an attribute's tree, or the chrome.</summary>
        public void Show(string section)
        {
            if (section != ChromeKey && !RpgStats.Attributes.Contains(section)) throw new ArgumentOutOfRangeException(nameof(section), section, "no such section of the character sheet");
            if (section == Section) return;
            Section = section;
            Selected = null;
            foreach (ListRow r in _attrRows) r.EnableInClassList("cy-row--selected", r.Id == Section);
            if (Section == ChromeKey) BuildChrome();
            else BuildTree();
            BuildGrid();
            UiFonts.Apply(Root);
        }

        void Raise(string attribute)
        {
            RpgResult r = _core.SpendAttributePoint(attribute);
            if (!r.Ok)
            {
                UiSound.Raise(UiSound.Error);
                Refused?.Invoke(InventoryModel.Reason(r.Reason));
                return;
            }
            UiSound.Raise(UiSound.Confirm);
            Refresh(_core, 0, 0);
            int i = RpgStats.Attributes.ToList().IndexOf(attribute);
            if (_plusButtons[i].enabledSelf) _plusButtons[i].Focus();
            else _attrRows[i].Focus();
        }

        void BuildTree()
        {
            List<PerkView> perks = CharacterModel.Tree(_core, Section);
            _middleHead.text = $"PERK TREE // {RpgStats.AttributeMeta[Section].Label.ToUpperInvariant()}";
            if (Selected == null || perks.All(p => p.Id != Selected)) Selected = perks.FirstOrDefault()?.Id;
            _middle.Clear();
            _middleRows.Clear();
            int tier = -1;
            foreach (PerkView p in perks)
            {
                if (p.Tier != tier)
                {
                    tier = p.Tier;
                    _middle.Add(Ui.Text($"TIER {tier}", "cy-ch__tier"));
                }
                var row = new ListRow(p.Id, "cy-perk");
                if (p.Owned) row.AddToClassList("cy-perk--owned");
                if (p.Blocked != null && !p.Owned) row.AddToClassList("cy-perk--locked");
                if (p.Blocked == null) row.AddToClassList("cy-perk--open");
                row.Add(Ui.Text(p.Name, "cy-perk__name"));
                row.Add(Ui.Text($"{p.Rank}/{p.MaxRank}", "cy-perk__rank"));
                row.Selected += r =>
                {
                    Selected = r.Id;
                    ShowPerk();
                    BuildGrid();
                };
                row.Chosen += r => Buy(r.Id);
                _middleRows.Add(row);
                _middle.Add(row);
            }
            ShowPerk();
        }

        void ShowPerk()
        {
            foreach (ListRow r in _middleRows) r.EnableInClassList("cy-row--selected", r.Id == Selected);
            _detail.Clear();
            _detailButtons.Clear();
            PerkView p = Selected != null ? CharacterModel.Tree(_core, Section).FirstOrDefault(x => x.Id == Selected) : null;
            if (p == null) return;
            _detail.Add(Ui.Text(p.State, "cy-pd__state", "cy-pd__state--" + p.State.ToLowerInvariant()));
            _detail.Add(Ui.Display(p.Name, "cy-pd__name"));
            _detail.Add(Ui.Text($"RANK {p.Rank} / {p.MaxRank}   ·   TIER {p.Tier}", "cy-pd__rank"));
            var pips = Ui.Div("cy-pd__pips");
            for (int i = 0; i < p.MaxRank; i++) pips.Add(Ui.Div("cy-pip", "cy-pip--big", i < p.Rank ? "cy-pip--on" : "cy-pip--off"));
            _detail.Add(pips);
            _detail.Add(Ui.Text(p.Desc, "cy-pd__desc"));
            if (p.Requires.Count > 0)
            {
                _detail.Add(Ui.Text("NEEDS", "cy-menu__sub"));
                foreach (string req in p.Requires)
                {
                    bool missing = p.Missing.Contains(req);
                    _detail.Add(Ui.Text((missing ? "✕ " : "✓ ") + req.ToUpperInvariant(), "cy-pd__req", missing ? "cy-pd__req--missing" : "cy-pd__req--met"));
                }
            }
            var buy = new MenuButton(CharacterModel.BuyLabel(p), compact: true) { ActivateSound = null };
            buy.SetEnabled(p.Blocked == null && !p.Maxed);
            string id = p.Id;
            buy.Pressed += () => Buy(id);
            _detailButtons.Add(buy);
            _detail.Add(buy);
            var respec = new MenuButton("RESPEC ALL PERKS", danger: true, compact: true) { ActivateSound = null };
            respec.SetEnabled(RpgStats.Attributes.SelectMany(a => CharacterModel.Tree(_core, a)).Any(x => x.Owned));
            respec.Pressed += Respec;
            _detailButtons.Add(respec);
            _detail.Add(respec);
            UiFonts.Apply(_detail);
        }

        void Buy(string id)
        {
            RpgResult r = _core.BuyPerk(id);
            if (!r.Ok)
            {
                UiSound.Raise(UiSound.Error);
                Refused?.Invoke(InventoryModel.Reason(r.Reason));
                return;
            }
            UiSound.Raise(UiSound.Confirm);
            Selected = id;
            Refresh(_core, 0, 0);
            _middleRows.FirstOrDefault(x => x.Id == id)?.Focus();
        }

        void Respec()
        {
            int refunded = _core.RespecPerks();
            UiSound.Raise(UiSound.Back);
            Refused?.Invoke($"{refunded} PERK POINT{(refunded == 1 ? "" : "S")} REFUNDED");
            Refresh(_core, 0, 0);
            _middleRows.FirstOrDefault(x => x.Id == Selected)?.Focus();
        }

        void BuildChrome()
        {
            (int used, int cap, List<ChromeSlotView> slots) = CharacterModel.Chrome(_core);
            _middleHead.text = $"CHROME // CAPACITY {used} / {cap}";
            var installed = slots.SelectMany(s => s.Installed.Select(w => w.Id)).ToList();
            if (Selected == null || !installed.Contains(Selected)) Selected = installed.FirstOrDefault();
            _middle.Clear();
            _middleRows.Clear();
            foreach (ChromeSlotView s in slots)
            {
                _middle.Add(Ui.Text($"{s.Label}   {s.Installed.Count}/{s.Capacity}", "cy-ch__tier"));
                if (s.Installed.Count == 0) _middle.Add(Ui.Text("EMPTY", "cy-ch__empty"));
                foreach (var w in s.Installed)
                {
                    var row = new ListRow(w.Id, "cy-perk", "cy-perk--owned");
                    row.Add(Ui.Text(w.Name, "cy-perk__name"));
                    row.Add(Ui.Text($"{w.Load} CAP", "cy-perk__rank"));
                    row.Selected += r =>
                    {
                        Selected = r.Id;
                        ShowChrome();
                        BuildGrid();
                    };
                    _middleRows.Add(row);
                    _middle.Add(row);
                }
            }
            _middle.Add(Ui.Text("CHROME IS INSTALLED FROM THE INVENTORY.", "cy-ch__empty"));
            ShowChrome();
        }

        void ShowChrome()
        {
            foreach (ListRow r in _middleRows) r.EnableInClassList("cy-row--selected", r.Id == Selected);
            _detail.Clear();
            _detailButtons.Clear();
            if (Selected == null) return;
            CyberwareDef w = RpgCyberware.Get(Selected);
            _detail.Add(Ui.Text("INSTALLED", "cy-pd__state", "cy-pd__state--active"));
            _detail.Add(Ui.Display(w.Name.ToUpperInvariant(), "cy-pd__name"));
            _detail.Add(Ui.Text($"{w.Corp.ToUpperInvariant()}   ·   TIER {w.Tier}   ·   {w.Capacity} CAPACITY", "cy-pd__rank"));
            _detail.Add(Ui.Text(w.Desc, "cy-pd__desc"));
            var remove = new MenuButton("REMOVE", danger: true, compact: true) { ActivateSound = null };
            string id = w.Id;
            remove.Pressed += () =>
            {
                RpgResult r = _core.RemoveCyberware(id);
                if (!r.Ok)
                {
                    UiSound.Raise(UiSound.Error);
                    Refused?.Invoke(InventoryModel.Reason(r.Reason));
                    return;
                }
                UiSound.Raise(UiSound.Back);
                if (r.Extra != null && r.Extra["returned"] is bool back && !back) Refused?.Invoke("NO ROOM TO CARRY IT: IT IS GONE");
                Refresh(_core, 0, 0);
                FocusDefault();
            };
            _detailButtons.Add(remove);
            _detail.Add(remove);
            UiFonts.Apply(_detail);
        }

        /// <summary>The focus table: attributes and their + in the first two columns, the tree or chrome in the third, the detail's buttons in the last.</summary>
        void BuildGrid()
        {
            int rows = Math.Max(_attrRows.Count, Math.Max(_middleRows.Count, _detailButtons.Count));
            var table = new List<IEnumerable<VisualElement>>();
            for (int r = 0; r < rows; r++)
            {
                table.Add(new VisualElement[]
                {
                    r < _attrRows.Count ? _attrRows[r] : null, r < _plusButtons.Count ? _plusButtons[r] : null,
                    r < _middleRows.Count ? _middleRows[r] : null, r < _detailButtons.Count ? _detailButtons[r] : null,
                });
            }
            _grid.SetRows(table);
        }

        public void FocusDefault()
        {
            ListRow row = _middleRows.FirstOrDefault(r => r.Id == Selected) ?? _attrRows.FirstOrDefault(r => r.Id == Section);
            row?.Focus();
        }
    }
}
