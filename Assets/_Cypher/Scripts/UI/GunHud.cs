using System;
using System.Collections.Generic;
using System.Globalization;
using Cypher.Combat;
using Cypher.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>
    /// The gun's part of the HUD, after the prototype's <c>ui/hud/WeaponPanel.js</c> and <c>Crosshair.js</c>: the weapon panel at the bottom right (slot,
    /// name, kind, a pip per round, the count and the reserve, the fire modes, low and dry and reloading states), the crosshair's blades opening with the gun's
    /// real spread and each shot's bloom, and the hit marker (white on a hit, red and turned on a person).
    /// </summary>
    public sealed class GunHud
    {
        static readonly IReadOnlyDictionary<FireMode, string[]> ModeSets = new Dictionary<FireMode, string[]>
        {
            [FireMode.Auto] = new[] { "SEMI", "BURST", "FULL AUTO" }, [FireMode.Semi] = new[] { "SEMI", "BURST" }, [FireMode.Charge] = new[] { "TAP", "CHARGE" },
        };

        static readonly IReadOnlyDictionary<FireMode, string> ModeLabel = new Dictionary<FireMode, string>
        {
            [FireMode.Auto] = "FULL AUTO", [FireMode.Semi] = "SEMI", [FireMode.Charge] = "CHARGE",
        };

        readonly WeaponSystem _guns;
        readonly Reticle _reticle = new Reticle();
        readonly VisualElement _crosshair, _bladeT, _bladeB, _bladeL, _bladeR, _hit;
        readonly Label _slot, _name, _kind, _ammo, _reserve;
        readonly VisualElement _pips, _modes;
        string _gunId;
        int _ammoShown = -1, _reserveShown = -1;

        public VisualElement Panel { get; }
        public Reticle Reticle => _reticle;
        public string AmmoText => _ammo.text;
        public string NameText => _name.text;
        public double HitOpacity => _hit.resolvedStyle.opacity;
        public bool HitCritical => _hit.ClassListContains("cy-hitmark--crit");

        public GunHud(WeaponSystem guns, VisualElement crosshair)
        {
            _guns = guns ?? throw new ArgumentNullException(nameof(guns));
            _crosshair = crosshair ?? throw new ArgumentNullException(nameof(crosshair));
            _bladeT = crosshair.Q(className: "cy-crosshair__t");
            _bladeB = crosshair.Q(className: "cy-crosshair__b");
            _bladeL = crosshair.Q(className: "cy-crosshair__l");
            _bladeR = crosshair.Q(className: "cy-crosshair__r");
            _hit = Ui.Div("cy-hitmark");
            for (int i = 0; i < 4; i++) _hit.Add(Ui.Div("cy-hitmark__bar", "cy-hitmark__bar--" + i));
            crosshair.Add(_hit);

            Panel = Ui.Div("cy-hud-panel", "cy-gun");
            var head = Ui.Div("cy-hud-head");
            _slot = Ui.Text("", "cy-hud-sub");
            head.Add(_slot);
            head.Add(Ui.Text("ARMAMENT", "cy-hud-tag"));
            Panel.Add(head);
            _name = Ui.Display("", "cy-gun__name");
            Panel.Add(_name);
            _kind = Ui.Text("", "cy-gun__kind");
            Panel.Add(_kind);
            _pips = Ui.Div("cy-gun__pips");
            Panel.Add(_pips);
            var count = Ui.Div("cy-gun__count");
            _ammo = Ui.Display("", "cy-gun__ammo");
            count.Add(_ammo);
            count.Add(Ui.Text("/", "cy-gun__slash"));
            _reserve = Ui.Text("", "cy-gun__reserve");
            count.Add(_reserve);
            Panel.Add(count);
            _modes = Ui.Div("cy-gun__modes");
            Panel.Add(_modes);
            foreach (VisualElement e in Panel.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            _hit.pickingMode = PickingMode.Ignore;

            guns.Shot += _ => _reticle.Kick();
            guns.GunEvent += e =>
            {
                if (e.Kind == WeaponEventKind.DryFire) _reticle.Kick(0.35);
            };
        }

        public void Update(Camera camera, double dt, float screenHeight)
        {
            WeaponRig rig = _guns.Rig;
            if (rig == null) return;
            WeaponSlot w = rig.Current;
            WeaponConfig cfg = w.Config;
            if (cfg.Id != _gunId)
            {
                _gunId = cfg.Id;
                _slot.text = $"SLOT {cfg.Slot:00} // SMARTLINK";
                _name.text = cfg.Name;
                _kind.text = cfg.Kind;
                _pips.Clear();
                for (int i = 0; i < Math.Max(1, Math.Min(40, cfg.MagSize)); i++) _pips.Add(Ui.Div("cy-gun__pip"));
                _modes.Clear();
                foreach (string m in ModeSets[cfg.Mode])
                {
                    Label l = Ui.Text(m, "cy-gun__mode");
                    l.EnableInClassList("cy-gun__mode--on", m == ModeLabel[cfg.Mode]);
                    _modes.Add(l);
                }
                UiFonts.Apply(Panel);
            }
            if (w.Ammo != _ammoShown)
            {
                _ammoShown = w.Ammo;
                _ammo.text = w.Ammo.ToString("00", CultureInfo.InvariantCulture);
            }
            if (w.Reserve != _reserveShown)
            {
                _reserveShown = w.Reserve;
                _reserve.text = w.Reserve.ToString("#,0", CultureInfo.InvariantCulture);
            }
            for (int i = 0; i < _pips.childCount; i++) _pips[i].EnableInClassList("cy-gun__pip--spent", i >= w.Ammo);
            double ratio = (double)w.Ammo / Math.Max(1, cfg.MagSize);
            Panel.EnableInClassList("cy-gun--low", ratio <= 0.34 && ratio > 0);
            Panel.EnableInClassList("cy-gun--dry", w.Ammo == 0);
            Panel.EnableInClassList("cy-gun--reloading", rig.State == WeaponState.Reload);

            // the crosshair opens to the gun's actual cone, projected onto this screen
            bool reloading = rig.State == WeaponState.Reload;
            double fov = camera != null ? camera.fieldOfView : ViewmodelPose.CameraFov;
            _reticle.Update(dt, Reticle.SpreadPixels(rig.Spread, fov, screenHeight), reloading);
            float g = (float)_reticle.DrawnGap;
            _bladeT.style.top = -g - 8;
            _bladeB.style.top = g;
            _bladeL.style.left = -g - 8;
            _bladeR.style.left = g;
            _crosshair.EnableInClassList("cy-crosshair--aim", rig.Ads > 0.5);

            double flash = rig.HitFlash;
            _hit.style.opacity = (float)(flash * flash);
            _hit.EnableInClassList("cy-hitmark--crit", rig.HitCritical > 0.01);
        }
    }
}
