using Cypher.Core;
using Cypher.Player;
using UnityEngine;

namespace Cypher.Audio
{
    /// <summary>
    /// The player's own sounds, from what the character controller did this frame: a footstep each stride on whatever is underfoot (puddles in the rain, grates, gravel), the jump, the landing
    /// with its weight, and breathing that quickens and deepens with exertion. All are head-relative, so they play flat; the reverb places them. The decisions are Core's.
    /// </summary>
    public sealed class PlayerFoley
    {
        readonly StrideClock _stride = new StrideClock();
        readonly BreathClock _breath;
        readonly TakePicker _takes = new TakePicker(21);
        readonly AudioEngine _engine;

        public int Steps { get; private set; }
        public int Jumps { get; private set; }
        public int Landings { get; private set; }
        public int Breaths { get; private set; }

        /// <summary>The surface the last step was on (for tests and the log).</summary>
        public string LastSurface { get; private set; }

        public PlayerFoley(AudioEngine engine, double firstBreath)
        {
            _engine = engine;
            _breath = new BreathClock(firstBreath);
        }

        /// <summary>Once a frame, after the player has moved. <paramref name="now"/> is the engine's world clock.</summary>
        public void Update(FirstPersonController player, double dt, double now, double wetness)
        {
            PlayerMotor m = player.Motor;
            Vector3 feet = player.transform.position;
            string surface = Footing.SurfaceAt(feet.x, feet.z, CityLayout.IsOnRoad(feet.x, feet.z), wetness);
            int wet = ClipPlan.WetBucket(wetness);

            bool moving = m.PlanarSpeed > 0.2;
            if (_stride.Update(dt, m.Grounded, moving, m.PlanarSpeed, m.Crouching, m.Sprinting))
            {
                ClipPlan.Stance stance = m.Crouching ? ClipPlan.Stance.Crouch : m.Sprinting ? ClipPlan.Stance.Sprint : ClipPlan.Stance.Walk;
                int take = _takes.Pick("step." + surface + stance, ClipPlan.StepTakes);
                float volume = m.Crouching ? 0.55f : m.Sprinting ? 1.05f : 0.85f;
                _engine.PlayClip(ClipPlan.StepKey(surface, stance, wet, take), "foot." + surface, null, volume);
                LastSurface = surface;
                Steps++;
            }

            if (m.Jumped)
            {
                _engine.PlayClip(ClipPlan.JumpKey(_takes.Pick("jump", ClipPlan.JumpTakes)), "player.jump", null, 0.9f);
                Jumps++;
            }

            if (m.LandImpact > 0)
            {
                double impact = System.Math.Min(1.4, m.LandImpact);
                int bucket = ClipPlan.ImpactBucket(impact);
                int take = _takes.Pick("land." + surface, ClipPlan.LandTakes);
                _engine.PlayClip(ClipPlan.LandKey(surface, bucket, wet, take), "player.land", null, (float)(0.55 + impact * 0.8));
                // a hard landing gets the music's attention, as in the prototype
                if (impact > 0.75) _engine.BumpMusic(0.12);
                Landings++;
            }

            BreathEvent? breath = _breath.Update(dt, now, m.PlanarSpeed, m.Sprinting);
            if (breath.HasValue)
            {
                BreathEvent b = breath.Value;
                int take = _takes.Pick("breath." + b.Inhale, ClipPlan.BreathTakes);
                _engine.PlayClip(ClipPlan.BreathKey(b.Inhale, ClipPlan.EffortBucket(b.Effort), take), "player.breath", null, (float)b.Volume, startWorldTime: b.Time, busOverride: AudioBus.Voice);
                Breaths++;
            }
        }
    }
}
