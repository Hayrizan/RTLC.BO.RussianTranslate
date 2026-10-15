using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RTLC.BO.RussianTranslate
{
    // Use a separate sprite: changing the original texture would also affect
    // other locales and any sprites sharing its atlas.
    internal sealed class RussianMainMenuBranding : MonoBehaviour
    {
        internal const string BoostyUrl = "https://boosty.to/rtlc";
        private static Sprite _translatedLogo, _teamIcon;
        private static bool _assetsAttempted;
        private UnityEngine.UI.Image _logo;
        private Sprite _originalLogo;
        private GameObject _mainButtons, _credits;
        private UnityEngine.UI.Button _play, _teamButton;
        private CanvasGroup _mainFade, _creditFade;
        private bool _initialized;
        private RectTransform _socialRow;
        private Vector2 _originalRowSize;
        private bool _rowExpanded;
        private readonly List<ButtonLayout> _socialButtons = new List<ButtonLayout>();

        private sealed class ButtonLayout
        {
            internal UnityEngine.UI.Button Button;
            internal UnityEngine.UI.Navigation Navigation;
            internal Vector2 Size;
            internal Vector3 Scale, EventScale;
            internal float HoverScale;
            internal bool Aspect, DirectedEnabled;
            internal Behaviour Directed;
            internal ButtonsEvents Events;
        }

        internal UnityEngine.UI.Button TeamButton => _teamButton;

        internal void Initialize(NewMainMenu menu)
        {
            if (_initialized) return;
            _initialized = true;
            _logo = AccessTools.Field(typeof(NewMainMenu), "logo").GetValue(menu) as UnityEngine.UI.Image;
            _mainButtons = AccessTools.Field(typeof(NewMainMenu), "mainButtonsContainer").GetValue(menu) as GameObject;
            _mainFade = AccessTools.Field(typeof(NewMainMenu), "mainButtonsCanvas").GetValue(menu) as CanvasGroup;
            _play = AccessTools.Field(typeof(NewMainMenu), "playButton").GetValue(menu) as UnityEngine.UI.Button;
            if (_logo == null || _mainButtons == null || _play == null) return;
            _originalLogo = _logo.sprite;
            LoadAssets();
            Transform template = _mainButtons.transform.Find("socialMediaPanel/Discord Button");
            if (_teamIcon != null && template != null) CreateCredits(template);
            Refresh();
        }

        private static void LoadAssets()
        {
            if (_assetsAttempted) return;
            _assetsAttempted = true;
            _translatedLogo = LoadSprite("logo");
            _teamIcon = LoadSprite("rtlc");
        }

        private static Sprite LoadSprite(string path)
        {
            Texture2D texture = null;
            try
            {
                texture = RussianAssets.Texture(path);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                sprite.name = texture.name;
                return sprite;
            }
            catch (Exception ex)
            {
                BoRussianPlugin.ModLog.LogWarning("Не удалось загрузить оформление главного меню: " + ex.Message);
                return null;
            }
        }

        private void CreateCredits(Transform template)
        {
            _credits = new GameObject("RTLC Credits", typeof(RectTransform), typeof(CanvasGroup));
            _credits.SetActive(false);
            RectTransform root = (RectTransform)_credits.transform;
            root.SetParent(transform, false);
            root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
            root.anchoredPosition = new Vector2(-68, -60);
            root.sizeDelta = new Vector2(1040, 220);
            _creditFade = _credits.GetComponent<CanvasGroup>();

            var caption = new GameObject("Translation Credit", typeof(RectTransform), typeof(TextMeshProUGUI));
            caption.transform.SetParent(root, false);
            TMP_Text label = caption.GetComponent<TMP_Text>();
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(1, 1);
            label.rectTransform.pivot = new Vector2(1, 1);
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.rectTransform.sizeDelta = new Vector2(1040, 220);
            label.font = RussianFonts.Resolve(Resources.Load<TMP_FontAsset>("Fonts/MortyOld_BlackOutline_WhiteTextColor"), true);
            label.fontSize = 50;
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.alignment = TextAlignmentOptions.TopRight;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = "Переведено на русский RTLC TEAM\nПеревод: Hayrizan\nТестер: DimaLooper";

            // Instantiate while inactive so native Awake caches our final scale.
            // Internal references to Image/ButtonsEvents are remapped by Unity.
            GameObject clone = Instantiate(template.gameObject, root, false);
            clone.name = "RTLC Boosty Button";
            RectTransform rect = (RectTransform)clone.transform;
            _socialRow = (RectTransform)template.parent;
            rect.SetParent(_socialRow, false);
            rect.sizeDelta = new Vector2(251, 251);
            rect.localScale = template.localScale;
            UnityEngine.UI.Image icon = clone.GetComponent<UnityEngine.UI.Image>();
            icon.sprite = _teamIcon;
            icon.overrideSprite = null;
            icon.preserveAspect = true;
            _teamButton = clone.GetComponent<UnityEngine.UI.Button>();
            _teamButton.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            ButtonsEvents sounds = clone.GetComponent<ButtonsEvents>();
            if (sounds != null) _teamButton.onClick.AddListener(sounds.PlaySubmitSFX);
            _teamButton.onClick.AddListener(OpenBoosty);
            EventTrigger trigger = clone.GetComponent<EventTrigger>();
            if (trigger != null) trigger.triggers.RemoveAll(entry =>
                entry.eventID == EventTriggerType.PointerClick || entry.eventID == EventTriggerType.Submit);
            clone.SetActive(false);
            BoRussianPlugin.ModLog.LogInfo("Добавлены русский логотип и кнопка RTLC TEAM: " + BoostyUrl);
        }

        private void SetSocialRow(bool expanded)
        {
            if (_socialRow == null || !_socialRow.gameObject.activeInHierarchy || expanded == _rowExpanded) return;
            if (_socialButtons.Count == 0)
            {
                _originalRowSize = _socialRow.sizeDelta;
                foreach (Transform child in _socialRow)
                {
                    var button = child.GetComponent<UnityEngine.UI.Button>();
                    if (button == null) continue;
                    var state = new ButtonLayout { Button = button, Navigation = button.navigation,
                        Size = ((RectTransform)child).sizeDelta, Scale = child.localScale, Aspect = button.image.preserveAspect,
                        Events = child.GetComponent<ButtonsEvents>() };
                    if (state.Events != null)
                    {
                        state.EventScale = (Vector3)AccessTools.Field(typeof(ButtonsEvents), "originalLocalScale").GetValue(state.Events);
                        state.HoverScale = (float)AccessTools.Field(typeof(ButtonsEvents), "scaleUpSize").GetValue(state.Events);
                    }
                    foreach (Behaviour component in child.GetComponents<Behaviour>())
                        if (component.GetType().FullName == "IEVO.UI.uGUIDirectedNavigation.DirectedNavigation")
                        { state.Directed = component; state.DirectedEnabled = component.enabled; }
                    _socialButtons.Add(state);
                }
            }
            _rowExpanded = expanded;
            // Native spacing is 55, sizes are ~251; fit exactly five equal cells.
            _socialRow.sizeDelta = expanded ? new Vector2(5 * 251 + 4 * 55, _originalRowSize.y) : _originalRowSize;
            Vector3 commonScale = _socialButtons[0].EventScale;
            if (commonScale == Vector3.zero) commonScale = _socialButtons[0].Scale;
            for (int i = 0; i < _socialButtons.Count; ++i)
            {
                ButtonLayout state = _socialButtons[i];
                ((RectTransform)state.Button.transform).sizeDelta = expanded ? new Vector2(251, 251) : state.Size;
                state.Button.transform.localScale = expanded ? commonScale : state.Scale;
                state.Button.image.preserveAspect = expanded || state.Aspect;
                if (state.Events != null)
                {
                    AccessTools.Field(typeof(ButtonsEvents), "originalLocalScale").SetValue(state.Events, expanded ? commonScale : state.EventScale);
                    AccessTools.Field(typeof(ButtonsEvents), "scaleUpSize").SetValue(state.Events, expanded ? _socialButtons[0].HoverScale : state.HoverScale);
                }
                if (state.Directed != null) state.Directed.enabled = !expanded && state.DirectedEnabled;
                UnityEngine.UI.Navigation nav = state.Navigation;
                if (expanded)
                {
                    nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                    nav.selectOnLeft = _socialButtons[(i + _socialButtons.Count - 1) % _socialButtons.Count].Button;
                    nav.selectOnRight = _socialButtons[(i + 1) % _socialButtons.Count].Button;
                    nav.selectOnUp = _mainButtons.transform.Find("Exit").GetComponent<UnityEngine.UI.Button>();
                    nav.selectOnDown = _play;
                }
                state.Button.navigation = nav;
            }
            UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(_socialRow);
        }

        private static void OpenBoosty() => Application.OpenURL(BoostyUrl);

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            bool russian = RussianFonts.IsRussian;
            if (_logo != null)
            {
                Sprite wanted = russian && _translatedLogo != null ? _translatedLogo : _originalLogo;
                if (_logo.sprite != wanted) _logo.sprite = wanted;
            }
            if (_credits == null) return;
            if (_teamButton.gameObject.activeSelf != russian) _teamButton.gameObject.SetActive(russian);
            _teamButton.interactable = russian && _play != null && _play.IsInteractable() && (_mainFade == null || _mainFade.alpha > .95f);
            SetSocialRow(russian);
            bool visible = russian && _mainButtons != null && _mainButtons.activeInHierarchy;
            if (_credits.activeSelf != visible) _credits.SetActive(visible);
            _creditFade.alpha = _mainFade == null ? 1 : _mainFade.alpha;
            _creditFade.interactable = _creditFade.blocksRaycasts = visible && _play != null && _play.IsInteractable() && _creditFade.alpha > .95f;
        }
    }

    [HarmonyPatch(typeof(NewMainMenu), "Start")]
    internal static class RussianMainMenuBrandingPatch
    {
        private static void Postfix(NewMainMenu __instance)
        {
            try
            {
                var branding = __instance.GetComponent<RussianMainMenuBranding>();
                if (branding == null) branding = __instance.gameObject.AddComponent<RussianMainMenuBranding>();
                branding.Initialize(__instance);
                if (!ReferenceEquals(BoRussianPlugin.Instance, null))
                    BoRussianPlugin.Instance.RefreshRuntime();
            }
            catch (Exception ex) { BoRussianPlugin.ModLog.LogError("Ошибка оформления главного меню: " + ex); }
        }
    }

}
