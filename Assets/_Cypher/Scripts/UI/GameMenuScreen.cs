using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>A tab of the game menu.</summary>
    public enum MenuTab { Inventory, Character, Journal }

    /// <summary>A page of the game menu: draws from the RPG, takes focus.</summary>
    public interface IMenuPage
    {
        VisualElement Root { get; }

        /// <summary>Redraw from the core. <paramref name="playerX"/> and <paramref name="playerZ"/> are for distances.</summary>
        void Refresh(RpgCore core, double playerX, double playerZ);

        void FocusDefault();
    }

    /// <summary>
    /// The game menu, after the prototype's <c>ui/Menus.js</c>: a top bar with the tabs (each with its key), the level and the eddies, one page per tab, and a foot
    /// with the keys. The pages work on the real RPG (the prototype's were mostly for show).
    /// </summary>
    public sealed class GameMenuScreen : MenuScreen
    {
        static readonly (MenuTab Tab, string Label, string Key)[] Tabs =
        {
            (MenuTab.Inventory, "INVENTORY", "TAB"), (MenuTab.Character, "CHARACTER", "K"), (MenuTab.Journal, "JOURNAL", "J"),
        };

        readonly Dictionary<MenuTab, IMenuPage> _pages = new Dictionary<MenuTab, IMenuPage>();
        readonly Dictionary<MenuTab, VisualElement> _tabButtons = new Dictionary<MenuTab, VisualElement>();
        readonly Label _level = Ui.Text("", "cy-menu__meta");
        readonly Label _money = Ui.Text("", "cy-menu__meta", "cy-menu__money");
        readonly VisualElement _body = Ui.Div("cy-menu__body");

        public MenuTab Tab { get; private set; } = MenuTab.Inventory;

        public IMenuPage Page(MenuTab tab) => _pages[tab];

        /// <summary>The player clicked a tab.</summary>
        public event Action<MenuTab> TabChosen;

        public GameMenuScreen(IMenuPage inventory, IMenuPage character, IMenuPage journal)
        {
            _pages[MenuTab.Inventory] = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _pages[MenuTab.Character] = character ?? throw new ArgumentNullException(nameof(character));
            _pages[MenuTab.Journal] = journal ?? throw new ArgumentNullException(nameof(journal));

            Root = Ui.Div("cy-screen", "cy-scrim", "cy-menu");
            var shell = Ui.Div("cy-menu__shell");
            var top = Ui.Div("cy-menu__top");
            var logo = Ui.Div("cy-menu__logo");
            logo.Add(Ui.Display("CY", "cy-menu__logotext"));
            logo.Add(Ui.Display("PHER", "cy-menu__logotext", "cy-logo--accent"));
            top.Add(logo);
            var tabs = Ui.Div("cy-menu__tabs");
            foreach ((MenuTab tab, string label, string key) in Tabs)
            {
                var b = Ui.Div("cy-menu__tab");
                b.Add(Ui.Display(label, "cy-menu__tablabel"));
                b.Add(Ui.Text(key, "cy-menu__tabkey"));
                MenuTab t = tab;
                b.RegisterCallback<ClickEvent>(_ => TabChosen?.Invoke(t));
                b.pickingMode = PickingMode.Position;
                _tabButtons[tab] = b;
                tabs.Add(b);
            }
            top.Add(tabs);
            var meta = Ui.Div("cy-menu__metas");
            meta.Add(_level);
            meta.Add(_money);
            top.Add(meta);
            shell.Add(top);
            foreach (IMenuPage page in _pages.Values) _body.Add(page.Root);
            shell.Add(_body);
            var foot = Ui.Div("cy-menu__foot");
            foot.Add(Ui.Text("LB / RB  OR  TAB · K · J   SWITCH", "cy-menu__hint"));
            foot.Add(Ui.Text("ENTER  ACT", "cy-menu__hint"));
            foot.Add(Ui.Text("ESC  CLOSE", "cy-menu__hint"));
            shell.Add(foot);
            Root.Add(shell);
            ShowTab(Tab);
        }

        public void ShowTab(MenuTab tab)
        {
            Tab = tab;
            foreach (KeyValuePair<MenuTab, IMenuPage> p in _pages) Ui.Show(p.Value.Root, p.Key == tab);
            foreach (KeyValuePair<MenuTab, VisualElement> b in _tabButtons) b.Value.EnableInClassList("cy-menu__tab--on", b.Key == tab);
        }

        /// <summary>The next or previous tab, wrapping.</summary>
        public MenuTab Step(int direction)
        {
            int n = Tabs.Length;
            int i = ((int)Tab + direction % n + n) % n;
            return (MenuTab)i;
        }

        public void Refresh(RpgCore core, double playerX, double playerZ)
        {
            _level.text = $"LV {core.Level}";
            _money.text = HudModel.Money(core.Money);
            _pages[Tab].Refresh(core, playerX, playerZ);
            UiFonts.Apply(Root);
        }

        public override void FocusDefault() => _pages[Tab].FocusDefault();
    }

    /// <summary>A row of a list that takes focus: a menu entry the arrows walk through and Submit acts on.</summary>
    public sealed class ListRow : FocusControl
    {
        public string Id { get; }

        public event Action<ListRow> Chosen;

        /// <summary>Focused (or hovered and clicked): the page shows its detail.</summary>
        public event Action<ListRow> Selected;

        public ListRow(string id, params string[] classes)
        {
            Id = id;
            AddToClassList("cy-row");
            foreach (string c in classes) AddToClassList(c);
            RegisterCallback<FocusInEvent>(_ => Selected?.Invoke(this));
        }

        protected override void OnActivate() => Chosen?.Invoke(this);
    }
}
