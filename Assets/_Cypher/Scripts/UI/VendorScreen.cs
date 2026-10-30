using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>
    /// Trading with a vendor (the prototype had the rules but no screen): the vendor's name, greeting and the player's discount and eddies across the top; their
    /// shelf on the left at the buy price and the player's things on the right at the sell price; the chosen item's stats below. Submit on a line buys or sells one;
    /// what cannot be traded says why.
    /// </summary>
    public sealed class VendorScreen : MenuScreen
    {
        readonly Label _name = Ui.Display("", "cy-shop__name");
        readonly Label _kind = Ui.Text("", "cy-shop__kind");
        readonly Label _greeting = Ui.Text("", "cy-shop__greeting");
        readonly Label _discount = Ui.Text("", "cy-shop__discount");
        readonly Label _money = Ui.Text("", "cy-shop__money");
        readonly VisualElement _shelf = Ui.Div("cy-shop__list");
        readonly VisualElement _bag = Ui.Div("cy-shop__list");
        readonly VisualElement _detail = Ui.Div("cy-shop__detail");
        readonly FocusGrid _grid;
        readonly List<ListRow> _shelfRows = new List<ListRow>();
        readonly List<ListRow> _bagRows = new List<ListRow>();
        RpgCore _core;
        string _vendorId;

        public string VendorId => _vendorId;
        public VendorModel.Shop Shop { get; private set; }
        public IReadOnlyList<ListRow> ShelfRows => _shelfRows;
        public IReadOnlyList<ListRow> BagRows => _bagRows;

        /// <summary>A trade was refused; the screen's owner shows why.</summary>
        public event Action<string> Refused;

        public VendorScreen()
        {
            Root = Ui.Div("cy-screen", "cy-scrim", "cy-shop");
            var panel = Ui.Div("cy-shop__panel");
            var head = Ui.Div("cy-shop__head");
            var who = Ui.Div("cy-shop__who");
            who.Add(_name);
            who.Add(_kind);
            head.Add(who);
            var purse = Ui.Div("cy-shop__purse");
            purse.Add(_money);
            purse.Add(_discount);
            head.Add(purse);
            panel.Add(head);
            panel.Add(_greeting);
            var body = Ui.Div("cy-shop__body");
            var left = Ui.Div("cy-shop__col");
            left.Add(Ui.Text("FOR SALE", "cy-menu__sub"));
            var shelfScroll = new ScrollView(ScrollViewMode.Vertical);
            shelfScroll.AddToClassList("cy-shop__scroll");
            shelfScroll.Add(_shelf);
            left.Add(shelfScroll);
            body.Add(left);
            var right = Ui.Div("cy-shop__col");
            right.Add(Ui.Text("YOUR GEAR", "cy-menu__sub"));
            var bagScroll = new ScrollView(ScrollViewMode.Vertical);
            bagScroll.AddToClassList("cy-shop__scroll");
            bagScroll.Add(_bag);
            right.Add(bagScroll);
            body.Add(right);
            body.Add(_detail);
            panel.Add(body);
            var foot = Ui.Div("cy-menu__foot");
            foot.Add(Ui.Text("ENTER  BUY / SELL ONE", "cy-menu__hint"));
            foot.Add(Ui.Text("ESC  LEAVE", "cy-menu__hint"));
            panel.Add(foot);
            Root.Add(panel);
            _grid = new FocusGrid(Root);
        }

        public void Open(RpgCore core, string vendorId)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _vendorId = vendorId ?? throw new ArgumentNullException(nameof(vendorId));
            Refresh();
        }

        public void Refresh()
        {
            Shop = VendorModel.Read(_core, _vendorId);
            _name.text = Shop.Name;
            _kind.text = Shop.Kind;
            _greeting.text = Shop.Greeting.Length > 0 ? $"“{Shop.Greeting}”" : "";
            _discount.text = VendorModel.DiscountText(Shop.Discount);
            _money.text = HudModel.Money(Shop.Money);
            Fill(_shelf, _shelfRows, Shop.Shelf, buying: true);
            Fill(_bag, _bagRows, Shop.Bag, buying: false);
            var rows = new List<IEnumerable<VisualElement>>();
            for (int i = 0; i < Math.Max(_shelfRows.Count, _bagRows.Count); i++)
            {
                rows.Add(new VisualElement[] { i < _shelfRows.Count ? _shelfRows[i] : null, i < _bagRows.Count ? _bagRows[i] : null });
            }
            _grid.SetRows(rows);
            UiFonts.Apply(Root);
        }

        void Fill(VisualElement list, List<ListRow> rows, List<TradeLine> lines, bool buying)
        {
            list.Clear();
            rows.Clear();
            if (lines.Count == 0) list.Add(Ui.Text(buying ? "SOLD OUT" : "NOTHING TO SELL", "cy-ch__empty"));
            foreach (TradeLine l in lines)
            {
                var row = new ListRow(l.Uid, "cy-trade");
                if (!l.Enabled) row.AddToClassList("cy-trade--blocked");
                if (ColorUtility.TryParseHtmlString(l.RarityColor, out Color rc)) row.style.borderRightColor = rc;
                var text = Ui.Div("cy-trade__text");
                text.Add(Ui.Text(l.Name + (l.Qty > 1 ? $"  ×{l.Qty}" : "") + (l.Equipped ? "  [EQ]" : ""), "cy-trade__name"));
                text.Add(Ui.Text(l.Blocked ?? l.Kind, "cy-trade__kind"));
                row.Add(text);
                row.Add(Ui.Text(HudModel.Money(l.Price), "cy-trade__price"));
                TradeLine line = l;
                row.Selected += _ => ShowDetail(line.Uid, buying);
                row.Chosen += _ => Trade(line.Uid, buying);
                rows.Add(row);
                list.Add(row);
            }
        }

        void ShowDetail(string uid, bool buying)
        {
            _detail.Clear();
            Item item = buying ? _core.Economy.State(_vendorId).Stock.FirstOrDefault(i => i.Uid == uid) : _core.Inventory.Find(uid);
            if (item == null) return;
            ItemView v = InventoryModel.View(_core, item);
            ColorUtility.TryParseHtmlString(v.RarityColor, out Color rc);
            Label rare = Ui.Text(v.RarityLabel, "cy-det__rarity");
            rare.style.color = rc;
            _detail.Add(rare);
            Label name = Ui.Display(v.Name, "cy-det__name");
            name.style.borderLeftColor = rc;
            _detail.Add(name);
            _detail.Add(Ui.Text(v.Kind, "cy-det__kind"));
            foreach ((string label, string value) in v.Stats)
            {
                var row = Ui.Div("cy-det__stat");
                row.Add(Ui.Text(label, "cy-det__statlabel"));
                row.Add(Ui.Text(value, "cy-det__statvalue"));
                _detail.Add(row);
            }
            if (buying && v.Compare.Count > 0)
            {
                _detail.Add(Ui.Text("AGAINST " + v.Replaces, "cy-menu__sub"));
                foreach ((string label, string delta, bool better) in v.Compare)
                {
                    var row = Ui.Div("cy-det__stat");
                    row.Add(Ui.Text(label, "cy-det__statlabel"));
                    row.Add(Ui.Text(delta, "cy-det__statvalue", better ? "cy-det__better" : "cy-det__worse"));
                    _detail.Add(row);
                }
            }
            _detail.Add(Ui.Text(v.Desc, "cy-det__desc"));
            UiFonts.Apply(_detail);
        }

        /// <summary>Buy or sell one through the core, redraw, and keep the player's place.</summary>
        public void Trade(string uid, bool buying)
        {
            RpgResult r = buying ? VendorModel.Buy(_core, _vendorId, uid) : VendorModel.Sell(_core, _vendorId, uid);
            if (!r.Ok)
            {
                UiSound.Raise(UiSound.Error);
                Refused?.Invoke(r.Reason == "equipped" ? "UNEQUIP FIRST" : InventoryModel.Reason(r.Reason));
                return;
            }
            UiSound.Raise(UiSound.Confirm);
            List<ListRow> before = buying ? _shelfRows : _bagRows;
            int index = before.FindIndex(x => x.Id == uid);
            Refresh();
            List<ListRow> after = buying ? _shelfRows : _bagRows;
            ListRow next = after.FirstOrDefault(x => x.Id == uid) ?? (after.Count > 0 ? after[Math.Min(Math.Max(0, index), after.Count - 1)] : null);
            if (next != null) next.Focus();
            else FocusDefault();
        }

        public override void FocusDefault()
        {
            ListRow first = _shelfRows.FirstOrDefault() ?? _bagRows.FirstOrDefault();
            first?.Focus();
        }
    }
}
