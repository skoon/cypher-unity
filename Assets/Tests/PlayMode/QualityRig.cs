using System;
using System.Collections.Generic;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Cypher.Tests
{
    /// <summary>
    /// The pieces a <see cref="QualityDirector"/> drives, built the way the scene builder builds them, with a post volume
    /// whose profile is made here so the project's own profile is never in play. Everything is destroyed by Dispose.
    /// </summary>
    sealed class QualityRig : IDisposable
    {
        readonly List<Object> _owned = new List<Object>();

        public LowResController LowRes { get; }
        public RainController Rain { get; }
        public StreetReflectionProbe Probe { get; }
        public UnityEngine.Rendering.Volume PostVolume { get; }
        public QualityDirector Director { get; }

        /// <param name="activate">False leaves the director inactive so the test can configure it first (or not at all).</param>
        public QualityRig(bool activate = true, bool configure = true)
        {
            var renderingGo = Track(new GameObject("QualityRig Rendering"));
            LowRes = renderingGo.AddComponent<LowResController>();

            Shader rainShader = Shader.Find("Cypher/Rain");
            Assert.IsNotNull(rainShader, "Cypher/Rain shader not found");
            var rainMaterial = Track(new Material(rainShader));
            var follow = Track(new GameObject("QualityRig Camera"));
            var rainGo = Track(new GameObject("QualityRig Rain"));
            rainGo.SetActive(false);
            Rain = rainGo.AddComponent<RainController>();
            Rain.Configure(follow.transform, rainMaterial, 0.45f, 0.35f, RainController.DefaultMaxParticles);
            rainGo.SetActive(true);

            var probeGo = Track(new GameObject("QualityRig Probe"));
            Probe = probeGo.AddComponent<StreetReflectionProbe>();
            Probe.Configure(Vector3.zero, new Vector3(100f, 40f, 100f), StreetReflectionProbe.DefaultResolution, captureAutomatically: false);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _owned.Add(profile);
            profile.Add<Bloom>(true);
            var volumeGo = Track(new GameObject("QualityRig Volume"));
            PostVolume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>();
            PostVolume.isGlobal = true;
            PostVolume.sharedProfile = profile;

            var directorGo = Track(new GameObject("QualityRig Director"));
            directorGo.SetActive(false);
            Director = directorGo.AddComponent<QualityDirector>();
            if (configure) Director.Configure(LowRes, Rain, Probe, PostVolume);
            if (activate) directorGo.SetActive(true);
        }

        public Bloom LiveBloom
        {
            get
            {
                Assert.IsTrue(PostVolume.profile.TryGet(out Bloom b), "the live profile has no Bloom");
                return b;
            }
        }

        public Bloom SharedBloom
        {
            get
            {
                Assert.IsTrue(PostVolume.sharedProfile.TryGet(out Bloom b), "the shared profile has no Bloom");
                return b;
            }
        }

        GameObject Track(GameObject go)
        {
            _owned.Add(go);
            return go;
        }

        Material Track(Material m)
        {
            _owned.Add(m);
            return m;
        }

        public void Dispose()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                if (_owned[i]) Object.DestroyImmediate(_owned[i]);
            }
            _owned.Clear();
        }
    }
}
