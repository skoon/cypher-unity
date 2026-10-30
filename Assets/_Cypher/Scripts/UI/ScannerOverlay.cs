using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>
    /// The optical scanner (hold Scan, F), after the prototype's <c>ui/Scanner.js</c>: the view dims, a sweep runs down it, a meter charges, and brackets close
    /// on the nearest contacts in view with a card each (what it is, what is known, what can be done, how far). The contacts are real: the named places, the
    /// people and the vehicles. An overlay over play: it does not stop the world or the player.
    /// </summary>
    public sealed class ScannerOverlay
    {
        sealed class Tag
        {
            public VisualElement Root, Box, Card;
            public Label Name, Sub, Action, Distance;
        }

        readonly VisualElement _sweep = Ui.Div("cy-scan__sweep");
        readonly VisualElement _meterFill = Ui.Div("cy-scan__meterfill");
        readonly Label _state = Ui.Text("", "cy-scan__state");
        readonly Label _readout = Ui.Text("", "cy-scan__readout");
        readonly List<Tag> _tags = new List<Tag>();
        double _charge;
        double _sweepT;

        public VisualElement Root { get; } = Ui.Div("cy-screen", "cy-scan");
        public double Charge => _charge;
        public string State => _state.text;
        public string ReadoutText => _readout.text;

        /// <summary>The contacts tagged this frame, nearest first.</summary>
        public IReadOnlyList<(ScanContact Contact, double Distance)> Tagged { get; private set; } = Array.Empty<(ScanContact, double)>();

        public ScannerOverlay()
        {
            Root.pickingMode = PickingMode.Ignore;
            Root.Add(Ui.Div("cy-scan__dim"));
            Root.Add(_sweep);
            var layer = Ui.Div("cy-scan__tags");
            for (int i = 0; i < ScanModel.MaxTags; i++)
            {
                var t = new Tag { Root = Ui.Div("cy-tag"), Box = Ui.Div("cy-tag__box"), Card = Ui.Div("cy-tag__card") };
                foreach (string corner in new[] { "cy-tag__c--tl", "cy-tag__c--tr", "cy-tag__c--bl", "cy-tag__c--br" }) t.Box.Add(Ui.Div("cy-tag__c", corner));
                t.Name = Ui.Text("", "cy-tag__name");
                t.Sub = Ui.Text("", "cy-tag__sub");
                t.Action = Ui.Text("", "cy-tag__action");
                t.Distance = Ui.Text("", "cy-tag__dist");
                t.Card.Add(t.Name);
                t.Card.Add(t.Sub);
                t.Card.Add(t.Action);
                t.Card.Add(t.Distance);
                t.Root.Add(t.Box);
                t.Root.Add(t.Card);
                Ui.Show(t.Root, false);
                _tags.Add(t);
                layer.Add(t.Root);
            }
            Root.Add(layer);
            var hud = Ui.Div("cy-scan__hud");
            var title = Ui.Div("cy-scan__title");
            title.Add(Ui.Display("OPTICAL SCAN", "cy-scan__label"));
            title.Add(_state);
            hud.Add(title);
            var meter = Ui.Div("cy-scan__meter");
            meter.Add(_meterFill);
            hud.Add(meter);
            hud.Add(_readout);
            Root.Add(hud);
            foreach (VisualElement e in Root.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            Ui.Show(Root, false);
        }

        /// <summary>Charge or discharge, and tag what is in view. <paramref name="contacts"/> is only read while the scanner shows.</summary>
        public void Update(bool held, double dt, Camera camera, Func<IEnumerable<ScanContact>> contacts)
        {
            _charge = ScanModel.Charge(_charge, held, dt);
            bool live = held || _charge > 0.004;
            Ui.Show(Root, live);
            if (!live)
            {
                _charge = 0;
                Tagged = Array.Empty<(ScanContact, double)>();
                return;
            }
            Root.style.opacity = (float)Math.Min(1, _charge * 1.4);
            _meterFill.style.width = Length.Percent((float)(_charge * 100));
            _sweepT = (_sweepT + dt * 0.45) % 1;
            float w = Root.resolvedStyle.width, h = Root.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0 || camera == null) return;
            _sweep.style.top = (float)(_sweepT * h);

            Vector3 eye = camera.transform.position;
            bool InView(ScanContact c)
            {
                Vector3 v = camera.WorldToViewportPoint(new Vector3((float)c.X, (float)c.Y, (float)c.Z));
                if (v.z <= 0) return false;
                float sx = v.x * w, sy = (1 - v.y) * h;
                return sx > -180 && sx < w + 180 && sy > -140 && sy < h + 140;
            }
            Tagged = ScanModel.Pick(contacts(), eye.x, eye.y, eye.z, InView);
            _state.text = ScanModel.State(held, _charge, Tagged.Count);
            _readout.text = ScanModel.Readout(Tagged);

            float fovK = h / (2 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2));
            for (int i = 0; i < _tags.Count; i++)
            {
                Tag t = _tags[i];
                bool show = i < Tagged.Count && _charge > ScanModel.TagsFrom;
                Ui.Show(t.Root, show);
                if (!show) continue;
                (ScanContact c, double d) = Tagged[i];
                Vector3 v = camera.WorldToViewportPoint(new Vector3((float)c.X, (float)c.Y, (float)c.Z));
                float sx = v.x * w, sy = (1 - v.y) * h;
                float boxH = Mathf.Clamp((float)(c.Height * fovK / Math.Max(0.6, d)), 26, 460), boxW = boxH * (c.Kind == ScanKind.Vehicle ? 1.6f : c.Kind == ScanKind.Place ? 0.9f : 0.52f);
                t.Root.style.left = sx;
                t.Root.style.top = sy;
                t.Box.style.width = boxW;
                t.Box.style.height = boxH;
                t.Box.style.left = -boxW / 2;
                t.Box.style.top = -boxH / 2;
                bool flip = sx > w * 0.62;
                t.Card.style.left = flip ? StyleKeyword.Auto : new StyleLength(boxW / 2 + 12);
                t.Card.style.right = flip ? new StyleLength(boxW / 2 + 12) : StyleKeyword.Auto;
                t.Card.style.top = -boxH / 2;
                t.Root.EnableInClassList("cy-tag--place", c.Kind == ScanKind.Place);
                t.Root.EnableInClassList("cy-tag--vehicle", c.Kind == ScanKind.Vehicle);
                t.Name.text = c.Name;
                t.Sub.text = c.Sub;
                t.Action.text = c.Action;
                Ui.Show(t.Action, c.Action.Length > 0);
                t.Distance.text = HudModel.Distance(d);
            }
        }
    }
}
