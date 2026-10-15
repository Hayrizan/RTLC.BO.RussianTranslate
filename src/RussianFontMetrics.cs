using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace RTLC.BO.RussianTranslate
{
    internal static class RussianFontMetrics
    {
        private const string Cyrillic = "АВЕКМНОРСТУХаеорсухЁё";
        private const string Latin =    "ABEKMHOPCTYXaeopcyxËë";
        // Feruka's tight advances need a small gap with Cartoon's thicker contours.
        private const float CartoonTrackingAt90 = 6f;

        internal static GlyphPairAdjustmentRecord[] ReadPairs(IEnumerable<uint> indexes, FaceInfo face)
        {
            var method = AccessTools.Method(typeof(FontEngine), "GetPairAdjustmentRecords", new[] { typeof(uint[]) })
                ?? AccessTools.Method(typeof(FontEngine), "GetGlyphPairAdjustmentTable", new[] { typeof(uint[]) });
            if (method == null) return null;
            var pairs = (GlyphPairAdjustmentRecord[])method.Invoke(null, new object[] { indexes.ToArray() });
            if (pairs == null) return null;
            // This game's FontEngine returns GPOS adjustments in design units,
            // whereas the packed SDF glyph metrics are already in point-size units.
            int units = (int)AccessTools.Field(typeof(FaceInfo), "m_UnitsPerEM").GetValue(face);
            if (units <= 0) throw new InvalidOperationException("FontEngine не сообщил unitsPerEm: " + face.familyName);
            float scale = face.pointSize / (float)units;
            for (int i = 0; i < pairs.Length; i++)
            {
                var a = pairs[i].firstAdjustmentRecord;
                var b = pairs[i].secondAdjustmentRecord;
                var av = a.glyphValueRecord;
                var bv = b.glyphValueRecord;
                pairs[i] = new GlyphPairAdjustmentRecord(
                    new GlyphAdjustmentRecord(a.glyphIndex, new GlyphValueRecord(av.xPlacement * scale, av.yPlacement * scale, av.xAdvance * scale, av.yAdvance * scale)),
                    new GlyphAdjustmentRecord(b.glyphIndex, new GlyphValueRecord(bv.xPlacement * scale, bv.yPlacement * scale, bv.xAdvance * scale, bv.yAdvance * scale)))
                    { featureLookupFlags = pairs[i].featureLookupFlags };
            }
            return pairs;
        }

        internal static void Match(TMP_FontAsset font, TMP_FontAsset reference, GlyphPairAdjustmentRecord[] nativePairs)
        {
            reference.ReadFontAssetDefinition();
            font.ReadFontAssetDefinition();
            bool title = reference.name.StartsWith("Feruka", StringComparison.OrdinalIgnoreCase);
            var sourceFace = font.faceInfo;
            var refFace = reference.faceInfo;
            if (!title)
            {
                MatchBodySize(font, reference, nativePairs);
                return;
            }
            float tracking = title && sourceFace.familyName == "Mediocre Cartoon"
                ? CartoonTrackingAt90 * sourceFace.pointSize / 90f
                : 0f;
            float units = sourceFace.pointSize / (float)refFace.pointSize * refFace.scale;
            float upperX = WidthRatio(font, reference, 'A', 'Z', units);
            float lowerX = title ? upperX : WidthRatio(font, reference, 'a', 'z', units);
            float upperY = HeightRatio(font, reference, 'H', units);
            float lowerY = title ? upperY : HeightRatio(font, reference, 'x', units);
            float upperOffset = reference.characterLookupTable['H'].glyph.metrics.horizontalBearingY * units
                - font.characterLookupTable['H'].glyph.metrics.horizontalBearingY * upperY;
            float lowerOffset = title ? upperOffset : reference.characterLookupTable['x'].glyph.metrics.horizontalBearingY * units
                - font.characterLookupTable['x'].glyph.metrics.horizontalBearingY * lowerY;

            // Feruka draws lowercase as capital-height letters. Match that behavior
            // without changing the localized strings or the body font's casing.
            if (title)
                foreach (TMP_Character ch in font.characterTable)
                {
                    uint upper = ch.unicode <= char.MaxValue ? char.ToUpperInvariant((char)ch.unicode) : ch.unicode;
                    TMP_Character capital;
                    if (upper != ch.unicode && font.characterLookupTable.TryGetValue(upper, out capital))
                    {
                        ch.glyphIndex = capital.glyphIndex;
                        ch.glyph = capital.glyph;
                    }
                }

            var changed = new HashSet<uint>();
            var mapped = new HashSet<uint>();
            var mapping = new Dictionary<uint, HashSet<uint>>();
            foreach (TMP_Character ch in font.characterTable.OrderBy(c => c.unicode))
            {
                uint code = title && ch.unicode <= char.MaxValue ? char.ToUpperInvariant((char)ch.unicode) : ch.unicode;
                int alias = code <= char.MaxValue ? Cyrillic.IndexOf((char)code) : -1;
                uint match = alias >= 0 ? Latin[alias] : code;
                TMP_Character original;
                bool hasReference = reference.characterLookupTable.TryGetValue(match, out original) && original.glyph != null;
                if (hasReference)
                {
                    mapped.Add(ch.glyphIndex);
                    HashSet<uint> destinations;
                    if (!mapping.TryGetValue(original.glyphIndex, out destinations))
                        mapping[original.glyphIndex] = destinations = new HashSet<uint>();
                    destinations.Add(ch.glyphIndex);
                }
                if (!changed.Add(ch.glyphIndex)) continue;
                GlyphMetrics m = ch.glyph.metrics;
                bool lower = code <= char.MaxValue && char.IsLower((char)code);
                if (hasReference)
                {
                    GlyphMetrics r = original.glyph.metrics;
                    float scale = units * original.glyph.scale;
                    ch.glyph.metrics = new GlyphMetrics(r.width * scale, r.height * scale,
                        r.horizontalBearingX * scale, r.horizontalBearingY * scale, r.horizontalAdvance * scale);
                }
                else
                {
                    float x = lower ? lowerX : upperX;
                    float y = lower ? lowerY : upperY;
                    ch.glyph.metrics = new GlyphMetrics(m.width * x, m.height * y,
                        m.horizontalBearingX * x, m.horizontalBearingY * y + (lower ? lowerOffset : upperOffset), m.horizontalAdvance * x);
                }
                // Leave spaces unchanged and apply once per shared uppercase glyph.
                if (tracking != 0f && code <= char.MaxValue && !char.IsWhiteSpace((char)code)
                    && !char.IsControl((char)code))
                {
                    GlyphMetrics adjusted = ch.glyph.metrics;
                    adjusted.horizontalAdvance += tracking;
                    ch.glyph.metrics = adjusted;
                }
            }

            // Keep the original line boxes, baselines, word spacing and script metrics.
            // The family/style identify the actual supplied font, not the reference.
            var face = refFace;
            face.familyName = sourceFace.familyName;
            face.styleName = sourceFace.styleName;
            face.pointSize = sourceFace.pointSize;
            face.scale = 1f;
            face.ascentLine *= units; face.capLine *= units; face.meanLine *= units;
            face.descentLine *= units; face.baseline *= units; face.lineHeight *= units;
            face.superscriptOffset *= units; face.subscriptOffset *= units;
            face.underlineOffset *= units; face.underlineThickness *= units;
            face.strikethroughOffset *= units; face.strikethroughThickness *= units; face.tabWidth *= units;
            font.faceInfo = face;
            font.normalStyle = reference.normalStyle; font.boldStyle = reference.boldStyle;
            font.normalSpacingOffset = reference.normalSpacingOffset; font.boldSpacing = reference.boldSpacing;

            var pairs = new Dictionary<ulong, TMP_GlyphPairAdjustmentRecord>();
            if (nativePairs != null)
                foreach (GlyphPairAdjustmentRecord pair in nativePairs)
                {
                    uint first = pair.firstAdjustmentRecord.glyphIndex, second = pair.secondAdjustmentRecord.glyphIndex;
                    if (first == 0) break;
                    if (mapped.Contains(first) && mapped.Contains(second)) continue;
                    var a = pair.firstAdjustmentRecord.glyphValueRecord;
                    var b = pair.secondAdjustmentRecord.glyphValueRecord;
                    AddPair(pairs, first, second,
                        new TMP_GlyphValueRecord(a.xPlacement * lowerX, a.yPlacement * lowerY, a.xAdvance * lowerX, a.yAdvance * lowerY),
                        new TMP_GlyphValueRecord(b.xPlacement * lowerX, b.yPlacement * lowerY, b.xAdvance * lowerX, b.yAdvance * lowerY));
                }
            foreach (TMP_GlyphPairAdjustmentRecord pair in reference.fontFeatureTable.glyphPairAdjustmentRecords)
            {
                HashSet<uint> first, second;
                if (!mapping.TryGetValue(pair.firstAdjustmentRecord.glyphIndex, out first)
                    || !mapping.TryGetValue(pair.secondAdjustmentRecord.glyphIndex, out second)) continue;
                var a = pair.firstAdjustmentRecord.glyphValueRecord;
                var b = pair.secondAdjustmentRecord.glyphValueRecord;
                foreach (uint left in first)
                    foreach (uint right in second)
                        AddPair(pairs, left, right,
                            new TMP_GlyphValueRecord(a.xPlacement * units, a.yPlacement * units, a.xAdvance * units, a.yAdvance * units),
                            new TMP_GlyphValueRecord(b.xPlacement * units, b.yPlacement * units, b.xAdvance * units, b.yAdvance * units));
            }
            font.fontFeatureTable.glyphPairAdjustmentRecords.Clear();
            font.fontFeatureTable.glyphPairAdjustmentRecords.AddRange(pairs.Values);
            font.ReadFontAssetDefinition();
            BoRussianPlugin.ModLog.LogInfo($"Метрики {font.name} → {reference.name}: ширина прописных {upperX:F4}, строчных {lowerX:F4}; высота {upperY:F4}/{lowerY:F4}; кернинг {pairs.Count} пар; дополнительный шаг {tracking:F2}.");
        }

        private static void MatchBodySize(TMP_FontAsset font, TMP_FontAsset reference, GlyphPairAdjustmentRecord[] nativePairs)
        {
            var source = font.faceInfo;
            float units = source.pointSize / (float)reference.faceInfo.pointSize * reference.faceInfo.scale;
            // One scale for the entire face: retain Libertine's shapes, side bearings,
            // advances and kerning. In particular, ё must never inherit ë's metrics.
            // Body copy is predominantly lowercase: match Morty's x-height, then
            // retain the native relationship between capitals and lowercase.
            float scale = HeightRatio(font, reference, 'x', units);
            foreach (Glyph glyph in font.glyphTable)
            {
                GlyphMetrics m = glyph.metrics;
                glyph.metrics = new GlyphMetrics(m.width * scale, m.height * scale,
                    m.horizontalBearingX * scale, m.horizontalBearingY * scale, m.horizontalAdvance * scale);
            }
            var face = source;
            // Preserve the game's UI line box; only the glyphs and their natural
            // spacing are scaled. Native ascender bounds otherwise trigger AutoSize
            // and false overflow in the original 80/90-unit caption rectangles.
            face.ascentLine = reference.faceInfo.ascentLine * units;
            face.descentLine = reference.faceInfo.descentLine * units;
            face.baseline = reference.faceInfo.baseline * units;
            face.capLine *= scale; face.meanLine *= scale;
            face.superscriptOffset *= scale; face.subscriptOffset *= scale;
            face.underlineOffset *= scale; face.underlineThickness *= scale;
            face.strikethroughOffset *= scale; face.strikethroughThickness *= scale; face.tabWidth *= scale;
            face.lineHeight = reference.faceInfo.lineHeight * units;
            font.faceInfo = face;
            font.normalStyle = reference.normalStyle; font.boldStyle = reference.boldStyle;
            font.normalSpacingOffset = 0f; font.boldSpacing = 0f;
            var pairs = new Dictionary<ulong, TMP_GlyphPairAdjustmentRecord>();
            if (nativePairs != null)
                foreach (GlyphPairAdjustmentRecord pair in nativePairs)
                {
                    uint first = pair.firstAdjustmentRecord.glyphIndex, second = pair.secondAdjustmentRecord.glyphIndex;
                    if (first == 0) break;
                    var a = pair.firstAdjustmentRecord.glyphValueRecord;
                    var b = pair.secondAdjustmentRecord.glyphValueRecord;
                    AddPair(pairs, first, second,
                        new TMP_GlyphValueRecord(a.xPlacement * scale, a.yPlacement * scale, a.xAdvance * scale, a.yAdvance * scale),
                        new TMP_GlyphValueRecord(b.xPlacement * scale, b.yPlacement * scale, b.xAdvance * scale, b.yAdvance * scale));
                }
            font.fontFeatureTable.glyphPairAdjustmentRecords.Clear();
            font.fontFeatureTable.glyphPairAdjustmentRecords.AddRange(pairs.Values);
            font.ReadFontAssetDefinition();
            BoRussianPlugin.ModLog.LogInfo($"Метрики {font.name} → {reference.name}: естественные пропорции и интервалы; единый масштаб {scale:F6}; собственный кернинг {pairs.Count} пар; дополнительный шаг 0.");
        }

        private static void AddPair(Dictionary<ulong, TMP_GlyphPairAdjustmentRecord> pairs, uint first, uint second, TMP_GlyphValueRecord a, TMP_GlyphValueRecord b)
        {
            pairs[((ulong)second << 32) | first] = new TMP_GlyphPairAdjustmentRecord(new TMP_GlyphAdjustmentRecord(first, a), new TMP_GlyphAdjustmentRecord(second, b));
        }

        private static float HeightRatio(TMP_FontAsset font, TMP_FontAsset reference, char letter, float units)
        {
            return reference.characterLookupTable[letter].glyph.metrics.height * units / font.characterLookupTable[letter].glyph.metrics.height;
        }

        private static float WidthRatio(TMP_FontAsset font, TMP_FontAsset reference, char first, char last, float units)
        {
            var ratios = new List<float>();
            for (uint code = first; code <= last; code++)
            {
                TMP_Character a, b;
                if (reference.characterLookupTable.TryGetValue(code, out a) && font.characterLookupTable.TryGetValue(code, out b)
                    && a.glyph.metrics.horizontalAdvance > 0f && b.glyph.metrics.horizontalAdvance > 0f)
                    ratios.Add(a.glyph.metrics.horizontalAdvance * units / b.glyph.metrics.horizontalAdvance);
            }
            ratios.Sort();
            if (ratios.Count == 0) return 1f;
            int half = ratios.Count / 2;
            return ratios.Count % 2 == 0 ? (ratios[half - 1] + ratios[half]) * 0.5f : ratios[half];
        }
    }
}
