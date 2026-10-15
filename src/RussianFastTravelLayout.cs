using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RTLC.BO.RussianTranslate
{
    internal static class RussianFastTravelLayout
    {
        private sealed class LabelState
        {
            internal TMP_Text Text;
            internal bool AutoSize;
            internal float Size, Width;
            internal ContentSizeFitter Fitter;
            internal ContentSizeFitter.FitMode HorizontalFit;
        }
        private sealed class GroupState
        {
            internal float Width, Spacing;
            internal bool ControlWidth, ExpandWidth;
            internal LabelState[] Labels;
        }
        private static readonly Dictionary<HorizontalLayoutGroup, GroupState> Changed = new Dictionary<HorizontalLayoutGroup, GroupState>();

        internal static void Refresh()
        {
            foreach (var group in Changed.Keys.Where(g => g == null).ToArray()) Changed.Remove(group);
            if (!RussianFonts.IsRussian)
            {
                foreach (var pair in Changed)
                {
                    HorizontalLayoutGroup group = pair.Key;
                    GroupState saved = pair.Value;
                    group.spacing = saved.Spacing;
                    group.childControlWidth = saved.ControlWidth;
                    group.childForceExpandWidth = saved.ExpandWidth;
                    ((RectTransform)group.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, saved.Width);
                    foreach (LabelState label in saved.Labels)
                    {
                        if (label.Text == null) continue;
                        label.Text.enableAutoSizing = label.AutoSize;
                        label.Text.fontSize = label.Size;
                        label.Text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, label.Width);
                        if (label.Fitter != null) label.Fitter.horizontalFit = label.HorizontalFit;
                    }
                    LayoutRebuilder.MarkLayoutForRebuild((RectTransform)group.transform);
                }
                Changed.Clear();
                return;
            }
            foreach (HorizontalLayoutGroup group in Resources.FindObjectsOfTypeAll<HorizontalLayoutGroup>())
            {
                if (!group.gameObject.scene.IsValid() || group.name != "Extra Controls Fast Trabel"
                    || group.GetComponentInParent<FastTravel>(true) == null) continue;
                TMP_Text[] labels = new[] { "Accept", "Back" }.Select(n => group.transform.Find(n))
                    .Select(t => t == null ? null : t.GetComponent<TMP_Text>()).ToArray();
                if (labels.Any(t => t == null)) continue;
                GroupState saved;
                if (!Changed.TryGetValue(group, out saved))
                {
                    saved = new GroupState { Width = ((RectTransform)group.transform).rect.width, Spacing = group.spacing,
                        ControlWidth = group.childControlWidth, ExpandWidth = group.childForceExpandWidth,
                        Labels = labels.Select(t => { var fitter = t.GetComponent<ContentSizeFitter>(); return new LabelState {
                            Text = t, AutoSize = t.enableAutoSizing, Size = t.fontSize, Width = t.rectTransform.rect.width,
                            Fitter = fitter, HorizontalFit = fitter == null ? ContentSizeFitter.FitMode.Unconstrained : fitter.horizontalFit }; }).ToArray() };
                    Changed.Add(group, saved);
                }
                // Native children have ContentSizeFitters AND a parent controlling
                // their width. Give width ownership to the group while in Russian.
                group.childControlWidth = true;
                group.childForceExpandWidth = false;
                group.spacing = Mathf.Max(saved.Spacing, 50f);
                float width = group.padding.horizontal + group.spacing;
                foreach (LabelState label in saved.Labels)
                {
                    if (label.Fitter != null) label.Fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                    label.Text.enableAutoSizing = false;
                    label.Text.fontSize = label.Text.fontSizeMax;
                    width += Mathf.Ceil(label.Text.GetPreferredValues(label.Text.text, Mathf.Infinity, Mathf.Infinity).x) + 2f;
                }
                RectTransform rect = (RectTransform)group.transform;
                // The native pivot and anchor are at the right edge, so widening
                // moves only the left edge; the Back caption stays on screen.
                float wanted = Mathf.Max(saved.Width, width);
                if (!Mathf.Approximately(rect.rect.width, wanted))
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, wanted);
                LayoutRebuilder.MarkLayoutForRebuild(rect);
            }
        }
    }
}
