using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using I2.Loc;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace RTLC.BO.RussianTranslate
{
    internal static class RussianFonts
    {
        private static TMP_FontAsset _body;
        private static TMP_FontAsset _title;
        private static TMP_FontAsset _titlePunctuation;
        private static bool _attempted;
        private static readonly Dictionary<TMP_FontAsset, TMP_FontAsset> Variants = new Dictionary<TMP_FontAsset, TMP_FontAsset>();
        private static readonly Dictionary<TMP_FontAsset, TMP_FontAsset> Originals = new Dictionary<TMP_FontAsset, TMP_FontAsset>();
        private static readonly Dictionary<Tuple<Material, TMP_FontAsset>, Material> Materials = new Dictionary<Tuple<Material, TMP_FontAsset>, Material>();
        private static readonly Dictionary<Material, Material> MaterialSources = new Dictionary<Material, Material>();
        private static readonly Dictionary<TMP_Text, OriginalText> ChangedTexts = new Dictionary<TMP_Text, OriginalText>();
        private static readonly Dictionary<TMP_Text, string> Measurements = new Dictionary<TMP_Text, string>();
        private static readonly HashSet<TMP_Text> ExplicitAssignments = new HashSet<TMP_Text>();
        private static readonly HashSet<TMP_Text> EastAsianTexts = new HashSet<TMP_Text>();
        private static readonly Dictionary<string, TMP_FontAsset> NativeScriptFonts = new Dictionary<string, TMP_FontAsset>();

        private sealed class OriginalText
        {
            public TMP_FontAsset Font;
            public Material Material;
            public TMP_FontAsset Replacement;
        }

        internal static bool IsRussian => LocalizationManager.CurrentLanguageCode == BoRussianPlugin.RussianLanguageCode;

        private static bool EnsureLoaded()
        {
            if (_body != null && _title != null)
                return true;
            if (_attempted)
                return false;
            TMP_FontAsset bodyTemplate = Resources.Load<TMP_FontAsset>("Fonts/MortyOld_NoOutline_AnyColor");
            TMP_FontAsset titleTemplate = Resources.Load<TMP_FontAsset>("Fonts/Feruka_NoOutline_AnyColor");
            if (bodyTemplate == null || titleTemplate == null)
                return false;
            _attempted = true;
            try
            {
                SortedSet<uint> characters = new SortedSet<uint>();
                for (uint c = 32; c <= 126; c++) characters.Add(c);
                for (uint c = 0x400; c <= 0x45f; c++) characters.Add(c);
                string data = BoRussianPlugin.Instance.TranslationJson ?? string.Empty;
                for (int i = 0; i < data.Length; i++)
                {
                    if (char.IsHighSurrogate(data[i]) && i + 1 < data.Length && char.IsLowSurrogate(data[i + 1]))
                        characters.Add((uint)char.ConvertToUtf32(data[i], data[++i]));
                    else if (!char.IsControl(data[i]))
                        characters.Add(data[i]);
                }
                _body = BuildFont("LinLibertine_RB.otf", bodyTemplate, characters);
                _title = BuildFont("MediocreCartoon-Regular.ttf", titleTemplate, characters);
                _title.fallbackFontAssetTable = new List<TMP_FontAsset> { _titlePunctuation };
                BoRussianPlugin.ModLog.LogInfo("Русские шрифты готовы: Morty Old -> Linux Libertine O Bold; Feruka -> Mediocre Cartoon.");
                return true;
            }
            catch (Exception ex)
            {
                BoRussianPlugin.ModLog.LogError("Не удалось создать русские шрифты: " + ex);
                return false;
            }
        }

        private static TMP_FontAsset BuildFont(string path, TMP_FontAsset template, IEnumerable<uint> characters)
        {
            const int pointSize = 90;
            const int atlasSize = 2048;
            const int padding = 9;
            FontEngine.InitializeFontEngine();
            FontEngineError result = FontEngine.LoadFontFace(RussianAssets.ReadFont(path), pointSize);
            if (result != FontEngineError.Success)
                throw new InvalidOperationException(Path.GetFileName(path) + ": LoadFontFace=" + result);
            FaceInfo face = FontEngine.GetFaceInfo();
            Dictionary<uint, uint> unicodeToGlyph = new Dictionary<uint, uint>();
            foreach (uint unicode in characters)
            {
                uint index;
                if (FontEngine.TryGetGlyphIndex(unicode, out index) && index != 0)
                    unicodeToGlyph[unicode] = index;
            }
            foreach (uint unicode in Enumerable.Range(0x410, 64).Select(c => (uint)c).Concat(new uint[] { 0x401, 0x451 }))
                if (!unicodeToGlyph.ContainsKey(unicode))
                    throw new InvalidOperationException(Path.GetFileName(path) + ": нет русской буквы U+" + unicode.ToString("X4"));

            Texture2D atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.Alpha8, false);
            atlas.name = "BoRU_" + Path.GetFileNameWithoutExtension(path) + " Atlas";
            atlas.hideFlags = HideFlags.HideAndDontSave;
            atlas.filterMode = FilterMode.Bilinear;
            // The engine writes SDF pixels into this zero-filled texture.
            atlas.SetPixels32(new Color32[atlasSize * atlasSize]);
            atlas.Apply(false, false);
            Type[] signature = { typeof(List<uint>), typeof(int), typeof(GlyphPackingMode), typeof(List<GlyphRect>), typeof(List<GlyphRect>), typeof(GlyphRenderMode), typeof(Texture2D), typeof(Glyph[]).MakeByRefType() };
            MethodInfo pack = typeof(FontEngine).GetMethod("TryAddGlyphsToTexture", BindingFlags.Static | BindingFlags.NonPublic, null, signature, null);
            if (pack == null) throw new MissingMethodException("FontEngine.TryAddGlyphsToTexture");
            object[] args = { unicodeToGlyph.Values.Distinct().ToList(), padding, GlyphPackingMode.BestShortSideFit,
                new List<GlyphRect> { new GlyphRect(0, 0, atlasSize - 1, atlasSize - 1) }, new List<GlyphRect>(), GlyphRenderMode.SDFAA, atlas, null };
            bool packed = (bool)pack.Invoke(null, args);
            Glyph[] rendered = args[7] as Glyph[];
            Dictionary<uint, Glyph> glyphs = new Dictionary<uint, Glyph>();
            if (rendered != null)
                foreach (Glyph glyph in rendered)
                {
                    if (glyph == null) break;
                    glyphs[glyph.index] = glyph;
                }
            if (!packed || glyphs.Count != unicodeToGlyph.Values.Distinct().Count())
                throw new InvalidOperationException(Path.GetFileName(path) + ": атлас вместил " + glyphs.Count + " глифов.");
            GlyphPairAdjustmentRecord[] nativePairs = RussianFontMetrics.ReadPairs(glyphs.Keys, face);

            TMP_FontAsset font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            font.name = "BoRU_" + Path.GetFileNameWithoutExtension(path);
            font.hideFlags = HideFlags.HideAndDontSave;
            AccessTools.Field(typeof(TMP_FontAsset), "m_Version").SetValue(font, "1.1.0");
            font.faceInfo = face;
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasWidth").SetValue(font, atlasSize);
            AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasHeight").SetValue(font, atlasSize);
            AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasPadding").SetValue(font, padding);
            AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasRenderMode").SetValue(font, GlyphRenderMode.SDFAA);
            font.atlasTextures = new[] { atlas };
            AccessTools.Field(typeof(TMP_FontAsset), "m_GlyphTable").SetValue(font, glyphs.Values.ToList());
            AccessTools.Field(typeof(TMP_FontAsset), "m_CharacterTable").SetValue(font,
                unicodeToGlyph.Select(pair => new TMP_Character(pair.Key, glyphs[pair.Value])).ToList());
            font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            font.material = MakeMaterial(template.material, font);
            font.ReadFontAssetDefinition();
            if (template.name.StartsWith("MortyOld", StringComparison.OrdinalIgnoreCase))
            {
                // Share the raw Bold atlas, but keep an independent set of glyph metrics
                // for punctuation used inside Feruka headings.
                _titlePunctuation = UnityEngine.Object.Instantiate(font);
                _titlePunctuation.name = "BoRU_TitlePunctuation_LibertineBold";
                var copiedGlyphs = font.glyphTable.ToDictionary(g => g.index,
                    g => new Glyph(g.index, g.metrics, g.glyphRect, g.scale, g.atlasIndex));
                AccessTools.Field(typeof(TMP_FontAsset), "m_GlyphTable").SetValue(_titlePunctuation, copiedGlyphs.Values.ToList());
                AccessTools.Field(typeof(TMP_FontAsset), "m_CharacterTable").SetValue(_titlePunctuation,
                    font.characterTable.Select(ch => new TMP_Character(ch.unicode, copiedGlyphs[ch.glyphIndex])).ToList());
                TMP_FontAsset heading = Resources.Load<TMP_FontAsset>("Fonts/Feruka_NoOutline_AnyColor");
                _titlePunctuation.material = MakeMaterial(heading.material, _titlePunctuation);
                RussianFontMetrics.Match(_titlePunctuation, heading, nativePairs);
            }
            RussianFontMetrics.Match(font, template, nativePairs);
            BoRussianPlugin.ModLog.LogInfo("TMP-шрифт " + face.familyName + ": " + unicodeToGlyph.Count + " символов, " + glyphs.Count + " глифов, русский алфавит 66/66.");
            return font;
        }

        private static Material MakeMaterial(Material original, TMP_FontAsset font)
        {
            original = SourceMaterial(original);
            var key = Tuple.Create(original, font);
            Material material;
            if (Materials.TryGetValue(key, out material)) return material;
            material = new Material(original);
            material.name = font.name + " / " + original.name;
            material.hideFlags = HideFlags.HideAndDontSave;
            material.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTextures[0]);
            material.SetFloat(ShaderUtilities.ID_TextureWidth, font.atlasWidth);
            material.SetFloat(ShaderUtilities.ID_TextureHeight, font.atlasHeight);
            material.SetFloat(ShaderUtilities.ID_GradientScale, font.atlasPadding + 1);
            if (font.faceInfo.familyName == "Mediocre Cartoon")
                material.SetFloat(ShaderUtilities.ID_FaceDilate, original.GetFloat(ShaderUtilities.ID_FaceDilate) + 0.15f);
            Materials[key] = material;
            MaterialSources[material] = original;
            return material;
        }

        private static Material SourceMaterial(Material material)
        {
            Material source;
            while (material != null && MaterialSources.TryGetValue(material, out source)) material = source;
            return material;
        }

        private static bool MatchesAtlas(Material material, TMP_FontAsset font) => material != null && font != null
            && material.GetTexture(ShaderUtilities.ID_MainTex) == font.atlasTexture;

        private static Material NativeMaterial(TMP_FontAsset font, Material material)
        {
            material = SourceMaterial(material);
            return MatchesAtlas(material, font) ? material : font.material;
        }

        private static void EnsureMaterial(TMP_Text text)
        {
            TMP_FontAsset font = text.font;
            if (font == null || MatchesAtlas(text.fontSharedMaterial, font)) return;
            TMP_FontAsset original;
            text.fontSharedMaterial = Originals.TryGetValue(font, out original)
                ? MakeMaterial(NativeMaterial(original, text.fontSharedMaterial), font)
                : font.material;
        }

        private static string ParsingText(TMP_Text text)
        {
            string measured;
            return Measurements.TryGetValue(text, out measured) ? measured : text.text;
        }

        // GetPreferredValues(string) parses its argument without assigning TMP_Text.text.
        // Keep nested measurements scoped to their own argument.
        internal static string BeginMeasurement(TMP_Text text, string value)
        {
            string previous;
            Measurements.TryGetValue(text, out previous);
            Measurements[text] = value;
            return previous;
        }

        internal static void EndMeasurement(TMP_Text text, string previous)
        {
            if (previous == null) Measurements.Remove(text); else Measurements[text] = previous;
        }

        internal static void AssignFont(TMP_Text text, TMP_FontAsset font)
        {
            if (font == null) return;
            TMP_FontAsset original;
            if (Originals.TryGetValue(font, out original) && text.font != font)
                ChangedTexts[text] = new OriginalText { Font = original, Material = NativeMaterial(original, text.fontSharedMaterial), Replacement = font };
            ExplicitAssignments.Add(text);
            try { text.font = font; }
            finally { ExplicitAssignments.Remove(text); }
            EnsureMaterial(text);
        }

        internal static TMP_FontAsset Resolve(TMP_FontAsset original, bool force = false)
        {
            if (original == null || (!force && !IsRussian) || !EnsureLoaded()) return original;
            TMP_FontAsset replacement;
            if (Originals.ContainsKey(original) || original == _body || original == _title) return original;
            if (Variants.TryGetValue(original, out replacement)) return replacement;
            TMP_FontAsset basis = original.name.StartsWith("MortyOld", StringComparison.OrdinalIgnoreCase) ? _body
                : original.name.StartsWith("Feruka", StringComparison.OrdinalIgnoreCase) ? _title : null;
            if (basis == null) return original;
            replacement = UnityEngine.Object.Instantiate(basis);
            replacement.name = "BoRU_" + original.name;
            replacement.hideFlags = HideFlags.HideAndDontSave;
            replacement.normalStyle = original.normalStyle;
            replacement.boldStyle = original.boldStyle;
            replacement.material = MakeMaterial(original.material, replacement);
            replacement.ReadFontAssetDefinition();
            Variants[original] = replacement;
            Originals[replacement] = original;
            return replacement;
        }

        internal static TMP_FontAsset DropdownFont(TMP_FontAsset englishFont)
        {
            if (!EnsureLoaded()) return englishFont;
            TMP_FontAsset mapped = Resolve(englishFont, true);
            return mapped != englishFont ? mapped : _body;
        }

        internal static Material BeforeFontSet(TMP_Text text, ref TMP_FontAsset font)
        {
            RegisterScriptFont(font);
            if (ExplicitAssignments.Contains(text)) return null;
            SetLanguageDropdown selector = text.name == "Item Label" ? text.GetComponentInParent<SetLanguageDropdown>(true) : null;
            TMP_FontAsset itemFont = LanguageItemFont(selector, ParsingText(text));
            if (itemFont != null) { font = itemFont; return null; }
            if (IsForeignLatin(ParsingText(text)))
            {
                TMP_FontAsset native = LatinContentFont(text, ParsingText(text), font);
                if (native != null) { font = native; return null; }
            }
            bool languageItem = selector != null && HasCyrillic(ParsingText(text));
            TMP_FontAsset mapped = Resolve(font, languageItem);
            if (mapped == font) return null;
            Material originalMaterial = NativeMaterial(font, text.font == font ? text.fontSharedMaterial : font.material);
            ChangedTexts[text] = new OriginalText { Font = font, Material = originalMaterial, Replacement = mapped };
            font = mapped;
            return originalMaterial;
        }

        internal static void AfterFontSet(TMP_Text text, Material originalMaterial)
        {
            if (originalMaterial != null)
                text.fontSharedMaterial = MakeMaterial(originalMaterial, text.font);
            EnsureMaterial(text);
        }

        internal static void SynchronizeRenderer(TextMeshProUGUI text)
        {
            if (text == null || !text.isActiveAndEnabled || text.fontSharedMaterial == null) return;
            CanvasRenderer renderer = text.canvasRenderer;
            Material drawn = renderer.materialCount == 0 ? null : renderer.GetMaterial(0);
            Texture expected = text.fontSharedMaterial.GetTexture(ShaderUtilities.ID_MainTex);
            if (expected == null || (drawn != null && drawn.GetTexture(ShaderUtilities.ID_MainTex) == expected)) return;
            // SetMaterialDirty is ignored during CanvasUpdateRegistry's graphics
            // rebuild. A font chosen in SetArraySizes then produces new UVs but
            // leaves the old atlas bound to the renderer. Bind TMP's own material
            // after parsing, preserving its stencil/masking implementation.
            renderer.materialCount = 1;
            renderer.SetMaterial(text.materialForRendering, 0);
        }

        private static bool HasCyrillic(string value) => value != null && value.Any(c => c >= 0x400 && c <= 0x52f);

        private static readonly HashSet<TMP_Text> LatinTexts = new HashSet<TMP_Text>();

        private static bool IsForeignLatin(string value) => !HasCyrillic(value)
            && value != null && !value.Any(IsEastAsian) && value.Any(c => c >= 0xc0 && c <= 0x24f && char.IsLetter(c));

        private static TMP_FontAsset LatinContentFont(TMP_Text text, string value, TMP_FontAsset requested)
        {
            // During a language change, hidden pages and measurement arguments
            // still contain the previous locale's accented Latin strings. Keep
            // a native face until Russian text arrives, instead of feeding them
            // to the narrower Cyrillic heading font's character repertoire.
            TMP_FontAsset original;
            if (requested != null && Originals.TryGetValue(requested, out original)) requested = original;
            TMP_FontAsset current = text.font;
            if (current != null && Originals.TryGetValue(current, out original)) current = original;
            TMP_FontAsset english = EnglishRoleFont(text);
            bool title = english != null && english.name.StartsWith("Feruka");
            bool outline = english != null && english.name.Contains("BlackOutline");
            var preserved = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f != null
                && (f.name.StartsWith("BP_") || f.name.StartsWith("Spanish_") || f.name.StartsWith("French_") || f.name.StartsWith("German_"))
                && f.name.Contains("Title") == title && f.name.Contains("BlackOutline") == outline);
            TMP_FontAsset localized = IsRussian ? null : LocalizedRoleFont(text, LocalizationManager.CurrentLanguage);
            foreach (TMP_FontAsset candidate in new[] { localized, requested, current, english }.Concat(preserved).Where(f => f != null).Distinct())
                if (!candidate.name.StartsWith("BoRU_") && value.All(c => c < 0xc0 || c > 0x24f
                    || !char.IsLetter(c) || candidate.HasCharacter(c, true, false))) return candidate;
            return null;
        }

        internal static void BeforeTextSet(TMP_Text text, string value)
        {
            if (text == null || !text.gameObject.scene.IsValid()) return;
            SetLanguageDropdown selector = text.name == "Item Label" ? text.GetComponentInParent<SetLanguageDropdown>(true) : null;
            if (selector != null)
            {
                TMP_FontAsset font = LanguageItemFont(selector, value);
                // The font setter still sees the previous label string here.
                // Assign after the text setter as well, immediately before parsing.
                if (font != null && text.font != font) AssignFont(text, font);
            }
            else if (IsRussian && HasCyrillic(value) && Resolve(text.font) != text.font) text.font = text.font;
            else if (!IsRussian && !HasCyrillic(value)) RestoreFont(text);
        }

        private static TMP_FontAsset LanguageItemFont(SetLanguageDropdown selector, string value)
        {
            TMP_Dropdown dropdown = selector == null ? null : selector.GetComponent<TMP_Dropdown>();
            int index = dropdown == null ? -1 : dropdown.options.FindIndex(o => o.text == value);
            return index < 0 ? null : RussianDropdownFontPatch.GetLanguageFont(selector, index);
        }

        private static TMP_FontAsset EnglishRoleFont(TMP_Text text)
        {
            Localize localize = text.GetComponent<Localize>();
            if (localize != null && !string.IsNullOrEmpty(localize.SecondaryTerm))
            {
                string path = LocalizationManager.GetTranslation(localize.SecondaryTerm, overrideLanguage: "English");
                if (!string.IsNullOrEmpty(path))
                {
                    TMP_FontAsset english = Resources.Load<TMP_FontAsset>(path);
                    if (english != null && (english.name.StartsWith("MortyOld", StringComparison.OrdinalIgnoreCase)
                        || english.name.StartsWith("Feruka", StringComparison.OrdinalIgnoreCase))) return english;
                }
            }
            string name = text.font == null ? "" : text.font.name;
            bool title = name.Contains("Feruka") || name.Contains("Mediocre") || name.StartsWith("YujiSyuku")
                || name.StartsWith("ZCOOLKuaiLe") || name.StartsWith("Hahmlet");
            return Resources.Load<TMP_FontAsset>("Fonts/" + (title ? "Feruka" : "MortyOld")
                + (name.Contains("BlackOutline") ? "_BlackOutline_WhiteTextColor" : "_NoOutline_AnyColor"));
        }

        internal static void BeforeParse(TMP_Text text)
        {
            if (text == null || !text.gameObject.scene.IsValid()) return;
            string value = ParsingText(text);
            SetLanguageDropdown selector = text.name == "Item Label" ? text.GetComponentInParent<SetLanguageDropdown>(true) : null;
            if (selector != null)
            {
                TMP_FontAsset itemFont = LanguageItemFont(selector, value);
                if (itemFont != null) { AssignFont(text, itemFont); return; }
            }
            if (IsForeignLatin(value))
            {
                TMP_FontAsset native = LatinContentFont(text, value, text.font);
                if (native != null)
                {
                    LatinTexts.Add(text);
                    AssignFont(text, native);
                    EnsureMaterial(text);
                    return;
                }
            }
            if (LatinTexts.Remove(text))
            {
                // The old accented string may have outlived the locale's font
                // callback. Restore the current role when its replacement arrives,
                // including replacements containing ASCII only.
                TMP_FontAsset localized = LocalizedRoleFont(text, LocalizationManager.CurrentLanguage);
                if (localized != null) AssignFont(text, Resolve(localized));
            }
            // Inactive loading tips are localized lazily. Font callbacks can run
            // while the string still belongs to the previous Asian locale.
            // Resolve that string first, then restore the current locale's role
            // as soon as the content changes. Do not give Japanese glyphs RU UVs.
            if (value != null && value.Any(IsEastAsian))
            {
                EastAsianTexts.Add(text);
                if (text.font == null || value.Any(c => IsEastAsian(c) && !text.font.HasCharacter(c, true, false)))
                {
                    string preferred = value.Any(c => c >= 0x3040 && c <= 0x30ff) ? "Japanese"
                        : value.Any(c => c >= 0xac00 && c <= 0xd7af || c >= 0x1100 && c <= 0x11ff) ? "Korean"
                        : LocalizationManager.CurrentLanguage;
                    TMP_FontAsset first = null;
                    foreach (string language in new[] { preferred, "Japanese", "Chinese (Simplified)", "Chinese (Traditional)", "Korean" }.Distinct())
                    {
                        TMP_FontAsset candidate = LocalizedRoleFont(text, language);
                        if (candidate == null) continue;
                        if (first == null) first = candidate;
                        if (value.All(c => !IsEastAsian(c) || candidate.HasCharacter(c, true, false))) { first = candidate; break; }
                    }
                    if (first != null) AssignFont(text, first);
                }
                EnsureAsciiFallback(text, value);
                EnsureMaterial(text);
                return;
            }
            if (EastAsianTexts.Remove(text))
            {
                TMP_FontAsset localized = LocalizedRoleFont(text, LocalizationManager.CurrentLanguage);
                if (localized != null) AssignFont(text, Resolve(localized));
            }
            if (IsRussian)
            {
                TMP_FontAsset mapped = Resolve(text.font);
                if (mapped != text.font) text.font = text.font;
                else if (HasCyrillic(value) && text.font != null && !text.font.name.StartsWith("BoRU_"))
                    AssignFont(text, Resolve(EnglishRoleFont(text), true));
                // Numeric option templates can keep a native Japanese font even
                // in RU. Resolve ASCII punctuation before its first measurement.
                EnsureAsciiFallback(text, value);
                EnsureMaterial(text);
                return;
            }
            // Font and string localization are separate callbacks. While switching
            // away from Russian, a new Asian font can still receive the old Russian
            // string. Keep a Cyrillic-capable face until the string changes too.
            if (HasCyrillic(value) && text.font != null)
            {
                if (value.Any(c => c >= 0x400 && c <= 0x52f && !text.font.HasCharacter(c)))
                    AssignFont(text, Resolve(EnglishRoleFont(text), true));
                EnsureMaterial(text);
                return;
            }
            RestoreFont(text);
            EnsureAsciiFallback(text, value);
            EnsureMaterial(text);
        }

        private static bool IsEastAsian(char c) => c >= 0x2e80 && c <= 0x9fff || c >= 0xf900 && c <= 0xfaff
            || c >= 0xac00 && c <= 0xd7af || c >= 0x1100 && c <= 0x11ff;

        private static TMP_FontAsset LocalizedRoleFont(TMP_Text text, string language)
        {
            Localize localize = text.GetComponent<Localize>();
            string term = localize == null ? null : localize.SecondaryTerm;
            FontManagerSO manager = GameManager.Instance == null ? null : GameManager.Instance.GetFontManager();
            if (manager == null) return null;
            string name = text.font == null ? "" : text.font.name;
            bool title = term == manager.TitleNoOutline || term == manager.TitleBlackOutline || name.Contains("Feruka")
                || name.Contains("Mediocre") || name.StartsWith("YujiSyuku") || name.StartsWith("ZCOOLKuaiLe") || name.StartsWith("Hahmlet") || name.StartsWith("NotoSansTC");
            bool outline = term == manager.BodyBlackOutline || term == manager.TitleBlackOutline || name.Contains("BlackOutline");
            string code = LocalizationManager.GetLanguageCode(language);
            if (string.IsNullOrEmpty(term)) term = title ? (outline ? manager.TitleBlackOutline : manager.TitleNoOutline)
                : (outline ? manager.BodyBlackOutline : manager.BodyNoOutline);
            if (language == LocalizationManager.CurrentLanguage)
            {
                string currentPath = LocalizationManager.GetTranslation(term);
                TMP_FontAsset currentFont = string.IsNullOrEmpty(currentPath) ? null : Resources.Load<TMP_FontAsset>(currentPath);
                if (currentFont != null) return currentFont;
            }
            foreach (TMP_FontAsset loaded in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) RegisterScriptFont(loaded);
            TMP_FontAsset preserved;
            if (NativeScriptFonts.TryGetValue(code + "|" + title + "|" + outline, out preserved) && preserved != null) return preserved;
            if (language == manager.Japanese)
            {
                string japanese = title ? (outline ? manager.Japanese_Title_BlackOutline_Path : manager.Japanese_Title_NoOutline_Path)
                    : (outline ? manager.Japanese_Body_BlackOutline_Path : manager.Japanese_Body_NoOutline_Path);
                return Resources.Load<TMP_FontAsset>(japanese);
            }
            // Asset terms have one mutable English column and empty foreign
            // columns. Their fallback is the current asset for every language.
            // Use that only for the current locale; foreign roles are preserved above.
            string path = language == LocalizationManager.CurrentLanguage ? LocalizationManager.GetTranslation(term) : null;
            return string.IsNullOrEmpty(path) ? null : Resources.Load<TMP_FontAsset>(path);
        }

        private static void RegisterScriptFont(TMP_FontAsset font)
        {
            if (font == null) return;
            string name = font.name;
            string code = name.StartsWith("SatsukiGendaiMincho") || name.StartsWith("YujiSyuku") ? "ja"
                : name.StartsWith("MaShanZheng") || name.StartsWith("ZCOOLKuaiLe") ? "zh-CN"
                : name.StartsWith("SongMyung") || name.StartsWith("Hahmlet") ? "ko"
                : name.StartsWith("TW-Kai") || name.StartsWith("NotoSansTC") ? "zh-TW" : null;
            if (code == null) return;
            bool title = name.StartsWith("YujiSyuku") || name.StartsWith("ZCOOLKuaiLe") || name.StartsWith("Hahmlet") || name.StartsWith("NotoSansTC");
            NativeScriptFonts[code + "|" + title + "|" + name.Contains("BlackOutline")] = font;
        }

        private static void EnsureAsciiFallback(TMP_Text text, string value)
        {
            TMP_FontAsset font = text.font;
            if (font == null || string.IsNullOrEmpty(value) || font.name.StartsWith("BoRU_")) return;
            // Resolution templates can retain the Japanese face without ever
            // opening the language list. Check the actual string before TMP parses
            // or measures it, instead of attaching fallbacks only to language items.
            if (!value.Any(c => c >= 32 && c <= 126 && !font.HasCharacter(c, true, false))) return;
            TMP_FontAsset english = EnglishRoleFont(text);
            if (english == null || english == font) return;
            if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!font.fallbackFontAssetTable.Contains(english)) font.fallbackFontAssetTable.Add(english);
        }

        private static void RestoreFont(TMP_Text text)
        {
            // Language localization can measure the new string before the game's
            // next font refresh. Restore its localized font before TMP resolves glyphs.
            TMP_FontAsset original;
            if (text.font == null || !Originals.TryGetValue(text.font, out original)) return;
            TMP_FontAsset localized = original;
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                FontManagerSO fm = gm.GetFontManager();
                if (fm != null)
                {
                    bool title = original.name.StartsWith("Feruka", StringComparison.OrdinalIgnoreCase);
                    bool outline = original.name.Contains("BlackOutline");
                    string term = title ? (outline ? fm.TitleBlackOutline : fm.TitleNoOutline)
                        : (outline ? fm.BodyBlackOutline : fm.BodyNoOutline);
                    string path = LocalizationManager.GetTranslation(term);
                    if (!string.IsNullOrEmpty(path)) localized = Resources.Load<TMP_FontAsset>(path) ?? original;
                }
            }
            OriginalText old;
            bool recorded = ChangedTexts.TryGetValue(text, out old);
            AssignFont(text, localized);
            if (recorded && localized == original) text.fontSharedMaterial = NativeMaterial(original, old.Material);
            ChangedTexts.Remove(text);
            EnsureMaterial(text);
        }

        internal static void Refresh()
        {
            if (!EnsureLoaded()) return;
            if (!IsRussian)
            {
                foreach (var pair in ChangedTexts.ToArray())
                {
                    TMP_Text text = pair.Key;
                    OriginalText original = pair.Value;
                    if (text != null && text.font == original.Replacement)
                    {
                        // A font callback can run before the old Russian string is replaced.
                        if (HasCyrillic(text.text)) continue;
                        if (text.name == "Item Label" && text.GetComponentInParent<SetLanguageDropdown>(true) != null) continue;
                        RestoreFont(text);
                    }
                    ChangedTexts.Remove(text);
                }
                return;
            }
            foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (text == null || !text.gameObject.scene.IsValid()) continue;
                TMP_FontAsset mapped = Resolve(text.font);
                if (mapped != text.font) text.font = text.font; // Setter patch records the original material and applies the replacement.
            }
            foreach (TMP_Text text in ChangedTexts.Keys.Where(t => t == null).ToArray())
                ChangedTexts.Remove(text);
            EastAsianTexts.RemoveWhere(t => t == null);
            LatinTexts.RemoveWhere(t => t == null);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "font", MethodType.Setter)]
    internal static class RussianTextFontPatch
    {
        private static void Prefix(TMP_Text __instance, ref TMP_FontAsset value, out Material __state)
        {
            __state = RussianFonts.BeforeFontSet(__instance, ref value);
        }

        private static void Postfix(TMP_Text __instance, Material __state)
        {
            RussianFonts.AfterFontSet(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "text", MethodType.Setter)]
    internal static class RussianTextCreationFontPatch
    {
        private static void Prefix(TMP_Text __instance, string value) => RussianFonts.BeforeTextSet(__instance, value);
    }

    // Serialized prefab text and SetText/SetCharArray bypass the text property.
    // TMP resolves characters in SetArraySizes, before either measuring or rendering.
    [HarmonyPatch]
    internal static class RussianTextParsingFontPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(TextMeshProUGUI), "SetArraySizes");
            yield return AccessTools.Method(typeof(TextMeshPro), "SetArraySizes");
        }
        private static void Prefix(TMP_Text __instance) => RussianFonts.BeforeParse(__instance);
    }

    [HarmonyPatch]
    internal static class RussianTextMeasurementFontPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(TMP_Text), nameof(TMP_Text.GetPreferredValues), new[] { typeof(string) });
            yield return AccessTools.Method(typeof(TMP_Text), nameof(TMP_Text.GetPreferredValues), new[] { typeof(string), typeof(float), typeof(float) });
        }
        private static void Prefix(TMP_Text __instance, string text, out string __state)
            => __state = RussianFonts.BeginMeasurement(__instance, text);
        private static void Finalizer(TMP_Text __instance, string __state) => RussianFonts.EndMeasurement(__instance, __state);
    }

    [HarmonyPatch(typeof(TextMeshProUGUI), "OnPreRenderCanvas")]
    internal static class RussianTextRendererFontPatch
    {
        private static void Postfix(TextMeshProUGUI __instance) => RussianFonts.SynchronizeRenderer(__instance);
    }
}
