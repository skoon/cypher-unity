using System;
using System.Collections.Generic;
using System.Text;
using Cypher.Core;
using Cypher.Fight;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>
    /// The fight on the HUD: an arc round the crosshair pointing at whoever just hit the player (<see cref="DamageArcs"/>), a name and health bar over the enemy under
    /// the crosshair (with the quickhacks running on it), a marker over every pinged enemy, seen through walls, the deck while the scanner is held (the hack picked,
    /// what each costs, what cannot be paid for, the target), and the flatline screen while the Trauma Team is on its way.
    /// </summary>
    public sealed class FightHud
    {
        /// <summary>How far the arcs sit from the centre of the screen, in panel pixels.</summary>
        public const float ArcRadius = 130f;

        /// <summary>How far over an enemy's feet its bar floats, metres; and a ping's marker.</summary>
        public const float BarHeight = 2.15f, PingHeight = 2.0f;

        /// <summary>The short names of the hacks' effects, under the health bar.</summary>
        public static string EffectLabel(string effect)
        {
            switch (effect)
            {
                case "reveal": return "PING";
                case "blind": return "BLIND";
                case "burn": return "BURN";
                case "shock": return "SHOCK";
                case "contagion": return "TOXIN";
                case "berserk": return "PSYCHO";
                default: throw new ArgumentOutOfRangeException(nameof(effect), effect, "a quickhack effect the HUD has no name for");
            }
        }

        readonly EnemyController _fight;
        readonly DamageArcs _arcs = new DamageArcs();
        readonly VisualElement[] _arcViews = new VisualElement[DamageArcs.Max];
        readonly VisualElement _bar, _barFill;
        readonly Label _barName;
        readonly VisualElement _flatline, _flatlineFill;
        readonly Label _barHacks;
        readonly VisualElement[] _pings = new VisualElement[EnemyBodies.Max];
        readonly VisualElement _deck;
        readonly Label _deckTarget;
        readonly List<Label> _deckRows = new List<Label>();
        readonly StringBuilder _text = new StringBuilder();
        Enemy _barFor;
        double _barShownHealth = -1;
        int _barHacksKey = -1, _deckKey = int.MinValue;
        Enemy _deckTargetShown;
        bool _deckTargetSet;

        public VisualElement Root { get; }
        public DamageArcs Arcs => _arcs;
        public bool BarShown => Ui.IsShown(_bar);
        public string BarName => _barName.text;
        public float BarFill { get; private set; }
        public bool FlatlineShown => Ui.IsShown(_flatline);
        public string BarHacks => Ui.IsShown(_barHacks) ? _barHacks.text : "";

        /// <summary>How many ping markers show now.</summary>
        public int PingsShown { get; private set; }

        public bool DeckShown => Ui.IsShown(_deck);
        public string DeckTarget => _deckTarget.text;

        /// <summary>The deck's rows as shown: the text, whether it is the pick, whether it can be paid for.</summary>
        public IEnumerable<(string Text, bool Picked, bool Affordable)> DeckRows
        {
            get
            {
                foreach (Label l in _deckRows)
                {
                    if (Ui.IsShown(l)) yield return (l.text, l.ClassListContains("cy-fight-deck__row--picked"), !l.ClassListContains("cy-fight-deck__row--short"));
                }
            }
        }

        public FightHud(EnemyController fight)
        {
            _fight = fight ?? throw new ArgumentNullException(nameof(fight));
            Root = Ui.Div("cy-fight");
            Root.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < DamageArcs.Max; i++)
            {
                _arcViews[i] = Ui.Div("cy-fight-arc");
                Ui.Show(_arcViews[i], false);
                Root.Add(_arcViews[i]);
            }

            _bar = Ui.Div("cy-fight-bar");
            _barName = Ui.Text("", "cy-fight-bar__name", "cy-display");
            _bar.Add(_barName);
            var track = Ui.Div("cy-fight-bar__track");
            _barFill = Ui.Div("cy-fight-bar__fill");
            track.Add(_barFill);
            _bar.Add(track);
            _barHacks = Ui.Text("", "cy-fight-bar__hacks");
            Ui.Show(_barHacks, false);
            _bar.Add(_barHacks);
            Ui.Show(_bar, false);
            Root.Add(_bar);

            for (int i = 0; i < _pings.Length; i++)
            {
                _pings[i] = Ui.Div("cy-fight-ping");
                Ui.Show(_pings[i], false);
                Root.Add(_pings[i]);
            }

            _deck = Ui.Div("cy-fight-deck");
            _deck.Add(Ui.Text("QUICKHACKS", "cy-fight-deck__title", "cy-display"));
            _deckTarget = Ui.Text("", "cy-fight-deck__target");
            _deck.Add(_deckTarget);
            Ui.Show(_deck, false);
            Root.Add(_deck);

            _flatline = Ui.Div("cy-flatline");
            _flatline.Add(Ui.Text("FLATLINED", "cy-flatline__title", "cy-display"));
            _flatline.Add(Ui.Text("TRAUMA TEAM EN ROUTE", "cy-flatline__sub"));
            var progress = Ui.Div("cy-flatline__track");
            _flatlineFill = Ui.Div("cy-flatline__fill");
            progress.Add(_flatlineFill);
            _flatline.Add(progress);
            Ui.Show(_flatline, false);
            Root.Add(_flatline);

            foreach (VisualElement e in Root.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            _fight.PlayerHit += OnPlayerHit;
            _fight.Revived += OnRevived;
        }

        public void Detach()
        {
            _fight.PlayerHit -= OnPlayerHit;
            _fight.Revived -= OnRevived;
        }

        void OnPlayerHit(Vector3 from, double damage)
        {
            _arcs.Add(DamageArcs.BearingTo(_playerFeet.x, _playerFeet.z, from.x, from.z), 0.35 + damage / 30);
        }

        void OnRevived(int fee) => _arcs.Clear();

        /// <summary>Where the player stood at the last <see cref="Update"/>: the hits come between frames, from the fight's step.</summary>
        Vector3 _playerFeet;

        public void Update(VisualElement panelRoot, Camera camera, Transform player, double playerYaw, double dt, RpgCore core)
        {
            _playerFeet = player.position;
            float w = panelRoot.resolvedStyle.width, h = panelRoot.resolvedStyle.height;
            bool sized = !float.IsNaN(w) && w > 0;

            _arcs.Update(dt);
            for (int i = 0; i < DamageArcs.Max; i++)
            {
                bool on = sized && i < _arcs.Count;
                Ui.Show(_arcViews[i], on);
                if (!on) continue;
                (double rel, double alpha) = _arcs.Get(i, playerYaw);
                float x = w / 2 + (float)Math.Sin(rel) * ArcRadius, y = h / 2 - (float)Math.Cos(rel) * ArcRadius;
                VisualElement a = _arcViews[i];
                a.style.left = x;
                a.style.top = y;
                a.style.rotate = new Rotate(new Angle((float)(rel * Mathf.Rad2Deg), AngleUnit.Degree));
                a.style.opacity = (float)alpha;
            }

            Enemy aimed = _fight.Aimed;
            bool showBar = sized && aimed != null && !_fight.Downed;
            Ui.Show(_bar, showBar);
            if (showBar)
            {
                if (aimed != _barFor)
                {
                    _barFor = aimed;
                    _barName.text = aimed.Archetype.Name.ToUpperInvariant();
                    _barShownHealth = -1;
                }
                Vector3 over = new Vector3((float)aimed.Position.X, (float)aimed.Position.Y + BarHeight, (float)aimed.Position.Z);
                Vector3 view = camera.WorldToViewportPoint(over);
                if (view.z > 0)
                {
                    _bar.style.left = view.x * w - 60;
                    _bar.style.top = (1 - view.y) * h - 30;
                }
                double health = aimed.Vitals.Health;
                if (health != _barShownHealth)
                {
                    _barShownHealth = health;
                    BarFill = (float)Math.Max(0, Math.Min(1, health / aimed.Archetype.MaxHealth));
                    _barFill.style.width = Length.Percent(BarFill * 100);
                }
                ShowBarHacks(aimed, core);
            }
            else
            {
                _barFor = null;
                _barHacksKey = -1;
            }

            UpdatePings(camera, w, h, sized);
            UpdateDeck(core);

            Ui.Show(_flatline, _fight.Downed);
            if (_fight.Downed) _flatlineFill.style.width = Length.Percent(_fight.DownProgress * 100);
        }

        static readonly string[] Effects = { "reveal", "blind", "burn", "shock", "contagion", "berserk" };

        /// <summary>The hacks running on the aimed enemy, written only when the set changes.</summary>
        void ShowBarHacks(Enemy aimed, RpgCore core)
        {
            int key = aimed.Id << 8;
            if (core != null)
            {
                string target = HackEffects.TargetOf(aimed);
                foreach (ActiveHack a in core.Deck.Active)
                {
                    if (a.TargetId == target) key |= 1 << Array.IndexOf(Effects, a.Effect);
                }
            }
            if (key == _barHacksKey) return;
            _barHacksKey = key;
            _text.Clear();
            for (int i = 0; i < Effects.Length; i++)
            {
                if ((key & (1 << i)) == 0) continue;
                if (_text.Length > 0) _text.Append("  ");
                _text.Append(EffectLabel(Effects[i]));
            }
            _barHacks.text = _text.ToString();
            Ui.Show(_barHacks, _text.Length > 0);
        }

        /// <summary>A marker over every pinged enemy in front of the camera, walls or not.</summary>
        void UpdatePings(Camera camera, float w, float h, bool sized)
        {
            int n = 0;
            IReadOnlyList<Enemy> all = _fight.Sim != null && sized && !_fight.Downed ? _fight.Sim.Enemies : (IReadOnlyList<Enemy>)Array.Empty<Enemy>();
            for (int i = 0; i < all.Count && n < _pings.Length; i++)
            {
                Enemy e = all[i];
                if (!e.Revealed || e.State == EnemyState.Down) continue;
                Vector3 view = camera.WorldToViewportPoint(new Vector3((float)e.Position.X, (float)e.Position.Y + PingHeight, (float)e.Position.Z));
                if (view.z <= 0) continue;
                VisualElement m = _pings[n++];
                m.style.left = view.x * w;
                m.style.top = (1 - view.y) * h;
                Ui.Show(m, true);
            }
            for (int i = n; i < _pings.Length; i++) Ui.Show(_pings[i], false);
            PingsShown = n;
        }

        /// <summary>The deck while hacking: rewritten only when the slots, the pick, what RAM pays for or the target change.</summary>
        void UpdateDeck(RpgCore core)
        {
            bool show = _fight.Hacking && core != null;
            Ui.Show(_deck, show);
            if (!show)
            {
                _deckKey = int.MinValue;
                _deckTargetSet = false;
                return;
            }
            Item[] slots = core.Inventory.DeckItems();
            double discount = core.HackDiscount;
            int key = _fight.HackSlot * 31 + (int)Math.Floor(core.Deck.Ram + 1e-6);
            foreach (Item it in slots) key = key * 17 + (it == null ? 0 : it.TemplateId.GetHashCode());
            if (key != _deckKey)
            {
                _deckKey = key;
                while (_deckRows.Count < slots.Length)
                {
                    var row = Ui.Text("", "cy-fight-deck__row");
                    row.pickingMode = PickingMode.Ignore;
                    _deck.Insert(_deck.IndexOf(_deckTarget), row);
                    _deckRows.Add(row);
                }
                for (int i = 0; i < _deckRows.Count; i++)
                {
                    Label row = _deckRows[i];
                    Item it = i < slots.Length ? slots[i] : null;
                    Ui.Show(row, it != null);
                    if (it == null) continue;
                    double cost = core.Deck.CostOf(it.Hack, discount);
                    row.text = $"{it.Name.ToUpperInvariant()}   {cost:0} RAM";
                    row.EnableInClassList("cy-fight-deck__row--picked", i == _fight.HackSlot);
                    row.EnableInClassList("cy-fight-deck__row--short", core.Deck.Ram + 1e-9 < cost);
                }
            }
            Enemy target = _fight.HackTarget;
            if (_deckTargetSet && target == _deckTargetShown) return;
            _deckTargetSet = true;
            _deckTargetShown = target;
            _deckTarget.text = target == null ? "NO TARGET IN RANGE" : $"TARGET  {target.Archetype.Name.ToUpperInvariant()}";
            _deckTarget.EnableInClassList("cy-fight-deck__target--none", target == null);
        }
    }
}
