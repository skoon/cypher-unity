using System;
using Cypher.Combat;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Audio
{
    /// <summary>
    /// The guns' sounds, as the prototype's audio plays them (<c>_weaponFire</c>, <c>_weaponHit</c> and the weapon events in <c>audio/Audio.js</c>): the shot of
    /// the gun's profile, head-locked (it is the player's own), with the brass on any gun but the rail and the shotgun, the supersonic crack on a tracer round, a
    /// click when the last round goes, and a bump to the score; the impact where the bullet lands; the reload, the switch and the dry fire.
    /// </summary>
    public sealed class GunAudio : IDisposable
    {
        readonly AudioEngine _engine;
        readonly WeaponSystem _guns;
        readonly TakePicker _takes = new TakePicker(0x6A5);
        readonly System.Collections.Generic.List<string> _played = new System.Collections.Generic.List<string>();

        /// <summary>The sounds that played lately (the last 32), for tests.</summary>
        public System.Collections.Generic.IReadOnlyList<string> Played => _played;

        public GunAudio(AudioEngine engine, WeaponSystem guns)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _guns = guns ?? throw new ArgumentNullException(nameof(guns));
            _guns.Shot += OnShot;
            _guns.GunEvent += OnEvent;
        }

        public void Dispose()
        {
            _guns.Shot -= OnShot;
            _guns.GunEvent -= OnEvent;
        }

        /// <summary>The sound a gun's shot makes: its profile from its class (the prototype's <c>gunProfile</c>).</summary>
        public static string ShotSound(WeaponConfig gun)
        {
            string profile = SoundBank.GunProfileFor(gun.Kind);
            return profile == "energy" ? "weapon.energy" : "weapon." + profile;
        }

        bool Play(string sound, Vector3? position, float volume, double delay = 0)
        {
            int takes = 0;
            foreach ((string s, int t) in ClipPlan.WeaponSounds) if (s == sound) takes = t;
            if (takes == 0) throw new InvalidOperationException($"{sound} has no clips; add it to ClipPlan.WeaponSounds");
            string key = ClipPlan.WeaponKey(sound, _takes.Pick(sound, takes));
            bool played = _engine.PlayClip(key, sound, position, volume, 1f, delay > 0 ? _engine.WorldTime + delay : 0);
            if (played)
            {
                _played.Add(sound);
                if (_played.Count > 32) _played.RemoveAt(0);
            }
            return played;
        }

        void OnShot(ShotResult r)
        {
            WeaponConfig gun = r.Shot.Weapon;
            string sound = ShotSound(gun);
            string profile = SoundBank.GunProfileFor(gun.Kind);
            Play(sound, null, 1f);
            if (profile != "energy" && profile != "shotgun") Play("weapon.shell", null, 0.5f);
            if (r.Shot.Tracer || profile == "rifle" || profile == "sniper") Play("weapon.crack", null, 0.4f, 0.02);
            if (_guns.Rig.Current.Config == gun && _guns.Rig.Current.Ammo == 0) Play("weapon.dryfire", null, 0.35f, 0.13);
            _engine.BumpMusic(0.32);
            if (r.Hit) Play("impact." + SoundBank.ImpactFor(r.Surface), r.Point, 1f);
        }

        void OnEvent(WeaponEvent e)
        {
            switch (e.Kind)
            {
                case WeaponEventKind.ReloadStart: Play("weapon.reload", null, 0.9f); break;
                case WeaponEventKind.Switch: Play("weapon.switch", null, 0.85f); break;
                case WeaponEventKind.DryFire: Play("weapon.dryfire", null, 0.8f); break;
            }
        }
    }
}
