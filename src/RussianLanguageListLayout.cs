using HarmonyLib;
using I2.Loc;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RTLC.BO.RussianTranslate
{
    // The native template has room for ten rows. The added Russian option
    // requires eleven, regardless of the language currently selected.
    [HarmonyPatch(typeof(TMP_Dropdown), nameof(TMP_Dropdown.Show))]
    internal static class RussianLanguageListLayout
    {
        private static void Prefix(TMP_Dropdown __instance)
        {
            if (__instance.GetComponent<SetLanguageDropdown>() == null || __instance.template == null
                || __instance.itemText == null || __instance.options.Count == 0 || __instance.IsExpanded) return;
            Toggle item = __instance.itemText.GetComponentInParent<Toggle>(true);
            ScrollRect scroll = __instance.template.GetComponent<ScrollRect>();
            if (item == null || scroll == null || scroll.viewport == null || scroll.content == null) return;
            float rowHeight = ((RectTransform)item.transform).rect.height;
            float border = __instance.template.rect.height - scroll.viewport.rect.height;
            float padding = Mathf.Max(0f, scroll.content.rect.height - rowHeight);
            float required = rowHeight * __instance.options.Count + border + padding;
            if (rowHeight > 0f && __instance.template.rect.height < required)
                __instance.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, required);
        }
    }
}
