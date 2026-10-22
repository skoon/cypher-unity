using System.Collections.Generic;
using Cypher.City;
using Cypher.Core;
using Cypher.Player;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    /// <summary>Feeds the controller whatever the test sets; a jump press is an edge, delivered once.</summary>
    sealed class ScriptedInput : IPlayerCommandSource
    {
        public double Forward, Strafe, LookX, LookY;
        public bool Sprint, Crouch;
        bool _jump;

        public void PressJump() => _jump = true;

        public void Clear()
        {
            Forward = Strafe = LookX = LookY = 0;
            Sprint = Crouch = false;
        }

        public PlayerCommand Poll(double dt)
        {
            var cmd = new PlayerCommand(Forward, Strafe, Sprint, Crouch, _jump, LookX, LookY);
            _jump = false;
            LookX = LookY = 0;   // look is a per-frame delta
            return cmd;
        }
    }

    /// <summary>Plain materials with the city's real shaders, enough to build and walk the city in a test.</summary>
    static class CityTestMaterials
    {
        public static CityMaterials Make(List<Object> owned, bool withProps = false)
        {
            Shader shader = Shader.Find("Cypher/CityLit");
            Assert.IsNotNull(shader, "Cypher/CityLit shader not found");
            Shader neonShader = Shader.Find("Cypher/NeonAdd");
            Assert.IsNotNull(neonShader, "Cypher/NeonAdd shader not found");
            Material M(Shader sh)
            {
                var m = new Material(sh);
                owned.Add(m);
                return m;
            }
            var mats = new CityMaterials { Walls = M(shader), Roofs = M(shader), Sidewalk = M(shader), Road = M(shader), Paint = M(shader), Metal = M(shader), Neon = M(neonShader) };
            Shader boardShader = Shader.Find("Cypher/NeonBoard");
            Assert.IsNotNull(boardShader, "Cypher/NeonBoard shader not found");
            mats.Boards = M(boardShader);
            mats.Holos = M(boardShader);
            mats.Holos.SetFloat("_Holo", 1f);
            if (!withProps) return mats;

            Shader steamShader = Shader.Find("Cypher/Steam");
            Assert.IsNotNull(steamShader, "Cypher/Steam shader not found");
            mats.Props = M(shader);
            mats.Props.SetFloat("_PropSurface", 1f);
            mats.Decals = M(shader);
            mats.Decals.SetFloat("_PropSurface", 2f);
            mats.Steam = M(steamShader);
            return mats;
        }
    }

    /// <summary>Builds the real city and a player on it. Everything made here is destroyed by Dispose.</summary>
    sealed class PlayerTestRig : System.IDisposable
    {
        public const float Dt = 1f / 60f;

        readonly List<Object> _owned = new List<Object>();

        public CityBuilder City { get; }
        public FirstPersonController Player { get; }
        public ScriptedInput Input { get; } = new ScriptedInput();
        public Transform CameraPivot { get; }

        /// <param name="windowRadius">1 is the 3x3 slice; 3 is the whole city, with its world edge.</param>
        public PlayerTestRig(int windowRadius = 1, bool withProps = false)
        {
            // Inactive while configuring: CityBuilder builds in Awake, which must see its materials.
            var cityGo = new GameObject("TestCity");
            _owned.Add(cityGo);
            cityGo.SetActive(false);
            City = cityGo.AddComponent<CityBuilder>();
            City.Configure(CityPlan.DefaultSeed, windowRadius, CityTestMaterials.Make(_owned, withProps));
            cityGo.SetActive(true);
            Assert.IsNotNull(City.Slice, "Awake should have built the city");

            var playerGo = new GameObject("TestPlayer");
            _owned.Add(playerGo);
            playerGo.SetActive(false);   // so Awake/OnEnable run after the source and camera are in place
            Player = playerGo.AddComponent<FirstPersonController>();
            Player.AutoTick = false;
            Player.CommandSource = Input;

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(playerGo.transform, false);
            Player.CameraPivot = pivot.transform;
            CameraPivot = pivot.transform;
            playerGo.SetActive(true);

            Physics.SyncTransforms();
        }

        public void Place(Vector3 feet, float yawDegrees = 0f)
        {
            Player.Teleport(feet, yawDegrees);
            Physics.SyncTransforms();
        }

        /// <summary>Tick the player for the given number of seconds at a fixed 60 Hz step.</summary>
        public void Run(float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++) Player.Tick(Dt);
        }

        /// <summary>Drop the player from a little above the spot and let it come to rest.</summary>
        public void PlaceAndSettle(Vector3 feet, float yawDegrees = 0f)
        {
            Place(feet, yawDegrees);
            Run(1.5f);
            Assert.IsTrue(Player.Motor.Grounded, $"player did not settle at {feet}");
        }

        public GameObject Track(GameObject go)
        {
            _owned.Add(go);
            return go;
        }

        public void Dispose()
        {
            foreach (Object o in _owned)
            {
                if (o) Object.DestroyImmediate(o);
            }
            _owned.Clear();
        }
    }
}
