using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cypher.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>
    /// The inventory, after the prototype's: the categories with their counts, the carry weight and a few of the character's numbers on the left; the items as a
    /// grid of tiles edged in their rarity's colour; the chosen item on the right with its stats, how it compares with what is worn, its mods, and the actions that
    /// work (equip, use, install, deck, drop). Submit on a tile does its first action.
    /// </summary>
    public sealed class InventoryPage : IMenuPage
    {
        public const int Columns = 4;

        readonly VisualElement _cats = Ui.Div("cy-inv__cats");
        readonly VisualElement _carryFill = Ui.Div("cy-inv__carryfill");
        readonly Label _carry = Ui.Text("", "cy-inv__carrynum");
        readonly VisualElement _numbers = Ui.Div("cy-inv__numbers");
        readonly Label _count = Ui.Text("", "cy-menu__sub");
        readonly VisualElement _tiles = Ui.Div("cy-inv__tiles");
        readonly ScrollView _tileScroll = new ScrollView(ScrollViewMode.Vertical);
        readonly VisualElement _detail = Ui.Div("cy-inv__detail");
        readonly FocusGrid _grid;
        readonly List<ListRow> _catRows = new List<ListRow>();
        readonly List<ListRow> _tileRows = new List<ListRow>();
        readonly List<MenuButton> _actionButtons = new List<MenuButton>();
        RpgCore _core;

        public VisualElement Root { get; } = Ui.Div("cy-page", "cy-inv");

        public string Category { get; private set; } = "all";
        public string Selected { get; private set; }
        public ItemView Detail { get; private set; }
        public IReadOnlyList<ListRow> Tiles => _tileRows;
        public IReadOnlyList<ListRow> CategoryRows => _catRows;
        public IReadOnlyList<MenuButton> ActionButtons => _actionButtons;

        /// <summary>An action was refused; the screen shows why.</summary>
        public event Action<string> Refused;

        public InventoryPage()
        {
            var side = Ui.Div("cy-inv__side");
            side.Add(Ui.Text("FILTER", "cy-menu__sub"));
            side.Add(_cats);
            side.Add(Ui.Text("CARRY", "cy-menu__sub"));
            var bar = Ui.Div("cy-inv__carrybar");
            bar.Add(_carryFill);
            side.Add(bar);
            side.Add(_carry);
            side.Add(_numbers);
            Root.Add(side);
            var main = Ui.Div("cy-inv__main");
            main.Add(_count);
            _tileScroll.AddToClassList("cy-inv__scroll");
            _tileScroll.Add(_tiles);
            main.Add(_tileScroll);
            Root.Add(main);
            Root.Add(_detail);
            _grid = new FocusGrid(Root);
        }

        public void Refresh(RpgCore core, double playerX, double playerZ)
        {
            _core = core;
            BuildCategories();
            BuildTiles();
            BuildSide();
            ShowDetail();
            BuildGrid();
        }

        void BuildCategories()
        {
            _cats.Clear();
            _catRows.Clear();
            foreach (var c in InventoryModel.Categories)
            {
                var row = new ListRow(c.Id, "cy-cat");
                if (c.Id == Category) row.AddToClassList("cy-row--selected");
                row.Add(Ui.Text(c.Label, "cy-cat__label"));
                row.Add(Ui.Text(InventoryModel.Count(_core, c.Id).ToString(CultureInfo.InvariantCulture), "cy-cat__count"));
                row.Chosen += r => ChooseCategory(r.Id);
                _catRows.Add(row);
                _cats.Add(row);
            }
        }

        public void ChooseCategory(string id)
        {
            if (InventoryModel.Categories.All(c => c.Id != id)) throw new ArgumentOutOfRangeException(nameof(id), id, "unknown inventory category");
            Category = id;
            Refresh(_core, 0, 0);
            _catRows.First(r => r.Id == id).Focus();
        }

        void BuildTiles()
        {
            List<Item> items = InventoryModel.Items(_core, Category);
            if (Selected == null || items.All(i => i.Uid != Selected)) Selected = items.FirstOrDefault()?.Uid;
            _count.text = $"ITEMS  {items.Count}";
            _tiles.Clear();
            _tileRows.Clear();
            VisualElement line = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (i % Columns == 0)
                {
                    line = Ui.Div("cy-inv__line");
                    _tiles.Add(line);
                }
                Item it = items[i];
                Rarity rarity = RpgItems.RarityOf(it.Rarity);
                var tile = new ListRow(it.Uid, "cy-tile");
                if (ColorUtility.TryParseHtmlString(rarity.Color, out Color rc))
                {
                    tile.style.borderBottomColor = rc;
                    tile.style.borderLeftColor = new Color(rc.r, rc.g, rc.b, 0.5f);
                }
                if (_core.Inventory.IsEquipped(it.Uid))
                {
                    tile.AddToClassList("cy-tile--equipped");
                    tile.Add(Ui.Text("EQ", "cy-tile__eq"));
                }
                tile.Add(Ui.Text(KindLetter(it), "cy-tile__icon"));
                if (it.Qty > 1) tile.Add(Ui.Text(it.Qty > 999 ? "999+" : it.Qty.ToString(CultureInfo.InvariantCulture), "cy-tile__qty"));
                tile.Add(Ui.Text(it.Name.ToUpperInvariant(), "cy-tile__name"));
                tile.Selected += r =>
                {
                    Selected = r.Id;
                    ShowDetail();
                    BuildGrid();
                };
                tile.Chosen += r =>
                {
                    Selected = r.Id;
                    ItemActionView? first = Detail?.Actions.Cast<ItemActionView?>().FirstOrDefault(a => a.Value.Enabled && a.Value.Action != ItemAction.Drop);
                    if (first.HasValue) Do(first.Value.Action);
                    else UiSound.Raise(UiSound.Error);
                };
                _tileRows.Add(tile);
                line.Add(tile);
            }
            if (items.Count == 0) _tiles.Add(Ui.Text("NOTHING HERE", "cy-inv__empty"));
            foreach (ListRow t in _tileRows) t.EnableInClassList("cy-row--selected", t.Id == Selected);
        }

        /// <summary>A letter for the tile in place of the prototype's icons (no image files): the item's type.</summary>
        static string KindLetter(Item it)
        {
            switch (it.Type)
            {
                case "weapon": return it.Slot == "melee" ? "BLD" : "GUN";
                case "clothing": return "APL";
                case "cyberware": return "CHR";
                case "consumable": return "MED";
                case "quickhack": return "HCK";
                case "quest": return "JOB";
                default: return "JNK";
            }
        }

        void BuildSide()
        {
            double weight = _core.Inventory.Weight, cap = _core.Inventory.Capacity;
            _carryFill.style.width = Length.Percent((float)(Math.Max(0, Math.Min(1, weight / Math.Max(1, cap))) * 100));
            _carry.text = $"{weight:0.0} / {cap:0.0} KG";
            Root.EnableInClassList("cy-inv--overweight", _core.Inventory.Overweight);
            _numbers.Clear();
            DerivedStats d = _core.Derived;
            void Number(string label, string value)
            {
                var row = Ui.Div("cy-inv__number");
                row.Add(Ui.Text(label, "cy-inv__numlabel"));
                row.Add(Ui.Text(value, "cy-inv__numvalue"));
                _numbers.Add(row);
            }
            Number("ARMOR", d["armor"].ToString("0", CultureInfo.InvariantCulture));
            Number("CRIT", InventoryModel.Value(d["critChance"]));
            Number("RAM", d["maxRam"].ToString("0", CultureInfo.InvariantCulture));
            Number("DECK", $"{_core.Inventory.Deck.Count(u => u != null)}/{_core.Inventory.Deck.Length}");
        }

        void ShowDetail()
        {
            _detail.Clear();
            _actionButtons.Clear();
            Item item = Selected != null ? _core.Inventory.Find(Selected) : null;
            foreach (ListRow t in _tileRows) t.EnableInClassList("cy-row--selected", t.Id == Selected);
            if (item == null)
            {
                Detail = null;
                return;
            }
            ItemView v = InventoryModel.View(_core, item);
            Detail = v;
            ColorUtility.TryParseHtmlString(v.RarityColor, out Color rc);

            var head = Ui.Div("cy-det__head");
            Label rare = Ui.Text(v.RarityLabel, "cy-det__rarity");
            rare.style.color = rc;
            head.Add(rare);
            head.Add(Ui.Text($"LV {v.Level}", "cy-det__level"));
            if (v.Equipped) head.Add(Ui.Text("EQUIPPED", "cy-det__equipped"));
            _detail.Add(head);
            Label name = Ui.Display(v.Name, "cy-det__name");
            name.style.borderLeftColor = rc;
            _detail.Add(name);
            _detail.Add(Ui.Text(v.Kind + (v.Qty > 1 ? $"  ×{v.Qty}" : ""), "cy-det__kind"));

            if (v.Stats.Count > 0)
            {
                var stats = Ui.Div("cy-det__stats");
                foreach ((string label, string value) in v.Stats)
                {
                    var row = Ui.Div("cy-det__stat");
                    row.Add(Ui.Text(label, "cy-det__statlabel"));
                    row.Add(Ui.Text(value, "cy-det__statvalue"));
                    stats.Add(row);
                }
                _detail.Add(stats);
            }
            if (v.Compare.Count > 0)
            {
                _detail.Add(Ui.Text("AGAINST " + v.Replaces, "cy-menu__sub"));
                var cmp = Ui.Div("cy-det__stats");
                foreach ((string label, string delta, bool better) in v.Compare)
                {
                    var row = Ui.Div("cy-det__stat");
                    row.Add(Ui.Text(label, "cy-det__statlabel"));
                    row.Add(Ui.Text(delta, "cy-det__statvalue", better ? "cy-det__better" : "cy-det__worse"));
                    cmp.Add(row);
                }
                _detail.Add(cmp);
            }
            if (v.Mods.Count > 0)
            {
                _detail.Add(Ui.Text("MODS", "cy-menu__sub"));
                foreach (string m in v.Mods) _detail.Add(Ui.Text("◆ " + m, "cy-det__mod"));
            }
            _detail.Add(Ui.Text(v.Desc, "cy-det__desc"));
            var foot = Ui.Div("cy-det__foot");
            foot.Add(Ui.Text(HudModel.Money(v.Value), "cy-det__value"));
            foot.Add(Ui.Text($"{v.Weight:0.00} KG", "cy-det__weight"));
            _detail.Add(foot);

            var actions = Ui.Div("cy-det__actions");
            foreach (ItemActionView a in v.Actions)
            {
                var b = new MenuButton(a.Label, a.Blocked, danger: a.Action == ItemAction.Drop, compact: true);
                b.SetEnabled(a.Enabled);
                ItemAction act = a.Action;
                b.Pressed += () => Do(act);
                _actionButtons.Add(b);
                actions.Add(b);
            }
            _detail.Add(actions);
            UiFonts.Apply(_detail);
        }

        /// <summary>Do an action on the chosen item through the core, then redraw and keep the player's place.</summary>
        public void Do(ItemAction action)
        {
            Item item = Selected != null ? _core.Inventory.Find(Selected) : null;
            if (item == null) return;
            RpgResult r = InventoryModel.Apply(_core, item, action);
            if (!r.Ok)
            {
                UiSound.Raise(UiSound.Error);
                Refused?.Invoke(InventoryModel.Reason(r.Reason));
                return;
            }
            UiSound.Raise(action == ItemAction.Drop ? UiSound.Back : UiSound.Confirm);
            string keep = Selected;
            Refresh(_core, 0, 0);
            ListRow tile = _tileRows.FirstOrDefault(t => t.Id == keep) ?? _tileRows.FirstOrDefault();
            if (tile != null) tile.Focus();
            else _catRows.First(c => c.Id == Category).Focus();
        }

        /// <summary>The focus table: categories in the first column, the tiles in rows of <see cref="Columns"/>, the action buttons in the last column.</summary>
        void BuildGrid()
        {
            int tileRows = (_tileRows.Count + Columns - 1) / Columns;
            int rows = Math.Max(_catRows.Count, Math.Max(tileRows, _actionButtons.Count));
            var table = new List<IEnumerable<VisualElement>>();
            for (int r = 0; r < rows; r++)
            {
                var cells = new List<VisualElement> { r < _catRows.Count ? _catRows[r] : null };
                for (int c = 0; c < Columns; c++)
                {
                    int i = r * Columns + c;
                    cells.Add(r < tileRows && i < _tileRows.Count ? _tileRows[i] : null);
                }
                cells.Add(r < _actionButtons.Count ? _actionButtons[r] : null);
                table.Add(cells);
            }
            _grid.SetRows(table);
        }

        public void FocusDefault()
        {
            ListRow tile = _tileRows.FirstOrDefault(t => t.Id == Selected) ?? _tileRows.FirstOrDefault();
            if (tile != null) tile.Focus();
            else _catRows.FirstOrDefault()?.Focus();
        }
    }
}
