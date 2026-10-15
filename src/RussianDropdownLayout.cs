using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RTLC.BO.RussianTranslate
{
    internal static class RussianDropdownLayout
    {
        private sealed class OriginalLayout
        {
            public Vector2 DropdownSize, DropdownPosition, CaptionSize, TemplateSize;
            public float DropdownWidth, CaptionWidth, CaptionHeight, TemplateWidth, FontSize, Maximum, ItemSize, ItemMaximum;
            public bool AutoSize, ItemAutoSize;
            public RectTransform Label;
            public float LabelX;
            public Vector2 LabelSize;
            public bool LabelAutoSize;
            public float LabelFontSize, LabelMaximum;
            public Transform Page;
            public string Signature;
        }

        private static readonly Dictionary<TMP_Dropdown, OriginalLayout> Changed = new Dictionary<TMP_Dropdown, OriginalLayout>();
        private static readonly Dictionary<Transform, float> Columns = new Dictionary<Transform, float>();

        internal static void Refresh()
        {
            foreach (TMP_Dropdown dropdown in Changed.Keys.Where(d => d == null).ToArray()) Changed.Remove(dropdown);
            foreach (Transform page in Columns.Keys.Where(p => p == null).ToArray()) Columns.Remove(page);
            if (!RussianFonts.IsRussian)
            {
                foreach (var pair in Changed)
                {
                    TMP_Dropdown d = pair.Key; OriginalLayout old = pair.Value;
                    RectTransform rect = (RectTransform)d.transform;
                    rect.sizeDelta = old.DropdownSize; rect.anchoredPosition = old.DropdownPosition;
                    if (d.captionText != null)
                    {
                        d.captionText.rectTransform.sizeDelta = old.CaptionSize;
                        d.captionText.enableAutoSizing = old.AutoSize; d.captionText.fontSize = old.FontSize;
                    }
                    if (d.template != null) d.template.sizeDelta = old.TemplateSize;
                    if (d.itemText != null) { d.itemText.enableAutoSizing = old.ItemAutoSize; d.itemText.fontSize = old.ItemSize; }
                    RestoreLabel(old);
                    if (old.Label != null)
                    {
                        old.Label.sizeDelta = old.LabelSize;
                        TMP_Text label = old.Label.GetComponent<TMP_Text>();
                        if (label != null) { label.enableAutoSizing = old.LabelAutoSize; label.fontSize = old.LabelFontSize; }
                    }
                }
                Changed.Clear(); Columns.Clear();
                return;
            }

            foreach (TMP_Dropdown d in Resources.FindObjectsOfTypeAll<TMP_Dropdown>())
            {
                // Hidden pages still contain serialized options in other languages.
                // Measure after activation/localization, using their actual visible text.
                if (!d.gameObject.scene.IsValid() || !d.gameObject.activeInHierarchy || d.captionText == null || d.options.Count == 0) continue;
                Transform page = d.transform.parent;
                while (page != null && !page.name.EndsWith(" Page")) page = page.parent;
                if (page == null || page.GetComponent<VerticalLayoutGroup>() == null) continue;

                OriginalLayout old;
                if (!Changed.TryGetValue(d, out old))
                {
                    RectTransform rect = (RectTransform)d.transform;
                    TMP_Text label = d.transform.parent.GetComponentsInChildren<TMP_Text>(true)
                        .FirstOrDefault(t => !t.transform.IsChildOf(d.transform));
                    old = new OriginalLayout
                    {
                        DropdownSize = rect.sizeDelta, DropdownPosition = rect.anchoredPosition, DropdownWidth = rect.rect.width,
                        CaptionSize = d.captionText.rectTransform.sizeDelta, CaptionWidth = d.captionText.rectTransform.rect.width,
                        CaptionHeight = d.captionText.rectTransform.rect.height,
                        AutoSize = d.captionText.enableAutoSizing, FontSize = d.captionText.fontSize,
                        Maximum = d.captionText.enableAutoSizing ? d.captionText.fontSizeMax : d.captionText.fontSize,
                        TemplateSize = d.template == null ? Vector2.zero : d.template.sizeDelta,
                        TemplateWidth = d.template == null ? 0f : d.template.rect.width,
                        ItemAutoSize = d.itemText != null && d.itemText.enableAutoSizing,
                        ItemSize = d.itemText == null ? 0f : d.itemText.fontSize,
                        ItemMaximum = d.itemText == null ? 0f : d.itemText.enableAutoSizing ? d.itemText.fontSizeMax : d.itemText.fontSize,
                        Label = label == null ? null : label.rectTransform,
                        LabelX = label == null ? 0f : label.rectTransform.anchoredPosition.x, Page = page,
                        LabelSize = label == null ? Vector2.zero : label.rectTransform.sizeDelta,
                        LabelAutoSize = label != null && label.enableAutoSizing,
                        LabelFontSize = label == null ? 0f : label.fontSize,
                        LabelMaximum = label == null ? 0f : 75f
                    };
                    Changed[d] = old;
                }

                bool toggle = d.options.Count == 2 && d.options.Any(o => o.text == "Вкл.") && d.options.Any(o => o.text == "Выкл.");
                bool speed = d.name == "Game Speed Dropdown" && d.options.Any(o => o.text == "Самая медленная");
                bool fps = d.transform.parent.name == "FPS Cap" && d.options.Any(o => o.text == "Без ограничений");
                if (d.GetComponent<I2.Loc.SetLanguageDropdown>() != null)
                {
                    d.captionText.enableAutoSizing = false;
                    d.captionText.fontSize = old.Maximum;
                }
                if (toggle || speed || fps || d.GetComponent<I2.Loc.SetLanguageDropdown>() == null)
                {
                    string signature = d.captionText.font.GetInstanceID() + "|" + string.Join("|", d.options.Select(o => o.text))
                        + "|" + (d.itemText == null || d.itemText.font == null ? 0 : d.itemText.font.GetInstanceID());
                    if (old.Signature != signature)
                    {
                        TMP_Text caption = d.captionText;
                        caption.enableAutoSizing = false; caption.fontSize = old.Maximum;
                        Vector2[] measurements = d.options.Select(o => caption.GetPreferredValues(o.text, Mathf.Infinity, Mathf.Infinity)).ToArray();
                        float textWidth = measurements.Max(size => size.x)
                            + caption.margin.x + caption.margin.z + 12f;
                        float extra = Mathf.Max(0f, textWidth - old.CaptionWidth);
                        RectTransform rect = (RectTransform)d.transform;
                        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, old.DropdownWidth + extra);
                        caption.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, old.CaptionWidth + extra);
                        if (caption.font.name.StartsWith("BoRU_"))
                            caption.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                                Mathf.Max(old.CaptionHeight, measurements.Max(size => size.y) + caption.margin.y + caption.margin.w + 4f));
                        if (d.itemText != null)
                        {
                            d.itemText.enableAutoSizing = false; d.itemText.fontSize = old.ItemMaximum;
                            float itemWidth = d.options.Max(o => d.itemText.GetPreferredValues(o.text, Mathf.Infinity, Mathf.Infinity).x)
                                + d.itemText.margin.x + d.itemText.margin.z + 140f;
                            if (d.template != null) d.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                                Mathf.Max(old.TemplateWidth, old.DropdownWidth + extra, itemWidth));
                        }
                        old.Signature = signature;
                        BoRussianPlugin.ModLog.LogInfo($"Русское поле {d.transform.parent.name}/{d.name}: ширина {rect.rect.width:F1}, текст {old.Maximum:F0}, пункты {old.ItemMaximum:F0}.");
                    }
                }
            }

            // Use the first native field as the common value column on each page.
            foreach (var pair in Changed)
                if (!Columns.ContainsKey(pair.Value.Page))
                {
                    TMP_Dropdown reference = pair.Value.Page.GetComponentsInChildren<TMP_Dropdown>(true).FirstOrDefault(d => Changed.ContainsKey(d));
                    if (reference == null) continue;
                    var corners = new Vector3[4]; ((RectTransform)reference.transform).GetWorldCorners(corners);
                    Columns[pair.Value.Page] = pair.Value.Page.InverseTransformPoint(corners[0]).x;
                }
            // Measure every title before moving any field. Native row layout
            // invokes our postfix one row at a time; growing the shared column
            // there moved earlier rows only on the next 250 ms refresh.
            foreach (var pair in Changed)
            {
                OriginalLayout old = pair.Value;
                if (old.Page == null || !pair.Key.gameObject.activeInHierarchy) continue;
                float column;
                if (!Columns.TryGetValue(old.Page, out column)) continue;
                Columns[old.Page] = Mathf.Max(column, PrepareLabel(old));
            }
            foreach (var pair in Changed) Align(pair.Key, pair.Value);
        }

        private static void RestoreLabel(OriginalLayout old)
        {
            if (old.Label == null) return;
            Vector2 position = old.Label.anchoredPosition; position.x = old.LabelX; old.Label.anchoredPosition = position;
        }

        private static float PrepareLabel(OriginalLayout old)
        {
            RestoreLabel(old);
            if (old.Label != null)
            {
                TMP_Text label = old.Label.GetComponent<TMP_Text>();
                if (label != null && label.font != null)
                {
                    label.enableAutoSizing = false; label.fontSize = old.LabelMaximum;
                    float width = label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity).x + 12f;
                    if (!Mathf.Approximately(old.Label.rect.width, width))
                        old.Label.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                    var labelCorners = new Vector3[4]; old.Label.GetWorldCorners(labelCorners);
                    return old.Page.InverseTransformPoint(labelCorners[3]).x + 24f;
                }
            }
            return float.NegativeInfinity;
        }

        private static void Align(TMP_Dropdown dropdown, OriginalLayout old)
        {
            float column;
            if (old.Page == null || !Columns.TryGetValue(old.Page, out column)) return;
            RestoreLabel(old);
            var rect = (RectTransform)dropdown.transform;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            float current = old.Page.InverseTransformPoint(corners[0]).x;
            rect.position += old.Page.TransformVector(new Vector3(column - current, 0f, 0f));
            RestoreLabel(old);
        }

        internal static void AfterRowLayout(HorizontalLayoutGroup group)
        {
            if (!RussianFonts.IsRussian) return;
            foreach (var pair in Changed)
                if (pair.Key != null && pair.Key.transform.parent == group.transform) Align(pair.Key, pair.Value);
        }
    }

    [HarmonyPatch(typeof(HorizontalLayoutGroup), nameof(HorizontalLayoutGroup.SetLayoutHorizontal))]
    internal static class RussianSettingsColumnPatch
    {
        private static void Postfix(HorizontalLayoutGroup __instance) => RussianDropdownLayout.AfterRowLayout(__instance);
    }
}
