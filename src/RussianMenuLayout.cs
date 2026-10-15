using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RTLC.BO.RussianTranslate
{
    internal static class RussianMenuLayout
    {
        private static readonly string[] TabNames =
        {
            "Map Button", "Inventory Button", "QuestLog Button",
            "Daruma  Button", "Omamori  Button", "Options  Button"
        };
        private static readonly string[] MapControlNames = { "Navigate", "Zoom In/Out", "CenterOnPlayer", "Legend" };

        private sealed class OriginalText
        {
            public TMP_Text Text;
            public bool AutoSize;
            public float Size;
            public float Maximum;
            public Vector2 RectSize;
        }

        private sealed class TabGroup
        {
            public OriginalText[] Labels;
            public string Signature;
            public float Size;
            public Vector3 Scale;
            public Vector3 Position;
            public bool ExpandedTabs;
        }

        private static readonly Dictionary<Transform, TabGroup> Changed = new Dictionary<Transform, TabGroup>();
        private static readonly HashSet<ButtonsEvents> MainButtons = new HashSet<ButtonsEvents>();

        internal static void Refresh()
        {
            foreach (Transform bar in Changed.Keys.Where(t => t == null).ToArray())
                Changed.Remove(bar);
            if (!RussianFonts.IsRussian)
            {
                foreach (TabGroup group in Changed.Values)
                    foreach (OriginalText original in group.Labels)
                        if (original.Text != null)
                        {
                            original.Text.enableAutoSizing = original.AutoSize;
                            original.Text.fontSize = original.Size;
                            if (group.ExpandedTabs) original.Text.rectTransform.sizeDelta = original.RectSize;
                        }
                foreach (var pair in Changed)
                {
                    pair.Key.localScale = pair.Value.Scale;
                    pair.Key.localPosition = pair.Value.Position;
                }
                Changed.Clear();
                foreach (ButtonsEvents button in MainButtons.Where(b => b != null))
                {
                    if (UnityEngine.EventSystems.EventSystem.current != null
                        && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject == button.gameObject) button.ScaleUp();
                    else button.ResetScale();
                }
                MainButtons.Clear();
                return;
            }

            var menuObjectField = AccessTools.Field(typeof(SelectMenu), "selectMenuObj");
            foreach (Transform root in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (!root.gameObject.scene.IsValid()) continue;
                if (root.name == "Rebind Actions")
                {
                    TMP_Text[] controls = new[] { "Horizontal Rebinding Layout/Controller Menu Button", "Horizontal Rebinding Layout/Keyboard Menu Button", "ResetAll", "Rebind Text" }
                        .Select(path => root.Find(path)).Select(t => t == null ? null : t.GetComponentInChildren<TMP_Text>(true)).ToArray();
                    FitGroup(root, controls);
                }
                if (root.name != "Main Menu Buttons") continue;
                TMP_Text[] mainLabels = new[] { "Play", "Options", "Exit" }.Select(name =>
                {
                    Transform button = root.Find(name);
                    if (button == null) return null;
                    ButtonsEvents events = button.GetComponent<ButtonsEvents>();
                    if (events != null)
                    {
                        MainButtons.Add(events);
                        Vector3 scale = (Vector3)AccessTools.Field(typeof(ButtonsEvents), "originalLocalScale").GetValue(events);
                        if (scale != Vector3.zero && button.localScale != scale) button.localScale = scale;
                    }
                    return button.GetComponentInChildren<TMP_Text>(true);
                }).ToArray();
                FitGroup(root, mainLabels);
            }
            if (menuObjectField == null) return;
            foreach (SelectMenu menu in Resources.FindObjectsOfTypeAll<SelectMenu>())
            {
                if (!menu.gameObject.scene.IsValid()) continue;
                GameObject menuObject = menuObjectField.GetValue(menu) as GameObject;
                Transform bar = menuObject == null ? null : menuObject.transform.Find("Top Bar");
                if (bar == null) continue;
                TMP_Text[] labels = TabNames.Select(name =>
                {
                    Transform button = bar.Find(name);
                    return button == null ? null : button.GetComponentInChildren<TMP_Text>(true);
                }).ToArray();
                FitGroup(bar, labels, true, true);
                Transform controls = menuObject.transform.Find("Map Page/Extra Controls Map menu");
                if (controls != null)
                {
                    FitGroup(controls, MapControlNames.Select(name =>
                    {
                        Transform control = controls.Cast<Transform>().FirstOrDefault(child => child.name == name);
                        return control == null ? null : control.GetComponent<TMP_Text>();
                    }).ToArray(), false);
                    Transform menuControls = menuObject.transform.Find("Extra Controls Select Menu");
                    if (controls.gameObject.activeInHierarchy && menuControls != null)
                        FitMapControlArea(controls, menuControls);
                }
            }
        }

        private static void FitGroup(Transform bar, TMP_Text[] labels, bool fitBounds = true, bool expandTabs = false)
        {
                if (labels.Any(t => t == null || t.font == null || t.rectTransform.rect.width <= 0f
                    || t.rectTransform.rect.height <= 0f || string.IsNullOrEmpty(t.text))) return;

                TabGroup group;
                if (!Changed.TryGetValue(bar, out group))
                {
                    group = new TabGroup
                    {
                        Scale = bar.localScale, Position = bar.localPosition, ExpandedTabs = expandTabs,
                        Labels = labels.Select(t => new OriginalText
                        {
                            Text = t, AutoSize = t.enableAutoSizing, Size = t.fontSize,
                            Maximum = t.enableAutoSizing ? t.fontSizeMax : t.fontSize,
                            RectSize = t.rectTransform.sizeDelta
                        }).ToArray()
                    };
                    Changed[bar] = group;
                }

                if (expandTabs)
                {
                    GridLayoutGroup grid = bar.GetComponent<GridLayoutGroup>();
                    if (grid != null)
                    {
                        // Native boxes inset the labels by 100 units, even though
                        // the tab cells have spare space. Use it while reserving
                        // 26 units between adjacent label boxes (13 px at 1080p).
                        float step = grid.cellSize.x + grid.spacing.x;
                        foreach (TMP_Text label in labels)
                        {
                            RectTransform button = label.transform.parent as RectTransform;
                            if (button == null) continue;
                            float width = Mathf.Min(button.rect.width, step) - 26f;
                            if (width > 0f) label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                        }
                    }
                }

                var signature = new StringBuilder();
                foreach (TMP_Text label in labels)
                    signature.Append(label.text).Append('|').Append(label.font.GetInstanceID())
                        .Append('|').Append(fitBounds ? label.rectTransform.rect.size : Vector2.zero).Append('|').Append(label.margin)
                        .Append('|').Append(label.fontStyle).Append('|').Append(label.characterSpacing).Append(';');
                string current = signature.ToString();
                if (group.Signature != current)
                {
                    // Tabs have fixed boxes. Map hints size their own boxes to the
                    // text, so retain their point size and fit the entire group below.
                    float low = 1f;
                    float high = group.Labels.Min(t => t.Maximum);
                    if (!fitBounds || Fits(labels, high)) low = high;
                    else
                        while (high - low > 0.05f)
                        {
                            float middle = (low + high) * 0.5f;
                            if (Fits(labels, middle)) low = middle;
                            else high = middle;
                        }
                    group.Size = Mathf.Floor(low * 20f) / 20f;
                    group.Signature = current;
                    BoRussianPlugin.ModLog.LogInfo($"Русские подписи {bar.name}: общий размер {group.Size:F2}, элементов {labels.Length}.");
                }
                foreach (TMP_Text label in labels)
                {
                    label.enableAutoSizing = false;
                    if (!Mathf.Approximately(label.fontSize, group.Size)) label.fontSize = group.Size;
                }
        }

        private static void FitMapControlArea(Transform controls, Transform menuControls)
        {
            TabGroup group;
            if (!Changed.TryGetValue(controls, out group)) return;
            controls.localScale = group.Scale;
            controls.localPosition = group.Position;
            TMP_Text[] right = menuControls.GetComponentsInChildren<TMP_Text>(false);
            if (right.Length == 0) return;
            float boundary = float.PositiveInfinity;
            foreach (TMP_Text text in right)
            {
                text.ForceMeshUpdate();
                boundary = Math.Min(boundary, text.transform.TransformPoint(text.textBounds.min).x);
            }
            float left = float.PositiveInfinity, end = float.NegativeInfinity;
            foreach (OriginalText label in group.Labels)
            {
                label.Text.ForceMeshUpdate();
                left = Math.Min(left, label.Text.transform.TransformPoint(label.Text.textBounds.min).x);
                end = Math.Max(end, label.Text.transform.TransformPoint(label.Text.textBounds.max).x);
            }
            if (end <= boundary - 12f || end <= left) return;
            float scale = (boundary - 12f - left) / (end - left);
            if (scale <= 0f) return;
            controls.localScale = group.Scale * scale;
            TMP_Text first = group.Labels.OrderBy(t => t.Text.transform.TransformPoint(t.Text.textBounds.min).x).First().Text;
            float shiftedLeft = first.transform.TransformPoint(first.textBounds.min).x;
            controls.position += new Vector3(left - shiftedLeft, 0f, 0f);
        }

        private static bool Fits(IEnumerable<TMP_Text> labels, float size)
        {
            bool fits = true;
            foreach (TMP_Text label in labels)
            {
                label.enableAutoSizing = false;
                label.fontSize = size;
                // These labels use Overflow mode, where isTextOverflowing remains
                // false even when text extends beyond its rectangle.
                Vector2 preferred = label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity);
                Rect rect = label.rectTransform.rect;
                if (preferred.x > rect.width || preferred.y > rect.height) fits = false;
            }
            return fits;
        }
    }

    [HarmonyPatch]
    internal static class RussianMainMenuScalePatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ButtonsEvents), "ScaleUp");
            yield return AccessTools.Method(typeof(ButtonsEvents), "ResetScale");
        }
        private static bool Prefix(ButtonsEvents __instance)
        {
            if (!RussianFonts.IsRussian || __instance.transform.parent == null || __instance.transform.parent.name != "Main Menu Buttons"
                || !new[] { "Play", "Options", "Exit" }.Contains(__instance.name)) return true;
            Vector3 original = (Vector3)AccessTools.Field(typeof(ButtonsEvents), "originalLocalScale").GetValue(__instance);
            // Fixed-size Russian buttons need no scale tween that can outlive the menu.
            if (original != Vector3.zero) __instance.transform.localScale = original;
            return false;
        }
    }
}
