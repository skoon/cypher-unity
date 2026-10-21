using Cypher.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Cypher.Perf
{
    /// <summary>
    /// FPS, frame time (average, worst and p99 of the last five seconds), draw calls and triangles in
    /// the top-left corner, drawn at native resolution by UI Toolkit on top of the upscaled scene.
    /// On by default; F3 hides and shows it. The numbers and wording come from Core
    /// (<see cref="PerfStats"/>, <see cref="PerfOverlayText"/>), so this only feeds frames and draws a label.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerfOverlay : MonoBehaviour
    {
        public const double WindowSeconds = 5.0;
        public const float RefreshSeconds = 0.25f;

        static readonly Color Good = new Color(0.45f, 1f, 0.55f);
        static readonly Color Bad = new Color(1f, 0.4f, 0.4f);
        static readonly Color Neutral = Color.white;

        [SerializeField] bool visible = true;
        [SerializeField, Min(8)] int fontSize = 18;

        readonly PerfStats _stats = new PerfStats(WindowSeconds);
        RenderCounters _counters;
        PanelSettings _panel;
        Label _label;
        InputAction _toggle;
        float _nextRefresh;
        int _updates;

        public PerfStats Stats => _stats;

        public RenderCounters Counters => _counters;

        /// <summary>What the label currently says.</summary>
        public string Text => _label != null ? _label.text : null;

        /// <summary>Green while the 30 FPS gate holds, red below it, white before any frame.</summary>
        public Color TextColor => _label.style.color.value;

        public bool Visible
        {
            get => visible;
            set
            {
                visible = value;
                if (_label != null) _label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void Awake()
        {
            _counters = new RenderCounters();
            BuildUi();
            Visible = visible;
            Refresh();
        }

        void OnEnable()
        {
            _toggle = new InputAction("TogglePerfOverlay", InputActionType.Button, "<Keyboard>/f3");
            _toggle.performed += _ => Visible = !Visible;
            _toggle.Enable();
        }

        void OnDisable()
        {
            _toggle?.Dispose();
            _toggle = null;
        }

        void OnDestroy()
        {
            _counters?.Dispose();
            if (_panel != null) Destroy(_panel);
        }

        void Update()
        {
            // The first delta is the time spent loading the scene, not a frame; it would pin the readout red for five seconds.
            double dt = Time.unscaledDeltaTime;
            if (_updates++ > 0 && dt > 0) _stats.AddFrame(dt);

            if (Time.unscaledTime >= _nextRefresh)
            {
                Refresh();
                _nextRefresh = Time.unscaledTime + RefreshSeconds;
            }
        }

        /// <summary>Redraw the label from the current stats.</summary>
        public void Refresh()
        {
            _label.text = PerfOverlayText.Format(_stats, _counters.DrawCalls, _counters.Triangles);
            _label.style.color = _stats.Count == 0 ? Neutral : _stats.MeetsFloor() ? Good : Bad;
        }

        void BuildUi()
        {
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "PerfOverlay panel";
            _panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panel.referenceResolution = new Vector2Int(1920, 1080);
            _panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _panel.match = 1f;   // follow the window height, so the text is the same size at any window size
            _panel.sortingOrder = 1000;

            // The document is created inactive so its panel is in place before it first enables.
            var go = new GameObject("PerfOverlay UI");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = _panel;
            go.SetActive(true);

            _label = new Label
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    left = 12,
                    top = 12,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 6,
                    paddingBottom = 6,
                    fontSize = fontSize,
                    unityFontDefinition = FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")),
                    unityTextAlign = TextAnchor.UpperLeft,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f),
                },
            };
            document.rootVisualElement.pickingMode = PickingMode.Ignore;
            document.rootVisualElement.Add(_label);
        }
    }
}
