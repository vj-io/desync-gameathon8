using System.Collections.Generic;
using UnityEngine;

namespace Desync.Art
{
    /// Glyph rain: one ParticleSystem (<= 360 billboard streaks, mat.glyphrain) parented to the player camera, world-aligned,
    /// emitting only in SOURCE. PostFXController attaches it whenever PlayerRef.Cam changes.
    public class GlyphRain : MonoBehaviour
    {
        const int MaxParticles = 360;
        ParticleSystem _ps;

        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.SizeXY, ParticleSystemVertexStream.StableRandomX,
        };

        public static GlyphRain Attach(Camera cam)
        {
            var mat = AssetLib.Get<Material>("mat.glyphrain");
            if (!mat || !cam) return null;
            var go = new GameObject("GlyphRain");
            go.transform.SetParent(cam.transform, false);
            var rain = go.AddComponent<GlyphRain>();
            rain.Build(mat);
            return rain;
        }

        void Build(Material mat)
        {
            _ps = gameObject.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.maxParticles = MaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.13f, 0.2f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.8f, 2.0f);
            main.startSizeZ = 1f;
            main.startColor = Color.white;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = _ps.emission;
            emission.rateOverTime = 150f;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(16f, 9f, 16f);
            shape.position = new Vector3(0f, 2.5f, 0f);

            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(-7f, -3.5f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var colour = _ps.colorOverLifetime;
            colour.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            colour.color = g;

            var r = GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            r.sharedMaterial = mat;
            r.sortMode = ParticleSystemSortMode.None;
            r.maxParticleSize = 2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.SetActiveVertexStreams(Streams);

            OnLayer(LayerState.InSource);
        }

        void OnEnable() => LayerState.Changed += OnLayer;
        void OnDisable() => LayerState.Changed -= OnLayer;

        void OnLayer(bool source)
        {
            if (!_ps) return;
            if (source)
            {
                _ps.Play();
                _ps.Emit(140);   // fill the volume at once instead of waiting for the first streaks to fall in
            }
            else _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // Follow the camera's position but stay world-aligned so the emitter box never tilts with the view.
        void LateUpdate() => transform.rotation = Quaternion.identity;
    }
}
