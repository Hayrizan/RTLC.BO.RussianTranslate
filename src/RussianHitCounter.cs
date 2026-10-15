using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using I2.Loc;
using TMPro;
using UnityEngine;

namespace RTLC.BO.RussianTranslate
{
    internal static class RussianHitCounter
    {
        private sealed class Layout
        {
            public TMP_Text Label;
            public Vector2 Size, Position, BackdropSize, BackdropPosition;
            public RectTransform Backdrop;
            public float FontSize;
            public bool AutoSize, Wrap;
        }

        private static readonly Dictionary<ComboUIPopup, Layout> Changed = new Dictionary<ComboUIPopup, Layout>();
        private static readonly AccessTools.FieldRef<ComboUIPopup, TMP_Text> Label = AccessTools.FieldRefAccess<ComboUIPopup, TMP_Text>("hitsText");
        private static readonly AccessTools.FieldRef<ComboUIPopup, TMP_Text> Number = AccessTools.FieldRefAccess<ComboUIPopup, TMP_Text>("hitNumText");

        internal static string Word(int count)
        {
            int hundred = Math.Abs(count % 100), ten = hundred % 10;
            if (hundred >= 11 && hundred <= 14) return "Попаданий";
            return ten == 1 ? "Попадание" : ten >= 2 && ten <= 4 ? "Попадания" : "Попаданий";
        }

        internal static void Apply(ComboUIPopup popup)
        {
            if (popup == null) return;
            SyncNumberFont(popup);
            if (!RussianFonts.IsRussian) return;
            TMP_Text label = Label(popup), number = Number(popup);
            if (label == null || number == null) return;
            Layout old;
            if (!Changed.TryGetValue(popup, out old))
            {
                RectTransform backdrop = label.transform.parent as RectTransform;
                old = new Layout { Label = label, Size = label.rectTransform.sizeDelta, Position = label.rectTransform.anchoredPosition,
                    Backdrop = backdrop, BackdropSize = backdrop == null ? Vector2.zero : backdrop.sizeDelta,
                    BackdropPosition = backdrop == null ? Vector2.zero : backdrop.anchoredPosition,
                    FontSize = label.fontSize, AutoSize = label.enableAutoSizing, Wrap = label.enableWordWrapping };
                Changed[popup] = old;
            }
            label.enableAutoSizing = false;
            label.fontSize = 55f;
            label.enableWordWrapping = false;
            int count;
            if (int.TryParse(number.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
                label.text = Word(count);
            float width = new[] { "Попадание", "Попадания", "Попаданий" }
                .Max(s => label.GetPreferredValues(s, Mathf.Infinity, Mathf.Infinity).x) + label.margin.x + label.margin.z + 12f;
            RectTransform rect = label.rectTransform;
            float extra = Mathf.Max(0f, width - old.Size.x);
            rect.sizeDelta = new Vector2(old.Size.x + extra, Mathf.Max(old.Size.y,
                label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity).y + label.margin.y + label.margin.w + 8f));
            rect.anchoredPosition = old.Position + new Vector2(extra * rect.pivot.x, 0f);
            if (old.Backdrop != null)
            {
                old.Backdrop.sizeDelta = old.BackdropSize + new Vector2(extra, 0f);
                old.Backdrop.anchoredPosition = old.BackdropPosition + new Vector2(extra * old.Backdrop.pivot.x, 0f);
            }
        }

        private static void SyncNumberFont(ComboUIPopup popup)
        {
            TMP_Text number = Number(popup);
            if (number == null) return;
            // This field has no text term. Resolve its title role explicitly so a
            // Japanese or French numeric face cannot survive another locale switch.
            FontManagerSO fm = GameManager.Instance == null ? null : GameManager.Instance.GetFontManager();
            string path = RussianFonts.IsRussian || fm == null ? "Fonts/Feruka_BlackOutline_WhiteTextColor"
                : LocalizationManager.GetTranslation(fm.TitleBlackOutline);
            TMP_FontAsset font = string.IsNullOrEmpty(path) ? null : Resources.Load<TMP_FontAsset>(path);
            if (font != null) RussianFonts.AssignFont(number, RussianFonts.Resolve(font));
        }

        internal static void Refresh()
        {
            foreach (ComboUIPopup popup in Changed.Keys.Where(p => p == null).ToArray()) Changed.Remove(popup);
            if (!RussianFonts.IsRussian)
            {
                foreach (var pair in Changed)
                {
                    Layout old = pair.Value;
                    old.Label.rectTransform.sizeDelta = old.Size;
                    old.Label.rectTransform.anchoredPosition = old.Position;
                    old.Label.enableAutoSizing = old.AutoSize;
                    old.Label.enableWordWrapping = old.Wrap;
                    old.Label.fontSize = old.FontSize;
                    if (old.Backdrop != null) { old.Backdrop.sizeDelta = old.BackdropSize; old.Backdrop.anchoredPosition = old.BackdropPosition; }
                    pair.Key.ChangeFontSizePerLanguage();
                }
                Changed.Clear();
                foreach (ComboUIPopup popup in Resources.FindObjectsOfTypeAll<ComboUIPopup>())
                    if (popup.gameObject.scene.IsValid()) SyncNumberFont(popup);
                return;
            }
            foreach (ComboUIPopup popup in Resources.FindObjectsOfTypeAll<ComboUIPopup>())
                if (popup.gameObject.scene.IsValid()) Apply(popup);
        }
    }

    [HarmonyPatch(typeof(ComboUIPopup), nameof(ComboUIPopup.AddToComboUI))]
    internal static class RussianHitCounterValuePatch
    {
        private static void Postfix(ComboUIPopup __instance) => RussianHitCounter.Apply(__instance);
    }

    [HarmonyPatch(typeof(ComboUIPopup), nameof(ComboUIPopup.ChangeFontSizePerLanguage))]
    internal static class RussianHitCounterSizePatch
    {
        private static void Postfix(ComboUIPopup __instance) => RussianHitCounter.Apply(__instance);
    }
}
