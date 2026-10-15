using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;

namespace RTLC.BO.RussianTranslate
{
    [HarmonyPatch(typeof(MainMenuBlackFade), "Awake")]
    internal static class RussianSkipIntroPatch
    {
        private static void Prefix(out bool __state)
        {
            __state = BoRussianPlugin.Instance != null &&
                BoRussianPlugin.Instance.SkipIntro && !BoConstants.ShowedLogoSplashScreen;
            if (__state)
                BoConstants.ShowedLogoSplashScreen = true;
        }

        private static void Postfix(bool __state, CanvasGroup ___boSplash,
            CanvasGroup ___videoRenderer, GameObject ___skipButton,
            GameObject ___mainMenuButtons, CustomMouseCursor ___customCursor,
            VideoPlayer ___videoPlayer, VideoClip ___introClip)
        {
            if (!__state) return;
            // Use the native return-to-menu path. The normal splash sequence
            // also prepares this clip for BeginNewGame; preserve that handoff.
            ___boSplash.gameObject.SetActive(false);
            ___videoRenderer.alpha = 0f;
            ___skipButton.SetActive(false);
            ___mainMenuButtons.SetActive(true);
            ___customCursor.MainMenuHideCursorOverride = false;
            ___videoPlayer.clip = ___introClip;
            BoRussianPlugin.ModLog.LogInfo("Skip intro: главное меню без стартовых заставок.");
        }
    }
}
