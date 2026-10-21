using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Rendering
{
    /// <summary>
    /// Applies the prototype's night rig (Core.NightRig) to the scene: moon light, trilight ambient,
    /// fog, and the shader globals CityLit reads for the unshadowed colour fills and street uplight.
    /// Call <see cref="Apply"/> after changing time of day or weather. Shader globals are not saved
    /// with the scene, so this also applies on enable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NightLighting : MonoBehaviour
    {
        static readonly int[] FillDirIds =
        {
            Shader.PropertyToID("_CypherFillDir0"), Shader.PropertyToID("_CypherFillDir1"), Shader.PropertyToID("_CypherFillDir2"),
        };

        static readonly int[] FillColorIds =
        {
            Shader.PropertyToID("_CypherFillColor0"), Shader.PropertyToID("_CypherFillColor1"), Shader.PropertyToID("_CypherFillColor2"),
        };

        static readonly int ZenithId = Shader.PropertyToID("_Zenith");
        static readonly int HorizonId = Shader.PropertyToID("_Horizon");
        static readonly int GroundId = Shader.PropertyToID("_Ground");

        [SerializeField] Light moon;
        [SerializeField, Tooltip("Optional: clears to the sky, or to the fog colour when there is no sky material.")] Camera backgroundCamera;
        [SerializeField, Tooltip("Optional Cypher/SkyGradient material. Its colours are driven from the night rig.")] Material skyMaterial;
        [SerializeField, Range(0f, 24f)] float timeOfDay = (float)NightRig.DefaultTimeOfDay;
        [SerializeField, Range(0f, 1f)] float rain = (float)NightRig.DefaultRain;
        [SerializeField, Range(0f, 1f)] float fog = (float)NightRig.DefaultFog;

        public NightRigState State { get; private set; }

        public void Configure(Light moonLight, Camera camera, Material sky, float time, float rainAmount, float fogAmount)
        {
            moon = moonLight;
            backgroundCamera = camera;
            skyMaterial = sky;
            timeOfDay = time;
            rain = rainAmount;
            fog = fogAmount;
        }

        void OnEnable() => Apply();

        public void Apply()
        {
            NightRigState s = NightRig.Compute(timeOfDay, rain, fog);
            State = s;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ToColor(s.HemiSkyRadiance);
            RenderSettings.ambientGroundColor = ToColor(s.HemiGroundRadiance);
            RenderSettings.ambientEquatorColor = ToColor(Average(s.HemiSkyRadiance, s.HemiGroundRadiance));

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = ToColor(s.FogColour);
            RenderSettings.fogDensity = (float)s.FogDensity;

            if (skyMaterial != null)
            {
                skyMaterial.SetColor(ZenithId, ToColor(s.SkyZenith));
                skyMaterial.SetColor(HorizonId, ToColor(s.SkyHorizon));
                skyMaterial.SetColor(GroundId, ToColor(s.FogColour));   // below the horizon the street fades into fog
            }
            RenderSettings.skybox = skyMaterial;

            if (backgroundCamera != null)
            {
                backgroundCamera.clearFlags = skyMaterial != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                backgroundCamera.backgroundColor = ToColor(s.FogColour);
            }

            if (moon != null)
            {
                moon.type = LightType.Directional;
                moon.color = ToColor(s.Moon.Colour);
                moon.intensity = (float)(s.Moon.Intensity / System.Math.PI);
                moon.shadows = LightShadows.None;   // shadows are off for Milestone 1
                moon.transform.rotation = Quaternion.LookRotation(-ToVector(s.Moon.ToLight));
            }

            RigLight[] fills = { s.FillWarm, s.FillCool, s.Uplight };
            for (int i = 0; i < fills.Length; i++)
            {
                Shader.SetGlobalVector(FillDirIds[i], ToVector(fills[i].ToLight));
                Rgb r = fills[i].Radiance;
                Shader.SetGlobalVector(FillColorIds[i], new Vector4(r.R, r.G, r.B, 0f));
            }
        }

        /// <summary>Unity colour fields are authored in gamma space and converted to linear for shading.</summary>
        static Color ToColor(Rgb linear) => new Color(linear.R, linear.G, linear.B, 1f).gamma;

        static Vector3 ToVector(Vec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        static Rgb Average(Rgb a, Rgb b) => new Rgb((a.R + b.R) / 2f, (a.G + b.G) / 2f, (a.B + b.B) / 2f);
    }
}
