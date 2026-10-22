using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cypher.City
{
    /// <summary>
    /// Keeps a block's props and decals out of the frame when the block is farther than the draw distance, and owns the steam. Props are
    /// small and the fog is thick, so distant ones cost draw calls and triangles for nothing. Each block is two renderers (props, decals)
    /// registered with the box they fit in; every fifth of a second the blocks are switched on or off by their distance from the viewer, with a
    /// margin so a block at the edge does not flicker. The draw distance comes from the quality tier (<see cref="Cypher.Core.QualityTier.PropDistance"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PropDressing : MonoBehaviour
    {
        /// <summary>A visible block stays visible this much farther out than a hidden one appears.</summary>
        public const float Margin = 8f;

        public const float RefreshSeconds = 0.2f;

        sealed class Block
        {
            public Bounds Bounds;
            public Renderer[] Renderers;
            public bool Visible = true;
        }

        readonly List<Block> _blocks = new List<Block>();
        float _distance = 125f;
        float _timer;
        Renderer _steam;
        bool _steamWanted = true;

        /// <summary>Where distance is measured from. When null, the main camera.</summary>
        public Transform Viewer { get; set; }

        public int BlockCount => _blocks.Count;

        public int VisibleBlocks
        {
            get
            {
                int n = 0;
                foreach (Block b in _blocks)
                {
                    if (b.Visible) n++;
                }
                return n;
            }
        }

        /// <summary>How far from the viewer a block is still drawn, in metres. Setting it applies at once.</summary>
        public float DrawDistance
        {
            get => _distance;
            set
            {
                if (!(value >= 0f) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "the draw distance must be a finite number of metres, 0 or more");
                _distance = value;
                Refresh();
            }
        }

        /// <summary>Whether the steam may show. It also needs a renderer registered with <see cref="SetSteam"/>.</summary>
        public bool SteamVisible
        {
            get => _steamWanted;
            set
            {
                _steamWanted = value;
                if (_steam != null) _steam.enabled = value;
            }
        }

        /// <summary>Register one block: the renderers to switch together and the bounds that decide when.</summary>
        public void Add(Bounds bounds, params Renderer[] renderers)
        {
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));
            var live = new List<Renderer>();
            foreach (Renderer r in renderers)
            {
                if (r != null) live.Add(r);
            }
            if (live.Count == 0) return;
            _blocks.Add(new Block { Bounds = bounds, Renderers = live.ToArray() });
        }

        public void SetSteam(Renderer steam)
        {
            _steam = steam;
            if (_steam != null) _steam.enabled = _steamWanted;
        }

        void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = RefreshSeconds;
            Refresh();
        }

        /// <summary>Switch every block by its distance from the viewer now. Does nothing without a viewer.</summary>
        public void Refresh()
        {
            Transform viewer = Viewer;
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            if (viewer == null) return;
            Refresh(viewer.position);
        }

        public void Refresh(Vector3 viewerPosition)
        {
            foreach (Block b in _blocks)
            {
                float limit = _distance + (b.Visible ? Margin : 0f);
                bool visible = b.Bounds.SqrDistance(viewerPosition) < limit * limit;
                if (visible == b.Visible) continue;
                b.Visible = visible;
                foreach (Renderer r in b.Renderers)
                {
                    if (r != null) r.enabled = visible;
                }
            }
        }
    }
}
