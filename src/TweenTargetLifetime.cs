using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace RTLC.BO.RussianTranslate
{
    // Native menu/icon animations retain a Transform after its scene is unloaded.
    // Link the tween at creation; DOTween kills it before evaluating a dead target.
    [HarmonyPatch]
    internal static class TransformScaleLifetimePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ShortcutExtensions), "DOScale", new[] { typeof(Transform), typeof(Vector3), typeof(float) });
            yield return AccessTools.Method(typeof(ShortcutExtensions), "DOScale", new[] { typeof(Transform), typeof(float), typeof(float) });
        }

        private static void Postfix(Transform target, Tween __result)
        {
            if (target != null && target.gameObject.scene.IsValid() && __result != null)
                __result.SetLink(target.gameObject, LinkBehaviour.KillOnDestroy);
        }
    }

    [HarmonyPatch(typeof(DOTweenModuleUI), nameof(DOTweenModuleUI.DOFade), typeof(CanvasGroup), typeof(float), typeof(float))]
    internal static class CanvasFadeLifetimePatch
    {
        private static void Postfix(CanvasGroup target, Tween __result)
        {
            if (target != null && target.gameObject.scene.IsValid() && __result != null)
                __result.SetLink(target.gameObject, LinkBehaviour.KillOnDestroy);
        }
    }
}
