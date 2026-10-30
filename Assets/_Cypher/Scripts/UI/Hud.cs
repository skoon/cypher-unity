using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using Cypher.Rpg;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>
    /// The in-game HUD, after the prototype's (<c>ui/HUD.js</c>): the biomonitor (health, stamina, RAM, level, eddies) at the bottom left, the tracked job at the top
    /// left, the compass at the top, the minimap at the top right, notices on the right, the waypoint to the tracked objective, the E prompt, the crosshair, and a red
    /// flash when hurt. It reads the RPG's state every frame and its events for the notices; it is shown only while playing.
    /// </summary>
    public sealed class Hud
    {
        readonly RpgService _rpg;
        readonly HudToasts _toastModel = new HudToasts();
        readonly BioMonitor _bio;
        readonly JobTracker _tracker;
        readonly CompassView _compass;
        readonly MinimapView _minimap;
        readonly WaypointView _waypoint;
        readonly ToastView _toasts;
        readonly Label _prompt;
        readonly VisualElement _crosshair;
        readonly VisualElement _damage;
        double _hurt;
        Func<string> _interactKey = () => "E";
        string _promptLabel;
        readonly List<(double Bearing, bool Tracked)> _pips = new List<(double, bool)>();
        GunHud _gun;

        public VisualElement Root { get; }

        /// <summary>The notices showing now, newest first; for tests.</summary>
        public IReadOnlyList<HudToast> Notices => _toastModel.Visible(Time.unscaledTimeAsDouble);

        public string PromptText => _prompt.text;
        public bool PromptShown => Ui.IsShown(_prompt);
        public bool Shown => Ui.IsShown(Root);
        public JobTracker Tracker => _tracker;
        public BioMonitor Bio => _bio;
        public WaypointView Waypoint => _waypoint;

        /// <summary>The gun's panel, crosshair and hit marker; null in a scene without guns.</summary>
        public GunHud Gun => _gun;

        /// <summary>Show the gun in hand: the weapon panel, a crosshair that follows its spread, the hit marker.</summary>
        public void AttachGuns(Cypher.Combat.WeaponSystem guns)
        {
            _gun = new GunHud(guns, _crosshair);
            Root.Add(_gun.Panel);
            UiFonts.Apply(_gun.Panel);
        }

        public Hud(RpgService rpg)
        {
            _rpg = rpg ?? throw new ArgumentNullException(nameof(rpg));
            Root = Ui.Div("cy-screen", "cy-hud");
            Root.pickingMode = PickingMode.Ignore;

            _damage = Ui.Div("cy-hud-damage");
            Root.Add(_damage);
            _crosshair = Ui.Div("cy-crosshair");
            foreach (string part in new[] { "cy-crosshair__dot", "cy-crosshair__t", "cy-crosshair__b", "cy-crosshair__l", "cy-crosshair__r" }) _crosshair.Add(Ui.Div(part));
            Root.Add(_crosshair);
            _waypoint = new WaypointView();
            Root.Add(_waypoint.Root);

            _tracker = new JobTracker();
            Root.Add(_tracker.Root);
            _compass = new CompassView();
            Root.Add(_compass.Root);
            _minimap = new MinimapView();
            Root.Add(_minimap.Root);
            _bio = new BioMonitor();
            Root.Add(_bio.Root);
            _toasts = new ToastView();
            Root.Add(_toasts.Root);

            _prompt = Ui.Text("", "cy-hud-prompt", "cy-display");
            Ui.Show(_prompt, false);
            Root.Add(_prompt);

            foreach (VisualElement e in Root.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            _rpg.Event += OnEvent;
            _rpg.CoreChanged += core => _toastModel.Clear();
        }

        /// <summary>What the interact key is called now (it can be rebound).</summary>
        public void SetInteractKey(Func<string> key) => _interactKey = key ?? (() => "E");

        public void Detach() => _rpg.Event -= OnEvent;

        /// <summary>The interact key was rebound: the prompt is written again on the next frame.</summary>
        public void KeyChanged() => _promptLabel = null;

        public void SetShown(bool shown)
        {
            Ui.Show(Root, shown);
            if (!shown) _promptLabel = null;   // the key may be rebound while the HUD is away: write the prompt again when it is back
        }

        /// <summary>Draw this frame from the RPG's state.</summary>
        public void Update(Camera camera, double now, double dt)
        {
            RpgCore core = _rpg.Core;
            if (core == null || camera == null) return;
            Transform player = _rpg.Player.transform;
            double px = player.position.x, pz = player.position.z;
            double yaw = _rpg.Player.Look.Yaw;

            _bio.Update(core, dt);
            JobTracker.Show(_tracker, core, px, pz);
            IReadOnlyList<JsonObject> markers = core.JobMarkers;
            JsonObject tracked = HudModel.TrackedMarker(markers);
            _pips.Clear();
            foreach (JsonObject m in markers) _pips.Add((HudModel.BearingTo(px, pz, (double)m["x"], (double)m["z"]), m == tracked));
            _compass.Update(HudModel.Bearing(yaw), _pips);
            _minimap.Update(_rpg.CityMeta, core.GetLocationList(), markers, px, pz, yaw);
            _waypoint.Update(Root, camera, tracked, px, pz);
            _toasts.Show(_toastModel.Visible(now));

            // the prompt is written when what E does changes, not every frame
            var near = core.Dialogue.Active ? null : core.NearbyAction();
            Ui.Show(_prompt, near != null);
            string label = near?.Label;
            if (label != _promptLabel)
            {
                _promptLabel = label;
                if (label != null) _prompt.text = $"[{_interactKey()}]  {label.ToUpperInvariant()}";
            }

            _gun?.Update(camera, dt, Root.resolvedStyle.height);

            _hurt = Math.Max(0, _hurt - dt * 1.6);
            _damage.style.opacity = (float)Math.Min(1, _hurt);
        }

        void OnEvent(string type, JsonObject p)
        {
            double now = Time.unscaledTimeAsDouble;
            string Upper(object o) => o == null ? null : o.ToString().ToUpperInvariant();
            switch (type)
            {
                case "rpg:moneyChanged":
                {
                    double delta = p["delta"] is double d ? d : 0;
                    if (delta != 0) _toastModel.Push("cred", (delta > 0 ? "+" : "−") + HudModel.Money(Math.Abs(delta)), Upper(p["reason"]), now);
                    break;
                }
                case "rpg:xpGained":
                    _toastModel.Push("xp", $"+{p["amount"]} XP", Upper(p["source"]), now);
                    break;
                case "rpg:levelUp":
                    _toastModel.Push("level", $"LEVEL {p["level"]}", "ATTRIBUTE AND PERK POINTS", now);
                    break;
                case "rpg:perkUnlocked":
                    _toastModel.Push("level", Upper(((JsonObject)p["perk"])["name"]), "PERK ACQUIRED", now);
                    break;
                case "rpg:itemAdded":
                {
                    object item = p["item"];
                    string name = item is Item it ? it.Name : item is JsonObject jo ? jo["name"] as string : "ITEM";
                    double qty = p["qty"] is double q ? q : 1;
                    _toastModel.Push("item", Upper(name) + (qty > 1 ? $"  ×{qty}" : ""), "ACQUIRED", now);
                    break;
                }
                case "rpg:objectiveCompleted":
                    _toastModel.Push("quest", Upper(p["text"]), "OBJECTIVE COMPLETE", now);
                    break;
                case "rpg:questStarted":
                    _toastModel.Push("quest", Upper(((JsonObject)p["quest"])["title"]), "NEW JOB", now);
                    break;
                case "rpg:questCompleted":
                case "rpg:questFailed":
                    _toastModel.Push("quest", Upper(((JsonObject)p["quest"])["title"]), Upper(p["outcomeTitle"]), now);
                    break;
                case "rpg:daemonUploaded":
                    _toastModel.Push("hack", Upper(((JsonObject)p["daemon"])["name"]), "DAEMON UPLOADED", now);
                    break;
                case "rpg:notify":
                {
                    string kind = p["kind"] as string ?? "info";
                    // levels, perks and the like have their own notices above
                    if (kind != "level" && kind != "perk" && kind != "hack") _toastModel.Push(kind, Upper(p["text"]), null, now);
                    break;
                }
                case "rpg:damaged":
                    _hurt = Math.Min(1.4, _hurt + 0.35 + (p["amount"] is double a ? a / 60 : 0));
                    break;
                case "rpg:playerDied":
                    _toastModel.Push("warn", "FLATLINED", "TRAUMA TEAM DISPATCHED", now);
                    break;
            }
        }
    }

    /// <summary>Bottom left: health (with a ghost that drains after a hit), stamina, RAM as chips, level and XP, eddies.</summary>
    public sealed class BioMonitor
    {
        readonly VisualElement _hpFill, _hpGhost, _stFill, _xpFill, _chips;
        readonly Label _hpNum, _stNum, _ramNum, _level, _money;
        double _ghost = 1;
        double _hpShown = double.NaN, _hpMaxShown = double.NaN, _stShown = double.NaN, _moneyShown = double.NaN;
        int _ramShown = -1, _ramMaxShown = -1, _levelShown = -1;

        public VisualElement Root { get; }
        public string HealthText => _hpNum.text;
        public string LevelText => _level.text;
        public string MoneyText => _money.text;
        public int RamChips => _chips.childCount;

        public BioMonitor()
        {
            Root = Ui.Div("cy-hud-panel", "cy-bio");
            var head = Ui.Div("cy-hud-head");
            head.Add(Ui.Text("BIOMONITOR", "cy-hud-tag"));
            head.Add(Ui.Text("OPERATOR // V-7734", "cy-hud-sub"));
            Root.Add(head);
            (_hpFill, _hpGhost, _hpNum) = Row("HP", "cy-bar--hp", true);
            (_stFill, _, _stNum) = Row("ST", "cy-bar--st", false);
            var ram = Ui.Div("cy-bio__row");
            ram.Add(Ui.Text("RAM", "cy-bio__lab"));
            _chips = Ui.Div("cy-bio__chips");
            ram.Add(_chips);
            _ramNum = Ui.Text("", "cy-bio__num");
            ram.Add(_ramNum);
            Root.Add(ram);
            var lv = Ui.Div("cy-bio__row");
            _level = Ui.Text("LV 1", "cy-bio__lab", "cy-bio__lv");
            lv.Add(_level);
            var xp = Ui.Div("cy-bio__bar", "cy-bar--xp");
            _xpFill = Ui.Div("cy-bio__fill");
            xp.Add(_xpFill);
            lv.Add(xp);
            _money = Ui.Text("", "cy-bio__num", "cy-bio__money");
            lv.Add(_money);
            Root.Add(lv);
        }

        (VisualElement Fill, VisualElement Ghost, Label Num) Row(string label, string barClass, bool ghost)
        {
            var row = Ui.Div("cy-bio__row");
            row.Add(Ui.Text(label, "cy-bio__lab"));
            var bar = Ui.Div("cy-bio__bar", barClass);
            VisualElement g = null;
            if (ghost)
            {
                g = Ui.Div("cy-bio__ghost");
                bar.Add(g);
            }
            var fill = Ui.Div("cy-bio__fill");
            bar.Add(fill);
            row.Add(bar);
            var num = Ui.Text("", "cy-bio__num");
            row.Add(num);
            Root.Add(row);
            return (fill, g, num);
        }

        public void Update(RpgCore core, double dt)
        {
            DerivedStats d = core.Derived;
            double hp = Math.Max(0, Math.Min(1, core.Health / d["maxHealth"]));
            _ghost = hp > _ghost ? hp : Math.Max(hp, _ghost - dt * 0.35);
            _hpFill.style.width = Length.Percent((float)(hp * 100));
            _hpGhost.style.width = Length.Percent((float)(_ghost * 100));
            double hpNum = Math.Ceiling(core.Health), hpMax = d["maxHealth"];
            if (hpNum != _hpShown || hpMax != _hpMaxShown)
            {
                _hpShown = hpNum;
                _hpMaxShown = hpMax;
                _hpNum.text = $"{hpNum:000}/{hpMax:0}";
            }
            Root.EnableInClassList("cy-bio--low", hp < 0.35);
            _stFill.style.width = Length.Percent((float)(Math.Max(0, Math.Min(1, core.Stamina / d["maxStamina"])) * 100));
            double st = Math.Floor(core.Stamina);
            if (st != _stShown)
            {
                _stShown = st;
                _stNum.text = $"{st:0}";
            }

            int max = (int)d["maxRam"];
            while (_chips.childCount < max) _chips.Add(Ui.Div("cy-bio__chip"));
            while (_chips.childCount > max) _chips.RemoveAt(_chips.childCount - 1);
            int full = (int)Math.Floor(core.Deck.Ram + 1e-6);
            for (int i = 0; i < max; i++) _chips[i].EnableInClassList("cy-bio__chip--on", i < full);
            if (full != _ramShown || max != _ramMaxShown)
            {
                _ramShown = full;
                _ramMaxShown = max;
                _ramNum.text = $"{full}/{max}";
            }

            int level = core.Stats.Level;
            if (level != _levelShown)
            {
                _levelShown = level;
                _level.text = $"LV {level}";
            }
            double toNext = core.Stats.XpToNext;
            double frac = double.IsInfinity(toNext) ? 1 : core.Stats.XpInto / toNext;
            _xpFill.style.width = Length.Percent((float)(Math.Max(0, Math.Min(1, frac)) * 100));
            double money = core.Economy.Money;
            if (money != _moneyShown)
            {
                _moneyShown = money;
                _money.text = HudModel.Money(money);
            }
        }
    }

    /// <summary>Top left: the tracked job, its type and recommended level, and its open steps with distances.</summary>
    public sealed class JobTracker
    {
        readonly Label _type, _level, _title;
        readonly VisualElement _steps;
        JsonObject _quest;
        readonly List<(Label Label, double X, double Z, long Shown)> _distances = new List<(Label, double, double, long)>();

        public VisualElement Root { get; }
        public string Title => _title.text;
        public bool Shown => Ui.IsShown(Root);
        public IReadOnlyList<string> StepTexts => _steps.Children().Select(c => ((Label)c.ElementAt(1)).text).ToList();

        public JobTracker()
        {
            Root = Ui.Div("cy-hud-panel", "cy-tracker");
            var head = Ui.Div("cy-hud-head");
            _type = Ui.Text("MAIN JOB", "cy-hud-tag");
            _level = Ui.Text("", "cy-hud-sub");
            head.Add(_type);
            head.Add(_level);
            Root.Add(head);
            _title = Ui.Text("", "cy-tracker__title", "cy-display");
            Root.Add(_title);
            _steps = Ui.Div("cy-tracker__steps");
            Root.Add(_steps);
        }

        public static void Show(JobTracker t, RpgCore core, double px, double pz)
        {
            // the core keeps the snapshot until the job changes, so a new one means something to redraw
            JsonObject quest = core.TrackedJob;
            Ui.Show(t.Root, quest != null && (string)quest["state"] == "active");
            if (quest == null) return;
            if (!ReferenceEquals(quest, t._quest))
            {
                t._quest = quest;
                t._type.text = HudModel.JobType((string)quest["type"]);
                t._level.text = $"LV {quest["recommendedLevel"]}";
                t._title.text = ((string)quest["title"]).ToUpperInvariant();
                t._steps.Clear();
                t._distances.Clear();
                foreach (TrackerStep s in HudModel.Steps(quest, px, pz))
                {
                    var row = Ui.Div("cy-tracker__step");
                    if (s.Active) row.AddToClassList("cy-tracker__step--active");
                    if (s.Optional) row.AddToClassList("cy-tracker__step--optional");
                    row.Add(Ui.Div("cy-tracker__box"));
                    row.Add(Ui.Text(s.Text, "cy-tracker__text"));
                    if (s.Place.HasValue)
                    {
                        Label dist = Ui.Text("", "cy-tracker__dist");
                        row.Add(dist);
                        t._distances.Add((dist, s.Place.Value.X, s.Place.Value.Z, -1));
                    }
                    t._steps.Add(row);
                }
                UiFonts.Apply(t.Root);
            }
            // the distances move with the player: their text is written when what it says changes
            for (int i = 0; i < t._distances.Count; i++)
            {
                var d = t._distances[i];
                double dx = d.X - px, dz = d.Z - pz, metres = Math.Sqrt(dx * dx + dz * dz);
                long shown = HudModel.DistanceKey(metres);
                if (shown == d.Shown) continue;
                d.Label.text = HudModel.Distance(metres);
                t._distances[i] = (d.Label, d.X, d.Z, shown);
            }
        }
    }

    /// <summary>Top centre: a ribbon of ticks sliding with the heading, the cardinal points, the bearing, and a pip for each marker (the tracked one bright).</summary>
    public sealed class CompassView
    {
        const float Span = 130;   // degrees across the ribbon
        readonly VisualElement _ribbon;
        readonly Label _bearing;
        readonly List<Label> _labels = new List<Label>();
        double _heading = -999;
        List<(double Bearing, bool Tracked)> _pips = new List<(double, bool)>();

        public VisualElement Root { get; }
        public string BearingText => _bearing.text;

        public CompassView()
        {
            Root = Ui.Div("cy-compass");
            _ribbon = Ui.Div("cy-compass__ribbon");
            _ribbon.generateVisualContent += Draw;
            Root.Add(_ribbon);
            foreach ((int _, string label) in HudModel.Cardinals)
            {
                Label l = Ui.Text(label, "cy-compass__label");
                _labels.Add(l);
                _ribbon.Add(l);
            }
            _bearing = Ui.Text("000", "cy-compass__bearing");
            Root.Add(_bearing);
        }

        public void Update(double heading, List<(double Bearing, bool Tracked)> pips)
        {
            _pips = pips;
            if (Math.Abs(heading - _heading) < 0.05 && pips.Count == 0) return;
            _heading = heading;
            _bearing.text = ((int)Math.Round(heading) % 360).ToString("000");
            float w = _ribbon.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0) return;
            for (int i = 0; i < _labels.Count; i++)
            {
                double rel = HudModel.Relative(HudModel.Cardinals[i].Degrees, heading);
                bool on = Math.Abs(rel) < Span / 2 - 4;
                Ui.Show(_labels[i], on);
                _labels[i].style.left = (float)(w / 2 + rel * w / Span) - 14;
                _labels[i].EnableInClassList("cy-compass__label--north", HudModel.Cardinals[i].Degrees == 0);
            }
            _ribbon.MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            float w = _ribbon.contentRect.width, h = _ribbon.contentRect.height;
            if (w <= 0 || h <= 0) return;
            Painter2D p = ctx.painter2D;
            float perDeg = w / Span;
            double start = Math.Floor((_heading - Span / 2) / 5) * 5;
            for (double a = start; a <= _heading + Span / 2; a += 5)
            {
                double d = a - _heading;
                float x = (float)(w / 2 + d * perDeg);
                int norm = (((int)a % 360) + 360) % 360;
                bool major = norm % 45 == 0, mid = norm % 15 == 0;
                float fade = 1 - (float)Math.Min(1, Math.Abs(d) / (Span / 2)) * 0.82f;
                p.strokeColor = major ? new Color(0.75f, 0.98f, 1f, 0.85f * fade + 0.12f) : new Color(0.18f, 0.95f, 1f, (mid ? 0.5f : 0.24f) * fade + 0.04f);
                p.lineWidth = major ? 1.6f : 1f;
                float len = major ? h * 0.42f : mid ? h * 0.26f : h * 0.16f;
                p.BeginPath();
                p.MoveTo(new Vector2(x, h - 1));
                p.LineTo(new Vector2(x, h - 1 - len));
                p.Stroke();
            }
            foreach ((double bearing, bool tracked) in _pips)
            {
                double rel = HudModel.Relative(bearing, _heading);
                bool edge = Math.Abs(rel) > Span / 2 - 2;
                float x = (float)(w / 2 + Math.Max(-Span / 2 + 2, Math.Min(Span / 2 - 2, rel)) * perDeg);
                p.fillColor = tracked ? new Color(1f, 0.69f, 0.23f, edge ? 0.6f : 1f) : new Color(1f, 0.18f, 0.58f, edge ? 0.45f : 0.85f);
                p.BeginPath();
                p.MoveTo(new Vector2(x, h * 0.42f));
                p.LineTo(new Vector2(x + 5, h * 0.42f + 6));
                p.LineTo(new Vector2(x, h * 0.42f + 12));
                p.LineTo(new Vector2(x - 5, h * 0.42f + 6));
                p.ClosePath();
                p.Fill();
            }
        }
    }

    /// <summary>Top right: the blocks and buildings round the player, turned so ahead is up, the places, and the job markers.</summary>
    public sealed class MinimapView
    {
        const double Radius = 88;   // metres from the centre to the edge
        readonly VisualElement _map;
        readonly Label _foot;
        RpgCityMeta _city;
        IReadOnlyList<RpgLocation> _places = Array.Empty<RpgLocation>();
        IReadOnlyList<JsonObject> _markers = Array.Empty<JsonObject>();
        double _px = 1e9, _pz = 1e9, _yaw = 1e9;
        RpgLocation _footPlace;
        long _footShown = -1;

        public VisualElement Root { get; }

        public MinimapView()
        {
            Root = Ui.Div("cy-hud-panel", "cy-minimap");
            _map = Ui.Div("cy-minimap__map");
            _map.generateVisualContent += Draw;
            Root.Add(_map);
            _map.Add(Ui.Div("cy-minimap__player"));
            _foot = Ui.Text("", "cy-minimap__foot");
            Root.Add(_foot);
        }

        public void Update(RpgCityMeta city, IReadOnlyList<RpgLocation> places, IReadOnlyList<JsonObject> markers, double px, double pz, double yaw)
        {
            _city = city;
            _places = places;
            bool markersChanged = !ReferenceEquals(markers, _markers);
            _markers = markers;
            var nearest = RpgLocations.Nearest(places, px, pz);
            RpgLocation place = nearest?.Location;
            long shown = nearest.HasValue ? HudModel.DistanceKey(nearest.Value.Distance) : -1;
            if (place != _footPlace || shown != _footShown)
            {
                _footPlace = place;
                _footShown = shown;
                _foot.text = place != null ? $"{place.Name.ToUpperInvariant()}  {HudModel.Distance(nearest.Value.Distance)}" : "";
            }
            if (!markersChanged && Math.Abs(px - _px) < 0.3 && Math.Abs(pz - _pz) < 0.3 && Math.Abs(yaw - _yaw) < 0.01) return;
            _px = px;
            _pz = pz;
            _yaw = yaw;
            _map.MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            float s = Math.Min(_map.contentRect.width, _map.contentRect.height);
            if (s <= 0) return;
            Painter2D p = ctx.painter2D;
            float scale = (float)(s / 2 / Radius);
            Vector2 centre = new Vector2(s / 2, s / 2);
            Vector2 Screen(double x, double z)
            {
                (double r, double a) = HudModel.OnMap(x - _px, z - _pz, _yaw);
                return centre + new Vector2((float)(r * scale), (float)(-a * scale));
            }
            void Quad(double x0, double z0, double x1, double z1)
            {
                p.BeginPath();
                p.MoveTo(Screen(x0, z0));
                p.LineTo(Screen(x1, z0));
                p.LineTo(Screen(x1, z1));
                p.LineTo(Screen(x0, z1));
                p.ClosePath();
                p.Fill();
            }

            p.fillColor = new Color(0.024f, 0.035f, 0.06f, 1f);
            p.BeginPath();
            p.MoveTo(Vector2.zero);
            p.LineTo(new Vector2(s, 0));
            p.LineTo(new Vector2(s, s));
            p.LineTo(new Vector2(0, s));
            p.ClosePath();
            p.Fill();
            if (_city != null)
            {
                double reach = Radius * 1.5, half = _city.BlockSize / 2 + 2.5;
                p.fillColor = new Color(0.11f, 0.157f, 0.21f, 0.92f);
                foreach (var st in _city.Streets)
                {
                    if (Math.Abs(st.Cx - _px) > reach || Math.Abs(st.Cz - _pz) > reach) continue;
                    Quad(st.Cx - half, st.Cz - half, st.Cx + half, st.Cz + half);
                }
                p.fillColor = new Color(0.227f, 0.33f, 0.424f, 0.95f);
                foreach (var b in _city.Buildings)
                {
                    if (Math.Abs(b.X - _px) > reach || Math.Abs(b.Z - _pz) > reach) continue;
                    Quad(b.X - b.W / 2, b.Z - b.D / 2, b.X + b.W / 2, b.Z + b.D / 2);
                }
            }

            void Diamond(double x, double z, Color c, float size)
            {
                (double r, double a) = HudModel.OnMap(x - _px, z - _pz, _yaw);
                double dist = Math.Sqrt(r * r + a * a), lim = Radius * 0.94;
                bool edge = dist > lim;
                if (edge)
                {
                    r *= lim / dist;
                    a *= lim / dist;
                }
                Vector2 v = centre + new Vector2((float)(r * scale), (float)(-a * scale));
                p.fillColor = new Color(c.r, c.g, c.b, edge ? c.a * 0.7f : c.a);
                p.BeginPath();
                p.MoveTo(v + new Vector2(0, -size));
                p.LineTo(v + new Vector2(size * 0.82f, 0));
                p.LineTo(v + new Vector2(0, size));
                p.LineTo(v + new Vector2(-size * 0.82f, 0));
                p.ClosePath();
                p.Fill();
            }
            foreach (RpgLocation l in _places) Diamond(l.X, l.Z, new Color(0.18f, 0.95f, 1f, 0.55f), 3.5f);
            foreach (JsonObject m in _markers) Diamond((double)m["x"], (double)m["z"], (bool)m["tracked"] ? new Color(1f, 0.69f, 0.23f, 1f) : new Color(1f, 0.18f, 0.58f, 0.9f), 5.5f);
        }
    }

    /// <summary>The chevron over the tracked objective's place, with its distance; held at the screen's edge when the place is off it.</summary>
    public sealed class WaypointView
    {
        readonly Label _dist;
        long _distShown = -1;

        public VisualElement Root { get; }
        public bool Shown => Ui.IsShown(Root);
        public Vector2 Position { get; private set; }
        public bool AtEdge { get; private set; }

        public WaypointView()
        {
            Root = Ui.Div("cy-waypoint");
            Root.Add(Ui.Div("cy-waypoint__mark"));
            _dist = Ui.Text("", "cy-waypoint__dist");
            Root.Add(_dist);
        }

        public void Update(VisualElement panelRoot, Camera camera, JsonObject marker, double px, double pz)
        {
            Ui.Show(Root, marker != null);
            if (marker == null || panelRoot.panel == null) return;
            double x = (double)marker["x"], z = (double)marker["z"];
            var world = new Vector3((float)x, camera.transform.position.y, (float)z);
            Vector3 view = camera.WorldToViewportPoint(world);
            bool behind = view.z < 0;
            float w = panelRoot.resolvedStyle.width, h = panelRoot.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0) return;
            var pos = new Vector2(view.x * w, (1 - view.y) * h);
            if (behind) pos = new Vector2(w - pos.x, h - pos.y);
            const float margin = 60;
            AtEdge = behind || pos.x < margin || pos.x > w - margin || pos.y < margin || pos.y > h - margin;
            if (AtEdge)
            {
                // push it out along the line from the centre until it meets the frame
                Vector2 c = new Vector2(w / 2, h / 2), d = pos - c;
                if (d.sqrMagnitude < 1e-3f) d = new Vector2(0, h);
                float k = Math.Min((w / 2 - margin) / Math.Max(1e-3f, Math.Abs(d.x)), (h / 2 - margin) / Math.Max(1e-3f, Math.Abs(d.y)));
                pos = c + d * k;
            }
            Position = pos;
            Root.style.left = pos.x - 40;
            Root.style.top = pos.y - 26;
            Root.EnableInClassList("cy-waypoint--edge", AtEdge);
            double dx = x - px, dz = z - pz;
            double metres = Math.Sqrt(dx * dx + dz * dz);
            long shown = HudModel.DistanceKey(metres);
            if (shown == _distShown) return;
            _distShown = shown;
            _dist.text = HudModel.Distance(metres);
        }
    }

    /// <summary>The notices on the right, newest at the top, coloured by kind.</summary>
    public sealed class ToastView
    {
        readonly List<HudToast> _shown = new List<HudToast>();

        public VisualElement Root { get; } = Ui.Div("cy-hud-toasts");

        public void Show(IReadOnlyList<HudToast> toasts)
        {
            if (Same(toasts)) return;
            _shown.Clear();
            for (int i = 0; i < toasts.Count; i++) _shown.Add(toasts[i]);
            Root.Clear();
            foreach (HudToast t in toasts)
            {
                var row = Ui.Div("cy-hud-toast", "cy-hud-toast--" + t.Kind);
                row.Add(Ui.Text(t.Text, "cy-hud-toast__text", "cy-display"));
                if (!string.IsNullOrEmpty(t.Sub)) row.Add(Ui.Text(t.Sub, "cy-hud-toast__sub"));
                row.pickingMode = PickingMode.Ignore;
                Root.Add(row);
            }
            UiFonts.Apply(Root);
        }

        bool Same(IReadOnlyList<HudToast> toasts)
        {
            if (toasts.Count != _shown.Count) return false;
            for (int i = 0; i < toasts.Count; i++)
            {
                HudToast a = toasts[i], b = _shown[i];
                if (a.Kind != b.Kind || a.Text != b.Text || a.Sub != b.Sub || a.ExpiresAt != b.ExpiresAt) return false;
            }
            return true;
        }
    }
}
