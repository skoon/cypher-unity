using System;
using Cypher.Core;
using Cypher.Player;
using UnityEngine;

namespace Cypher.Perf
{
    /// <summary>Feeds a <see cref="BenchmarkPilot"/>'s commands to the first-person controller in place of the keyboard.</summary>
    public sealed class PilotCommandSource : IPlayerCommandSource
    {
        readonly FirstPersonController _player;

        public BenchmarkPilot Pilot { get; }

        public PilotCommandSource(FirstPersonController player, BenchmarkPilot pilot)
        {
            _player = player != null ? player : throw new ArgumentNullException(nameof(player));
            Pilot = pilot ?? throw new ArgumentNullException(nameof(pilot));
        }

        public PlayerCommand Poll(double dt)
        {
            Vector3 p = _player.transform.position;
            return Pilot.Next(p.x, p.z, _player.Look.Yaw, _player.Look.Pitch, dt);
        }
    }
}
