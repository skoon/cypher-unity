using System;
using System.Collections.Generic;
using Cypher.Core;
using Cypher.Fight;
using UnityEngine;

namespace Cypher.Audio
{
    /// <summary>
    /// The enemies' guns: each shot is the gun's own recipe (the player's guns' sounds) played where the muzzle is, so it is heard from its direction and fades with
    /// distance; the impact where a miss lands; a crack past the ear when one comes close; the score bumped as the player's shots bump it. And the player's
    /// quickhacks on them: the upload's confirm (or the error when it is refused), and a Short Circuit's snap where it strikes.
    /// </summary>
    public sealed class EnemyGunAudio : IDisposable
    {
        /// <summary>A miss passing within this of the player cracks past the ear.</summary>
        public const float CrackDistance = 2.5f;

        readonly AudioEngine _engine;
        readonly EnemyController _fight;
        readonly TakePicker _takes = new TakePicker(0xE6A);
        readonly List<string> _played = new List<string>();

        /// <summary>The sounds that played lately (the last 32), for tests.</summary>
        public IReadOnlyList<string> Played => _played;

        public EnemyGunAudio(AudioEngine engine, EnemyController fight)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _fight = fight ?? throw new ArgumentNullException(nameof(fight));
            _fight.EnemyFired += OnFired;
            _fight.HackUploaded += OnHack;
            _fight.HackRefused += OnRefused;
        }

        public void Dispose()
        {
            _fight.EnemyFired -= OnFired;
            _fight.HackUploaded -= OnHack;
            _fight.HackRefused -= OnRefused;
        }

        void OnHack(Enemy target, ActiveHack hack)
        {
            _engine.PlayUi("ui.confirm");
            _played.Add("hack." + hack.Effect);
            if (_played.Count > 32) _played.RemoveAt(0);
            if (hack.Effect == "shock") Play("weapon.energy", new Vector3((float)target.Position.X, (float)target.Position.Y + 1.2f, (float)target.Position.Z), 0.7f);
        }

        void OnRefused(string reason) => _engine.PlayUi("ui.error");

        void Play(string sound, Vector3? position, float volume)
        {
            int takes = 0;
            foreach ((string s, int t) in ClipPlan.WeaponSounds) if (s == sound) takes = t;
            if (takes == 0) throw new InvalidOperationException($"{sound} has no clips; add it to ClipPlan.WeaponSounds");
            if (!_engine.PlayClip(ClipPlan.WeaponKey(sound, _takes.Pick(sound, takes)), sound, position, volume, 1f, 0)) return;
            _played.Add(sound);
            if (_played.Count > 32) _played.RemoveAt(0);
        }

        void OnFired(EnemyShotFx fx)
        {
            Play(GunAudio.ShotSound(fx.Gun), fx.Muzzle, 1f);
            if (fx.Impact) Play("impact." + SoundBank.ImpactFor(fx.Surface), fx.End, 0.8f);
            if (!fx.HitPlayer && PassesClose(fx.Muzzle, fx.End, _engine.ListenerPosition)) Play("weapon.crack", null, 0.5f);
            _engine.BumpMusic(0.2);
        }

        /// <summary>Whether the segment from <paramref name="a"/> to <paramref name="b"/> comes within <see cref="CrackDistance"/> of <paramref name="ear"/>.</summary>
        public static bool PassesClose(Vector3 a, Vector3 b, Vector3 ear)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ear - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - ear).sqrMagnitude <= CrackDistance * CrackDistance;
        }
    }
}
