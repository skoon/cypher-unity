using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Actors
{
    /// <summary>
    /// The point lights that ride the vehicles nearest the camera, so the wet road reacts to them (the prototype's six pooled lights; two on Performance). Real URP lights, no shadows:
    /// the renderer is Forward+, which handles a handful of them cheaply. The prototype's PointLight(0xfff2dd, intensity 18, range 26, decay 2) in Unity's units: the intensity divided
    /// by pi, as the night fills are.
    /// </summary>
    public sealed class TrafficLights : IDisposable
    {
        public const float Range = 26f;
        public const float Intensity = 18f / Mathf.PI;
        public static readonly Color Colour = new Color(1f, 0.949f, 0.867f);

        readonly Light[] _lights = new Light[QualityTiers.MaxVehicleLights];

        /// <summary>How many lights shine now.</summary>
        public int Lit { get; private set; }

        public IReadOnlyList<Light> Lights => _lights;

        public TrafficLights(Transform parent)
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                var go = new GameObject($"VehicleLight_{i}");
                if (parent != null) go.transform.SetParent(parent, false);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = Colour;
                light.range = Range;
                light.intensity = Intensity;
                light.shadows = LightShadows.None;
                light.enabled = false;
                _lights[i] = light;
            }
        }

        /// <summary>Put the lights on the first <paramref name="allowed"/> of the places given (nearest the camera first) and switch the rest off.</summary>
        public void Apply(IReadOnlyList<(double d2, double x, double y, double z)> spots, int allowed)
        {
            if (spots == null) throw new ArgumentNullException(nameof(spots));
            if (allowed < 0 || allowed > _lights.Length) throw new ArgumentOutOfRangeException(nameof(allowed), allowed, $"0 to {_lights.Length} lights");
            Lit = Math.Min(allowed, spots.Count);
            for (int i = 0; i < _lights.Length; i++)
            {
                if (i < Lit)
                {
                    _lights[i].transform.position = new Vector3((float)spots[i].x, (float)spots[i].y, (float)spots[i].z);
                    _lights[i].enabled = true;
                }
                else
                {
                    _lights[i].enabled = false;
                }
            }
        }

        /// <summary>Switch every light off (the traffic is gone).</summary>
        public void Off()
        {
            Lit = 0;
            foreach (Light l in _lights) l.enabled = false;
        }

        public void Dispose()
        {
            foreach (Light l in _lights)
            {
                if (l == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(l.gameObject);
                else UnityEngine.Object.DestroyImmediate(l.gameObject);
            }
        }
    }
}
