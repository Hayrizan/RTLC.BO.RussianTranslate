using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RTLC.BO.RussianTranslate
{
    internal static class RussianBindingLayout
    {
        private sealed class Original
        {
            public bool AutoSize;
            public float Size, PreferredWidth;
            public LayoutElement Layout;
        }

        private static readonly Dictionary<TMP_Text, Original> Changed = new Dictionary<TMP_Text, Original>();

        internal static void Refresh()
        {
            foreach (TMP_Text text in Changed.Keys.Where(t => t == null).ToArray()) Changed.Remove(text);
            if (!RussianFonts.IsRussian)
            {
                foreach (var pair in Changed)
                {
                    pair.Key.enableAutoSizing = pair.Value.AutoSize; pair.Key.fontSize = pair.Value.Size;
                    if (pair.Value.Layout != null) pair.Value.Layout.preferredWidth = pair.Value.PreferredWidth;
                }
                Changed.Clear();
                return;
            }
            var labels = Resources.FindObjectsOfTypeAll<TMP_Text>().Where(t => t.gameObject.scene.IsValid()
                && t.name == "Text_ActionLabel" && t.transform.parent != null && t.transform.parent.parent != null).ToArray();
            foreach (var group in labels.GroupBy(t => t.transform.parent.parent))
            {
                float width = 0f;
                foreach (TMP_Text text in group)
                {
                    Original old;
                    if (!Changed.TryGetValue(text, out old))
                    {
                        LayoutElement layout = text.GetComponent<LayoutElement>();
                        if (layout == null) continue;
                        Changed[text] = old = new Original { AutoSize = text.enableAutoSizing, Size = text.fontSize,
                            Layout = layout, PreferredWidth = layout.preferredWidth };
                    }
                    text.enableAutoSizing = false; text.fontSize = 60f;
                    width = Mathf.Max(width, old.PreferredWidth, text.GetPreferredValues(text.text, Mathf.Infinity, Mathf.Infinity).x + 16f);
                }
                foreach (TMP_Text text in group)
                {
                    Original old;
                    if (!Changed.TryGetValue(text, out old) || Mathf.Approximately(old.Layout.preferredWidth, width)) continue;
                    old.Layout.preferredWidth = width;
                    LayoutRebuilder.MarkLayoutForRebuild((RectTransform)text.transform.parent);
                }
            }
        }
    }
}
