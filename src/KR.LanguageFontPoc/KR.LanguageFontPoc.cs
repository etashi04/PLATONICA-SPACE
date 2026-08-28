using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.Bindings;
using Il2CppInterop.Runtime;
using app;

namespace KR.LanguageFontPoc
{
    [BepInPlugin("kr.platonicaspace.languagefontpoc", "PLATONICA SPACE Korean Runtime Patch", "0.2.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static FontAsset KoreanFont;
        internal static AssetBundle FontBundle;
        internal static ManualLogSource PocLog;
        internal static readonly Dictionary<string, List<TextPatchEntry>> TextPatches = new Dictionary<string, List<TextPatchEntry>>();
        internal static readonly HashSet<string> AppliedTextAssets = new HashSet<string>();
        internal static readonly Dictionary<string, MessagePatchEntry> MessageTranslations = new Dictionary<string, MessagePatchEntry>();
        internal static readonly Dictionary<string, string> DisplayTranslations = new Dictionary<string, string>();
        internal static readonly Dictionary<string, string> SelectionTranslations = new Dictionary<string, string>();
        internal static readonly Dictionary<string, string> StoryTitleTranslations = new Dictionary<string, string>();
        internal static readonly Dictionary<string, string> EntityTranslations = new Dictionary<string, string>();
        internal static readonly List<DisplayTemplateEntry> DisplayTemplates = new List<DisplayTemplateEntry>();
        internal static readonly Dictionary<char, List<KeyValuePair<string, string>>> JapaneseDisplayTranslations = new Dictionary<char, List<KeyValuePair<string, string>>>();
        internal static readonly Dictionary<char, List<DisplayTemplateEntry>> JapaneseDisplayTemplates = new Dictionary<char, List<DisplayTemplateEntry>>();
        internal static readonly Dictionary<string, string> DisplayFragmentTranslations = new Dictionary<string, string>();
        internal static readonly Dictionary<string, string> CombinedDisplayCache = new Dictionary<string, string>();
        internal static readonly Regex CharacterTokenPattern = new Regex(@"<(?:chesca|elara|ivan|katrina|kaya|kushana|larry|liam|nerissa|nora|sana|walter|zoe)>");

        public override void Load()
        {
            PocLog = Log;
            var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var bundlePath = Path.Combine(pluginDirectory, "kr_poc_font_textcore.bundle");
            FontBundle = LoadBundle(bundlePath);
            if (FontBundle == null)
                throw new InvalidOperationException("Cannot load Korean font bundle: " + bundlePath);

            KoreanFont = LoadFontAsset(FontBundle, "assets/generated/kr_notosans_textcore.asset");
            if (KoreanFont == null)
                throw new InvalidOperationException("Cannot load KR_NotoSans_TextCore from font bundle.");

            LoadTextPatches(pluginDirectory);
            Harmony.CreateAndPatchAll(typeof(TextAssetTextPatch));
            Harmony.CreateAndPatchAll(typeof(MessageGetPatch));
            Harmony.CreateAndPatchAll(typeof(ScenarioTextPatch));
            Harmony.CreateAndPatchAll(typeof(ScenarioParagraphPatch));
            Harmony.CreateAndPatchAll(typeof(ScenarioSelectionGroupPatch));
            Harmony.CreateAndPatchAll(typeof(ScenarioSelectionPatch));
            Harmony.CreateAndPatchAll(typeof(ChoiceOpenPatch));
            Harmony.CreateAndPatchAll(typeof(DetermineOpenPatch));
            Harmony.CreateAndPatchAll(typeof(DetermineLogPatch));
            Harmony.CreateAndPatchAll(typeof(TextLogHolderAddPatch));
            Harmony.CreateAndPatchAll(typeof(TextLogElementAddPatch));
            Harmony.CreateAndPatchAll(typeof(InventoryDescriptionPatch));
            Harmony.CreateAndPatchAll(typeof(InventoryTokenPatch));
            Harmony.CreateAndPatchAll(typeof(TextElementTextPatch));
            Log.LogInfo("KR_PATCH_READY font=" + KoreanFont.name + " assets=" + TextPatches.Count +
                " messages=" + MessageTranslations.Count + " display=" + DisplayTranslations.Count +
                " fragments=" + DisplayFragmentTranslations.Count + " wrap=unity-default bundle=" + bundlePath);
        }

        private static unsafe AssetBundle LoadBundle(string path)
        {
            fixed (char* characters = path)
            {
                var span = new ManagedSpanWrapper(characters, path.Length);
                var pointer = AssetBundle.LoadFromFile_Internal_Injected(ref span, 0, 0);
                return Unmarshal.UnmarshalUnityObject<AssetBundle>(pointer);
            }
        }

        private static unsafe FontAsset LoadFontAsset(AssetBundle bundle, string assetName)
        {
            return bundle.LoadAsset<FontAsset>(assetName);
        }

        private static void LoadTextPatches(string pluginDirectory)
        {
            var dataDirectory = Path.Combine(pluginDirectory, "data");
            var manifestPath = Path.Combine(dataDirectory, "runtime-text-patch-manifest.tsv");
            foreach (var line in File.ReadAllLines(manifestPath, Encoding.UTF8).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = line.Split('\t');
                if (fields.Length != 6) throw new InvalidOperationException("Invalid text patch manifest row: " + line);
                var entry = new TextPatchEntry
                {
                    AssetName = fields[0], SourceSha256 = fields[2],
                    PatchedText = File.ReadAllText(Path.Combine(dataDirectory, fields[5]), Encoding.UTF8)
                };
                List<TextPatchEntry> list;
                if (!TextPatches.TryGetValue(entry.AssetName, out list))
                {
                    list = new List<TextPatchEntry>();
                    TextPatches.Add(entry.AssetName, list);
                }
                list.Add(entry);
            }
            LoadMessageMap(Path.Combine(dataDirectory, "runtime-message-map.tsv"));
            LoadMap(Path.Combine(dataDirectory, "runtime-display-map.tsv"), DisplayTranslations);
            LoadMap(Path.Combine(dataDirectory, "runtime-selection-map.tsv"), SelectionTranslations);
            LoadMap(Path.Combine(dataDirectory, "runtime-story-title-map.tsv"), StoryTitleTranslations);
            LoadMap(Path.Combine(dataDirectory, "runtime-entity-map.tsv"), EntityTranslations);
            AddMissingKeywordTranslations();
            ExpandRenderedCharacterTokenEntries();
            foreach (var pair in DisplayTranslations)
            {
                var japanese = FirstJapaneseCharacter(pair.Key);
                AddJapaneseTranslation(pair.Key, pair.Value);
                var template = DisplayTemplateEntry.Create(pair.Key, pair.Value);
                if (template != null)
                {
                    DisplayTemplates.Add(template);
                    if (japanese.HasValue)
                    {
                        List<DisplayTemplateEntry> templates;
                        if (!JapaneseDisplayTemplates.TryGetValue(japanese.Value, out templates))
                        {
                            templates = new List<DisplayTemplateEntry>();
                            JapaneseDisplayTemplates[japanese.Value] = templates;
                        }
                        templates.Add(template);
                    }
                }
            }
            BuildDisplayFragmentTranslations();
            foreach (var pair in DisplayFragmentTranslations)
                AddJapaneseTranslation(pair.Key, pair.Value);
            foreach (var list in JapaneseDisplayTranslations.Values)
                list.Sort((left, right) => right.Key.Length.CompareTo(left.Key.Length));
            foreach (var list in JapaneseDisplayTemplates.Values)
                list.Sort((left, right) => right.SourceLength.CompareTo(left.SourceLength));
        }

        private static void AddMissingKeywordTranslations()
        {
            var missing = new Dictionary<string, string>
            {
                { "年齢", "나이" }, { "時代", "시대" }, { "素材", "소재" },
                { "倒れて", "쓰러져" }, { "寝息を立てて", "숨소리를 내며" },
                { "座って", "앉아서" }, { "飼っていた犬", "기르던 개" },
                { "生体", "생체" }, { "機械", "기계" }, { "動物", "동물" },
                { "破壊", "파괴" }, { "爆発", "폭발" }, { "洗濯機", "세탁기" },
                { "新人", "신입" }, { "管理職", "관리직" }, { "無給", "무급" },
                { "音", "소리" }, { "空気", "공기" }, { "テーブル", "테이블" },
                { "瞳", "눈동자" }, { "風", "바람" }, { "匂い", "냄새" },
                { "海", "바다" }, { "持病", "지병" }, { "職場", "직장" },
                { "駅", "역" }, { "上司", "상사" }, { "家族", "가족" },
                { "学生", "학생" }, { "教師", "교사" }, { "スパイ", "스파이" },
                { "基地", "기지" }, { "お父さん", "아버지" },
                { "バイオテロ", "바이오 테러" }, { "戦闘", "전투" },
                { "交通事故", "교통사고" }, { "惑星", "행성" },
                { "ポケット", "주머니" }, { "引き出し", "서랍" },
                { "天井裏", "천장 위" }, { "コンピュータ", "컴퓨터" },
                { "ロケット", "로켓" },
                { "叔母さん", "이모" }, { "飼い猫", "기르던 고양이" },
                { "死体", "시체" }, { "夢", "꿈" },
                { "諜報員", "정보 요원" }, { "敵性組織", "적대 조직" },
                { "惑星外", "행성 밖" }, { "薬剤", "약물" }, { "自動車", "자동차" },
                { "スキャン", "스캔" }, { "盗聴", "도청" }, { "ハッキング", "해킹" },
                { "義足", "의족" }, { "カメラ", "카메라" },
                { "ナノロボット", "나노로봇" }, { "ワクチン", "백신" },
                { "細胞", "세포" }, { "心臓", "심장" }
            };
            foreach (var pair in missing)
                if (!EntityTranslations.ContainsKey(pair.Key)) EntityTranslations[pair.Key] = pair.Value;

            var compositeKeys = new[]
            {
                "年齢/料理/ガスマスク/時代/素材",
                "倒れて/寝息を立てて/座って/父/飼っていた犬",
                "生体/機械/動物/破壊/爆発",
                "新人/管理職/無給/地球/宇宙船",
                "音/空気/テーブル/記憶/瞳",
                "地球/風/匂い/海/宇宙",
                "学校/職場/駅/上司/家族",
                "学生/教師/スパイ/基地/宇宙船",
                "バイオテロ/戦闘/交通事故/惑星/病院",
                "ポケット/引き出し/天井裏/コンピュータ/ロケット",
                "叔母さん/飼い猫/死体/仕事/夢",
                "諜報員/敵性組織/惑星外/薬剤/自動車",
                "スキャン/盗聴/ハッキング/義足/カメラ",
                "ナノロボット/細菌/ワクチン/細胞/心臓"
            };
            foreach (var key in compositeKeys)
            {
                var parts = key.Split('/');
                var translated = parts.Select(part => EntityTranslations.ContainsKey(part) ?
                    EntityTranslations[part] : part).ToArray();
                EntityTranslations[key] = string.Join("/", translated);
            }
        }

        private static void AddJapaneseTranslation(string source, string target)
        {
            var japanese = FirstJapaneseCharacter(source);
            if (!japanese.HasValue) return;
            List<KeyValuePair<string, string>> translations;
            if (!JapaneseDisplayTranslations.TryGetValue(japanese.Value, out translations))
            {
                translations = new List<KeyValuePair<string, string>>();
                JapaneseDisplayTranslations[japanese.Value] = translations;
            }
            translations.Add(new KeyValuePair<string, string>(source, target));
        }

        private static void ExpandRenderedCharacterTokenEntries()
        {
            var renderedEntries = new Dictionary<string, string>();
            foreach (var pair in DisplayTranslations.ToArray())
            {
                var matches = CharacterTokenPattern.Matches(pair.Key);
                if (matches.Count == 0) continue;
                var renderedSource = pair.Key;
                var renderedTarget = pair.Value;
                var complete = true;
                foreach (Match match in matches)
                {
                    var key = match.Value.Substring(1, match.Value.Length - 2);
                    MessagePatchEntry character;
                    if (!MessageTranslations.TryGetValue(key, out character) || string.IsNullOrEmpty(character.Source))
                    {
                        complete = false;
                        break;
                    }
                    renderedSource = renderedSource.Replace(match.Value, character.Source);
                    renderedTarget = renderedTarget.Replace(match.Value, character.Translation);
                }
                if (complete && !DisplayTranslations.ContainsKey(renderedSource))
                    renderedEntries[renderedSource] = renderedTarget;
            }
            foreach (var entry in renderedEntries) DisplayTranslations[entry.Key] = entry.Value;
        }

        private static void LoadMessageMap(string path)
        {
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = line.Split('\t');
                if (fields.Length != 4) throw new InvalidOperationException("Invalid message map row: " + line);
                var decoded = fields.Select(f => Encoding.UTF8.GetString(Convert.FromBase64String(f))).ToArray();
                MessageTranslations[decoded[0]] = new MessagePatchEntry
                { Source = decoded[1], OfficialEnglish = decoded[2], Translation = decoded[3] };
            }
        }

        private static void LoadMap(string path, Dictionary<string, string> target)
        {
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = line.Split('\t');
                if (fields.Length != 2) throw new InvalidOperationException("Invalid runtime map row: " + line);
                target[Encoding.UTF8.GetString(Convert.FromBase64String(fields[0]))] =
                    Encoding.UTF8.GetString(Convert.FromBase64String(fields[1]));
            }
        }

        internal static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        internal static bool TryTranslateDisplay(string source, out string translation)
        {
            if (!string.IsNullOrEmpty(source) && DisplayTranslations.TryGetValue(source, out translation)) return true;
            if (!string.IsNullOrEmpty(source) && StoryTitleTranslations.TryGetValue(source, out translation)) return true;
            if (!string.IsNullOrEmpty(source) && EntityTranslations.TryGetValue(source, out translation)) return true;
            if (!string.IsNullOrEmpty(source) && DisplayFragmentTranslations.TryGetValue(source, out translation)) return true;
            var japanese = FirstJapaneseCharacter(source);
            if (japanese.HasValue)
            {
                var japaneseCharacters = source.Where(c =>
                        (c >= '\u3040' && c <= '\u30ff') || (c >= '\u3400' && c <= '\u9fff') || c == '、' || c == '。')
                    .Distinct().ToArray();
                // Rendered token variants were expanded into the direct map at load time,
                // so the cheap longest-first substring path is both correct and fast.
                foreach (var character in japaneseCharacters)
                {
                    List<KeyValuePair<string, string>> translations;
                    if (!JapaneseDisplayTranslations.TryGetValue(character, out translations)) continue;
                    foreach (var pair in translations)
                    {
                        var index = source.IndexOf(pair.Key, StringComparison.Ordinal);
                        if (index < 0) continue;
                        translation = source.Substring(0, index) + pair.Value + source.Substring(index + pair.Key.Length);
                        return true;
                    }
                }
                // Regex templates are only needed while literal <character> tokens survive.
                // Rendered logs use the pre-expanded direct map and must stay on the fast path.
                if (source.IndexOf('<') >= 0)
                {
                    foreach (var character in japaneseCharacters)
                    {
                        List<DisplayTemplateEntry> templates;
                        if (JapaneseDisplayTemplates.TryGetValue(character, out templates))
                            foreach (var template in templates)
                                if (template.TryApply(source, out translation)) return true;
                    }
                }
            }
            translation = null;
            return false;
        }

        internal static string TranslateAllDisplaySegments(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string cached;
            if (CombinedDisplayCache.TryGetValue(value, out cached)) return cached;
            var original = value;
            var lines = Regex.Split(value, "(\\r?\\n)");
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex += 2)
            {
                var line = lines[lineIndex];
                for (var pass = 0; pass < 64 && FirstJapaneseCharacter(line).HasValue; pass++)
                {
                    string translated;
                    if (!TryTranslateDisplay(line, out translated) || translated == line) break;
                    line = translated;
                }
                lines[lineIndex] = line;
            }
            value = string.Concat(lines);
            CombinedDisplayCache[original] = value;
            return value;
        }

        private static void BuildDisplayFragmentTranslations()
        {
            var candidates = new Dictionary<string, HashSet<string>>();
            foreach (var pair in DisplayTranslations)
            {
                var sourceTokens = CharacterTokenPattern.Matches(pair.Key).Cast<Match>().Select(match => match.Value).ToArray();
                var targetTokens = CharacterTokenPattern.Matches(pair.Value).Cast<Match>().Select(match => match.Value).ToArray();
                if (sourceTokens.Length == 0 || !sourceTokens.SequenceEqual(targetTokens)) continue;
                var sourceParts = CharacterTokenPattern.Split(pair.Key);
                var targetParts = CharacterTokenPattern.Split(pair.Value);
                if (sourceParts.Length != targetParts.Length) continue;
                for (var index = 0; index < sourceParts.Length; index++)
                {
                    var sourcePart = sourceParts[index];
                    var targetPart = targetParts[index];
                    if (string.IsNullOrEmpty(sourcePart) || sourcePart == targetPart || !FirstJapaneseCharacter(sourcePart).HasValue) continue;
                    HashSet<string> values;
                    if (!candidates.TryGetValue(sourcePart, out values))
                    {
                        values = new HashSet<string>();
                        candidates[sourcePart] = values;
                    }
                    values.Add(targetPart);
                }
            }
            foreach (var candidate in candidates)
                if (candidate.Value.Count == 1) DisplayFragmentTranslations[candidate.Key] = candidate.Value.First();
        }

        internal static string NormalizeKoreanDisplay(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.Any(c => c >= '\uAC00' && c <= '\uD7A3'))
            {
                // Explicit line breaks must never carry indentation into the rendered line.
                value = Regex.Replace(value, @"(\r?\n)[ \t\u00A0]+", "$1");
                value = Regex.Replace(value, @"\.+([?!])", "$1");
                value = Regex.Replace(value, @"([?!])\.+", "$1");
                value = value.Replace('、', ',').Replace('，', ',').Replace('。', '.');
            }
            return value;
        }

        private static char? FirstJapaneseCharacter(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            foreach (var c in value)
                if ((c >= '\u3040' && c <= '\u30ff') || (c >= '\u3400' && c <= '\u9fff')) return c;
            return null;
        }

    }

    internal sealed class DisplayTemplateEntry
    {
        private readonly Regex Pattern;
        private readonly string Translation;
        private readonly List<string> Tokens;
        internal readonly int SourceLength;

        private DisplayTemplateEntry(Regex pattern, string translation, List<string> tokens)
        {
            Pattern = pattern;
            Translation = translation;
            Tokens = tokens;
            SourceLength = pattern.ToString().Length;
        }

        internal static DisplayTemplateEntry Create(string source, string translation)
        {
            var matches = Plugin.CharacterTokenPattern.Matches(source ?? "");
            if (matches.Count == 0) return null;
            var pattern = new StringBuilder();
            var tokens = new List<string>();
            var offset = 0;
            for (var index = 0; index < matches.Count; index++)
            {
                var token = matches[index];
                if ((translation ?? "").IndexOf(token.Value, StringComparison.Ordinal) < 0) return null;
                pattern.Append(Regex.Escape(source.Substring(offset, token.Index - offset)));
                pattern.Append("(?<v").Append(index).Append(">.+?)");
                tokens.Add(token.Value);
                offset = token.Index + token.Length;
            }
            pattern.Append(Regex.Escape(source.Substring(offset)));
            return new DisplayTemplateEntry(new Regex(pattern.ToString(), RegexOptions.CultureInvariant), translation, tokens);
        }

        internal bool TryApply(string source, out string result)
        {
            result = null;
            var match = Pattern.Match(source);
            if (!match.Success) return false;
            result = Translation;
            for (var index = 0; index < Tokens.Count; index++)
                result = result.Replace(Tokens[index], match.Groups["v" + index].Value);
            result = source.Substring(0, match.Index) + result + source.Substring(match.Index + match.Length);
            return true;
        }
    }

    [HarmonyPatch(typeof(TextElementController.Text), "setText")]
    internal static class ScenarioTextPatch
    {
        private static void Prefix(ref string text)
        {
            text = Plugin.TranslateAllDisplaySegments(text);
        }
    }

    [HarmonyPatch(typeof(TextElementController.Paragraph), "setText")]
    internal static class ScenarioParagraphPatch
    {
        private static void Prefix(ref string name, ref string text)
        {
            name = Plugin.TranslateAllDisplaySegments(name);
            text = Plugin.TranslateAllDisplaySegments(text);
        }
    }

    [HarmonyPatch(typeof(TextElementController.SelectionGroup), "add")]
    internal static class ScenarioSelectionGroupPatch
    {
        private static void Prefix(ref string text, ref string info)
        {
            Translate(ref text);
            Translate(ref info);
        }

        internal static void Translate(ref string value)
        {
            string translated;
            if (value != null && (Plugin.SelectionTranslations.TryGetValue(value, out translated) ||
                Plugin.DisplayTranslations.TryGetValue(value, out translated))) value = translated;
        }
    }

    [HarmonyPatch(typeof(TextElementController.Selection), "setup")]
    internal static class ScenarioSelectionPatch
    {
        private static void Prefix(ref string text, ref string info)
        {
            ScenarioSelectionGroupPatch.Translate(ref text);
            ScenarioSelectionGroupPatch.Translate(ref info);
        }
    }

    [HarmonyPatch(typeof(DetermineElementController), "moveSelection")]
    internal static class DetermineLogPatch
    {
        private static readonly FieldInfo[] TextFields = typeof(DetermineElementController)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => typeof(UnityEngine.UIElements.TextElement).IsAssignableFrom(field.FieldType))
            .ToArray();

        private static void Postfix(DetermineElementController __instance)
        {
            if (__instance == null) return;
            foreach (var field in TextFields)
            {
                var element = field.GetValue(__instance) as UnityEngine.UIElements.TextElement;
                if (element == null || string.IsNullOrEmpty(element.text)) continue;
                var translated = Plugin.TranslateAllDisplaySegments(element.text);
                if (translated != element.text) element.text = translated;
            }
        }
    }

    [HarmonyPatch(typeof(DetermineElementController), "open")]
    internal static class DetermineOpenPatch
    {
        private static void Prefix(ref Il2CppSystem.Func<int, string> getText)
        {
            if (getText == null) return;
            var original = getText;
            var byIndex = new Dictionary<int, string>();
            getText = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<int, string>>(
                new System.Func<int, string>(index =>
                {
                    string translated;
                    if (byIndex.TryGetValue(index, out translated)) return translated;
                    translated = Plugin.TranslateAllDisplaySegments(original.Invoke(index));
                    byIndex[index] = translated;
                    return translated;
                }));
        }
    }

    [HarmonyPatch(typeof(TextLogHolder), "add")]
    internal static class TextLogHolderAddPatch
    {
        private static void Prefix(ref string name, ref string text)
        {
            name = Plugin.TranslateAllDisplaySegments(name);
            text = Plugin.TranslateAllDisplaySegments(text);
        }
    }

    [HarmonyPatch(typeof(TextLogElementController), "add")]
    internal static class TextLogElementAddPatch
    {
        private static void Prefix(TextLogHolder.Log __0)
        {
            if (__0 == null) return;
            __0.Name = Plugin.TranslateAllDisplaySegments(__0.Name);
            __0.Text = Plugin.TranslateAllDisplaySegments(__0.Text);
        }
    }

    [HarmonyPatch(typeof(InventoryElementController), "setDescription")]
    internal static class InventoryDescriptionPatch
    {
        private static void Prefix(ref string title, ref string text)
        {
            title = Plugin.TranslateAllDisplaySegments(title);
            text = Plugin.TranslateAllDisplaySegments(text);
        }
    }

    [HarmonyPatch(typeof(InventoryElementController), "addToken")]
    internal static class InventoryTokenPatch
    {
        private static void Prefix(ref string text)
        {
            text = Plugin.TranslateAllDisplaySegments(text);
        }
    }

    [HarmonyPatch(typeof(ChoiceElementController), "open")]
    internal static class ChoiceOpenPatch
    {
        private static void Prefix(ref string title, string[] selectionTitles, string[] texts,
            ref string correctMessage)
        {
            title = Plugin.TranslateAllDisplaySegments(title);
            correctMessage = Plugin.TranslateAllDisplaySegments(correctMessage);
            TranslateArray(selectionTitles);
            TranslateArray(texts);
        }

        private static void TranslateArray(string[] values)
        {
            if (values == null) return;
            for (var index = 0; index < values.Length; index++)
                values[index] = Plugin.TranslateAllDisplaySegments(values[index]);
        }
    }

    [HarmonyPatch]
    internal static class MessageGetPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(Message).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "get" && method.GetParameters().Length >= 1 &&
                    method.GetParameters()[0].ParameterType == typeof(string));
        }

        private static void Postfix(string __0, ref string __result)
        {
            if (__0 == "option-language") { __result = "언어"; return; }
            MessagePatchEntry entry;
            if (!Plugin.MessageTranslations.TryGetValue(__0, out entry)) return;
            __result = entry.Apply(__result, __0);
        }
    }

    internal sealed class TextPatchEntry
    {
        internal string AssetName;
        internal string SourceSha256;
        internal string PatchedText;
    }

    internal sealed class MessagePatchEntry
    {
        internal string Source;
        internal string OfficialEnglish;
        internal string Translation;

        internal string Apply(string original, string messageKey)
        {
            if (!Regex.IsMatch(Translation, @"\{\d+\}")) return Translation;
            string result;
            if (TryApply(Source, original, messageKey, out result) || TryApply(OfficialEnglish, original, messageKey, out result)) return result;
            return original;
        }

        private bool TryApply(string template, string original, string messageKey, out string result)
        {
            result = null;
            if (string.IsNullOrEmpty(template)) return false;
            var tokens = Regex.Matches(template, @"\{(\d+)\}");
            if (tokens.Count == 0) return false;
            var pattern = new StringBuilder("^");
            var offset = 0;
            foreach (Match token in tokens)
            {
                pattern.Append(Regex.Escape(template.Substring(offset, token.Index - offset)));
                pattern.Append("(?<p").Append(token.Groups[1].Value).Append(">.*?)");
                offset = token.Index + token.Length;
            }
            pattern.Append(Regex.Escape(template.Substring(offset))).Append("$");
            var match = Regex.Match(original ?? "", pattern.ToString());
            if (!match.Success) return false;
            result = Translation;
            foreach (Match token in Regex.Matches(Translation, @"\{(\d+)\}"))
            {
                var value = match.Groups["p" + token.Groups[1].Value].Value;
                string translatedValue;
                var memoryContext = Regex.IsMatch(messageKey ?? "", "memory|remember|determine|choice");
                if ((memoryContext && Plugin.StoryTitleTranslations.TryGetValue(value, out translatedValue)) ||
                    Plugin.EntityTranslations.TryGetValue(value, out translatedValue) ||
                    Plugin.StoryTitleTranslations.TryGetValue(value, out translatedValue) ||
                    Plugin.DisplayTranslations.TryGetValue(value, out translatedValue) ||
                    Plugin.SelectionTranslations.TryGetValue(value, out translatedValue)) value = translatedValue;
                result = result.Replace(token.Value, value);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(UnityEngine.TextAsset), "get_text")]
    internal static class TextAssetTextPatch
    {
        private static void Postfix(UnityEngine.TextAsset __instance, ref string __result)
        {
            if (__instance == null || __result == null) return;
            List<TextPatchEntry> entries;
            if (!Plugin.TextPatches.TryGetValue(__instance.name, out entries)) return;
            var hash = Plugin.Sha256(__result);
            var entry = entries.FirstOrDefault(e => e.SourceSha256 == hash);
            if (entry == null) return;
            __result = entry.PatchedText;
            var identity = __instance.name + "|" + hash;
            if (Plugin.AppliedTextAssets.Add(identity))
            {
                Plugin.PocLog.LogInfo("KR_TEXT_ASSET_APPLIED name=" + __instance.name + " source_sha256=" + hash);
            }
        }
    }

    [HarmonyPatch]
    internal static class TextElementTextPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertySetter(typeof(UnityEngine.UIElements.TextElement), "text");
        }

        private static void Prefix(UnityEngine.UIElements.TextElement __instance, ref string value)
        {
            string translated;
            if (Plugin.TryTranslateDisplay(value, out translated)) value = translated;
            // Detect semantic UI roles before inserting invisible Korean wrapping controls.
            var determineTitle = !string.IsNullOrEmpty(value) &&
                value.Contains("기억의 조각") && value.Contains("누구의 기억일까");
            var determineResult = !string.IsNullOrEmpty(value) &&
                value.Contains("기억의 조각") && value.Contains("것임을 확정했습니다");
            value = Plugin.NormalizeKoreanDisplay(value);
            if (ContainsHangul(value) && Plugin.KoreanFont != null)
            {
                __instance.style.unityFontDefinition = new StyleFontDefinition(Plugin.KoreanFont);
                var elementName = __instance.name ?? "";
                if (determineTitle || determineResult)
                    __instance.style.unityTextAlign = TextAnchor.MiddleCenter;
                else if (value.Length >= 20 && !Regex.IsMatch(elementName,
                    "title|button|selection|submit|option", RegexOptions.IgnoreCase))
                {
                    __instance.style.unityTextAlign = TextAnchor.MiddleLeft;
                }
            }
        }

        internal static bool ContainsHangul(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (var c in value) if (c >= '\uAC00' && c <= '\uD7A3') return true;
            return false;
        }
    }

}
