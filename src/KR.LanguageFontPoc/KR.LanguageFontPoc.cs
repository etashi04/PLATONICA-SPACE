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
    [BepInPlugin("kr.platonicaspace.languagefontpoc", "PLATONICA SPACE Korean Runtime Patch", "1.0.1")]
    public sealed class Plugin : BasePlugin
    {
        internal static FontAsset KoreanFont;
        internal static AssetBundle FontBundle;
        internal static ManualLogSource PocLog;
        internal static readonly Dictionary<string, List<TextPatchEntry>> TextPatches = new Dictionary<string, List<TextPatchEntry>>();
        internal static readonly Dictionary<string, TextPatchEntry> TextPatchesBySourceHash =
            new Dictionary<string, TextPatchEntry>(StringComparer.OrdinalIgnoreCase);
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
        internal static readonly Dictionary<string, string> DisplayAttemptCache = new Dictionary<string, string>();
        internal static readonly Dictionary<string, string> NormalizedDisplayTranslations =
            new Dictionary<string, string>();
        internal static readonly Dictionary<string, string> JapaneseSentenceFingerprints =
            new Dictionary<string, string>();
        internal static readonly HashSet<string> LoggedResidualJapanese = new HashSet<string>();
        internal static readonly HashSet<string> TileDescriptionTranslations = new HashSet<string>();
        internal static readonly HashSet<string> FixedMenuLabelTranslations = new HashSet<string>();
        internal static readonly Dictionary<string, List<StoryLogLineEntry>> StoryLogLines =
            new Dictionary<string, List<StoryLogLineEntry>>();
        internal static readonly HashSet<string> LoggedTextLogLayouts = new HashSet<string>();
        internal static bool DetermineActive;
        internal static bool TextLogActive;
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
            Harmony.CreateAndPatchAll(typeof(TsvParsePatch));
            Harmony.CreateAndPatchAll(typeof(TextAssetTextPatch));
            Harmony.CreateAndPatchAll(typeof(MessageGetPatch));
            Harmony.CreateAndPatchAll(typeof(ScenarioTextPatch));
            Harmony.CreateAndPatchAll(typeof(ScenarioParagraphPatch));
            Harmony.CreateAndPatchAll(typeof(ScenarioSelectionGroupPatch));
            Harmony.CreateAndPatchAll(typeof(ScenarioSelectionPatch));
            Harmony.CreateAndPatchAll(typeof(ChoiceOpenPatch));
            Harmony.CreateAndPatchAll(typeof(DetermineOpenPatch));
            Harmony.CreateAndPatchAll(typeof(DetermineClosePatch));
            Harmony.CreateAndPatchAll(typeof(TextLogHolderAddPatch));
            Harmony.CreateAndPatchAll(typeof(TextLogElementAddPatch));
            Harmony.CreateAndPatchAll(typeof(TextLogOpenPatch));
            Harmony.CreateAndPatchAll(typeof(TextLogClosePatch));
            Harmony.CreateAndPatchAll(typeof(InventoryDescriptionPatch));
            Harmony.CreateAndPatchAll(typeof(TileInventoryHoverPatch));
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
                TextPatchesBySourceHash[entry.SourceSha256] = entry;
            }
            LoadMessageMap(Path.Combine(dataDirectory, "runtime-message-map.tsv"));
            LoadMap(Path.Combine(dataDirectory, "runtime-display-map.tsv"), DisplayTranslations);
            LoadMap(Path.Combine(dataDirectory, "runtime-selection-map.tsv"), SelectionTranslations);
            LoadMap(Path.Combine(dataDirectory, "runtime-story-title-map.tsv"), StoryTitleTranslations);
            LoadMap(Path.Combine(dataDirectory, "runtime-entity-map.tsv"), EntityTranslations);
            LoadStoryLogLines(dataDirectory);
            AddMissingKeywordTranslations();
            AddMemorySelectionTranslations();
            AddUpdatedMemoryTranslations();
            foreach (var translation in EntityTranslations.Values)
                FixedMenuLabelTranslations.Add(translation);
            ExpandRenderedCharacterTokenEntries();
            ExpandRenderedPunctuationEntries();
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
            BuildSelectionQuestionFragmentTranslations();
            foreach (var pair in DisplayFragmentTranslations)
            {
                AddJapaneseTranslation(pair.Key, pair.Value);
                RegisterDisplayTemplate(pair.Key, pair.Value);
            }
            foreach (var pair in SelectionTranslations)
                AddJapaneseTranslation(pair.Key, pair.Value);
            foreach (var list in JapaneseDisplayTranslations.Values)
                list.Sort((left, right) => right.Key.Length.CompareTo(left.Key.Length));
            foreach (var list in JapaneseDisplayTemplates.Values)
                list.Sort((left, right) => right.SourceLength.CompareTo(left.SourceLength));
            BuildNormalizedDisplayTranslations();
            BuildJapaneseSentenceFingerprints();
            PrewarmDisplayCaches();
        }

        private static string NormalizeDisplayLookup(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsWhiteSpace(character)) continue;
                if (character == '。') builder.Append('.');
                else if (character == '、' || character == '，') builder.Append(',');
                else if (character == '：') builder.Append(':');
                else builder.Append(character);
            }
            return builder.ToString();
        }

        private static void BuildNormalizedDisplayTranslations()
        {
            var conflicts = new HashSet<string>();
            foreach (var pair in DisplayTranslations.Concat(DisplayFragmentTranslations))
            {
                if (!FirstJapaneseCharacter(pair.Key).HasValue) continue;
                var key = NormalizeDisplayLookup(pair.Key);
                string current;
                if (NormalizedDisplayTranslations.TryGetValue(key, out current) && current != pair.Value)
                    conflicts.Add(key);
                else
                    NormalizedDisplayTranslations[key] = pair.Value;
            }
            foreach (var key in conflicts) NormalizedDisplayTranslations.Remove(key);
        }

        private static void BuildJapaneseSentenceFingerprints()
        {
            const int fingerprintLength = 4;
            var conflicts = new HashSet<string>();
            foreach (var pair in DisplayTranslations)
            {
                if (!FirstJapaneseCharacter(pair.Key).HasValue) continue;
                // Dynamic UI templates and character/color tokens must be handled by the
                // dedicated template matcher so their values and closing tags survive.
                if (pair.Key.IndexOf('{') >= 0 || pair.Value.IndexOf('{') >= 0 ||
                    pair.Key.IndexOf('<') >= 0 || pair.Value.IndexOf('<') >= 0) continue;
                var source = NormalizeDisplayLookup(CharacterTokenPattern.Replace(pair.Key, ""));
                if (source.Length < fingerprintLength) continue;
                for (var index = 0; index <= source.Length - fingerprintLength; index++)
                {
                    var fingerprint = source.Substring(index, fingerprintLength);
                    if (fingerprint.Count(character =>
                        (character >= '\u3040' && character <= '\u30ff') ||
                        (character >= '\u3400' && character <= '\u9fff')) < 3) continue;
                    string current;
                    if (JapaneseSentenceFingerprints.TryGetValue(fingerprint, out current) && current != pair.Value)
                        conflicts.Add(fingerprint);
                    else
                        JapaneseSentenceFingerprints[fingerprint] = pair.Value;
                }
            }
            foreach (var fingerprint in conflicts) JapaneseSentenceFingerprints.Remove(fingerprint);
        }

        private static bool TryRepairPartiallyTranslatedLine(string line, out string repaired)
        {
            repaired = null;
            const int fingerprintLength = 4;
            var normalized = NormalizeDisplayLookup(line);
            if (normalized.Length < fingerprintLength) return false;
            string candidate = null;
            for (var index = 0; index <= normalized.Length - fingerprintLength; index++)
            {
                string translation;
                if (!JapaneseSentenceFingerprints.TryGetValue(
                    normalized.Substring(index, fingerprintLength), out translation)) continue;
                if (candidate != null && candidate != translation) return false;
                candidate = translation;
            }
            if (candidate == null) return false;
            var colon = line.IndexOf(':');
            if (colon < 0) colon = line.IndexOf('：');
            repaired = colon >= 0 && colon < 40
                ? line.Substring(0, colon + 1).TrimEnd() + " " + candidate
                : candidate;
            return true;
        }

        private static void RegisterDisplayTemplate(string source, string translation)
        {
            var template = DisplayTemplateEntry.Create(source, translation);
            if (template == null) return;
            DisplayTemplates.Add(template);
            var japanese = FirstJapaneseCharacter(source);
            if (!japanese.HasValue) return;
            List<DisplayTemplateEntry> templates;
            if (!JapaneseDisplayTemplates.TryGetValue(japanese.Value, out templates))
            {
                templates = new List<DisplayTemplateEntry>();
                JapaneseDisplayTemplates[japanese.Value] = templates;
            }
            templates.Add(template);
        }

        private static void PrewarmDisplayCaches()
        {
            // Memory logs can contain hundreds of elements. Exact strings account for almost
            // all of them, so resolve those once during plug-in load instead of on menu open.
            foreach (var pair in DisplayTranslations)
            {
                DisplayAttemptCache[pair.Key] = pair.Value;
                CombinedDisplayCache[pair.Key] = pair.Value;
            }
            foreach (var pair in StoryTitleTranslations)
            {
                DisplayAttemptCache[pair.Key] = pair.Value;
                CombinedDisplayCache[pair.Key] = pair.Value;
            }
            foreach (var pair in EntityTranslations)
            {
                DisplayAttemptCache[pair.Key] = pair.Value;
                CombinedDisplayCache[pair.Key] = pair.Value;
            }
            foreach (var pair in SelectionTranslations)
            {
                DisplayAttemptCache[pair.Key] = pair.Value;
                CombinedDisplayCache[pair.Key] = pair.Value;
            }
        }

        private static void AddMemorySelectionTranslations()
        {
            var entries = new[]
            {
                new KeyValuePair<string, string>("ドーナツ", "도넛"),
                new KeyValuePair<string, string>("食事", "식사"),
                new KeyValuePair<string, string>("地球", "지구"),
                new KeyValuePair<string, string>("文化", "문화"),
                new KeyValuePair<string, string>("学校/職場/駅/上司/家族", "학교/직장/역/상사/가족"),
                new KeyValuePair<string, string>("学生/教師/スパイ/基地/宇宙船", "학생/교사/스파이/기지/우주선"),
                new KeyValuePair<string, string>("クラスター", "클러스터"),
                new KeyValuePair<string, string>("不和", "불화"),
                new KeyValuePair<string, string>("資源", "자원"),
                new KeyValuePair<string, string>("祖母", "할머니"),
                new KeyValuePair<string, string>("細菌", "세균"),
                new KeyValuePair<string, string>("冷凍睡眠", "동면"),
                new KeyValuePair<string, string>("廃人", "폐인"),
                new KeyValuePair<string, string>("地球/風/匂い/海/宇宙", "지구/바람/냄새/바다/우주"),
                new KeyValuePair<string, string>("仕事", "일"),
                new KeyValuePair<string, string>("老朽化", "노후화"),
                new KeyValuePair<string, string>("倒れて/寝息を立てて/座って/父/飼っていた犬", "쓰러져/숨소리를 내며 자고/앉아/아버지/키우던 개"),
                new KeyValuePair<string, string>("翻訳", "번역"),
                new KeyValuePair<string, string>("星庁", "성청"),
                new KeyValuePair<string, string>("ドーム", "돔"),
                new KeyValuePair<string, string>("適応処置", "적응 처치"),
                new KeyValuePair<string, string>("ポケット/引き出し/天井裏/コンピュータ/ロケット", "주머니/서랍/다락/컴퓨터/로켓"),
                new KeyValuePair<string, string>("写真", "사진"),
                new KeyValuePair<string, string>("移民", "이민자"),
                new KeyValuePair<string, string>("叔母さん/飼い猫/死体/仕事/夢", "이모/고양이/시체/일/꿈"),
                new KeyValuePair<string, string>("耳", "귀"),
                new KeyValuePair<string, string>("暇つぶし", "시간 때우기"),
                new KeyValuePair<string, string>("年齢/料理/ガスマスク/時代/素材", "나이/요리/방독면/시대/소재"),
                new KeyValuePair<string, string>("IDカード", "신분증"),
                new KeyValuePair<string, string>("星", "행성"),
                new KeyValuePair<string, string>("手術", "수술"),
                new KeyValuePair<string, string>("離別", "이별"),
                new KeyValuePair<string, string>("視線", "시선"),
                new KeyValuePair<string, string>("地球化", "테라포밍"),
                new KeyValuePair<string, string>("花畑", "꽃밭"),
                new KeyValuePair<string, string>("コーヒー", "커피"),
                new KeyValuePair<string, string>("料理", "요리"),
                new KeyValuePair<string, string>("諜報員/敵性組織/惑星外/薬剤/自動車", "첩보원/적대 조직/행성 밖/약물/자동차"),
                new KeyValuePair<string, string>("新人/管理職/無給/地球/宇宙船", "신입/관리직/무급/지구/우주선"),
                new KeyValuePair<string, string>("スキャン/盗聴/ハッキング/義足/カメラ", "스캔/도청/해킹/의족/카메라"),
                new KeyValuePair<string, string>("テロ", "테러"),
                new KeyValuePair<string, string>("バイオテロ/戦闘/交通事故/惑星/病院", "바이오 테러/전투/교통사고/행성/병원"),
                new KeyValuePair<string, string>("妊娠", "임신"),
                new KeyValuePair<string, string>("終着点", "종착점"),
                new KeyValuePair<string, string>("武力衝突", "무력 충돌"),
                new KeyValuePair<string, string>("諜報員", "첩보원"),
                new KeyValuePair<string, string>("お父さん", "아버지"),
                new KeyValuePair<string, string>("配偶者", "배우자"),
                new KeyValuePair<string, string>("勉強", "공부"),
                new KeyValuePair<string, string>("密告", "밀고"),
                new KeyValuePair<string, string>("鉢植え", "화분"),
                new KeyValuePair<string, string>("チャイム", "초인종"),
                new KeyValuePair<string, string>("移住", "이주"),
                new KeyValuePair<string, string>("暗号", "암호"),
                new KeyValuePair<string, string>("音/空気/テーブル/記憶/瞳", "소리/공기/탁자/기억/눈동자"),
                new KeyValuePair<string, string>("炎", "불꽃"),
                new KeyValuePair<string, string>("生体/機械/動物/破壊/爆発", "생체/기계/동물/파괴/폭발"),
                new KeyValuePair<string, string>("命", "생명"),
                new KeyValuePair<string, string>("偽物", "가짜"),
                new KeyValuePair<string, string>("故障", "고장"),
                new KeyValuePair<string, string>("責任", "책임"),
                new KeyValuePair<string, string>("断絶", "단절"),
                new KeyValuePair<string, string>("学校", "학교"),
                new KeyValuePair<string, string>("コックピット", "조종석"),
                new KeyValuePair<string, string>("洗濯機", "세탁기"),
                new KeyValuePair<string, string>("持病", "지병"),
                new KeyValuePair<string, string>("チケット", "티켓"),
                new KeyValuePair<string, string>("静止衛星", "정지 위성"),
                new KeyValuePair<string, string>("安全装置", "안전장치"),
                new KeyValuePair<string, string>("記憶", "기억"),
                new KeyValuePair<string, string>("立ち入り禁止", "출입 금지"),
                new KeyValuePair<string, string>("ナノロボット/細菌/ワクチン/細胞/心臓", "나노로봇/세균/백신/세포/심장")
            };

            foreach (var entry in entries)
            {
                SelectionTranslations[entry.Key] = entry.Value;
                var sources = entry.Key.Split('/');
                var targets = entry.Value.Split('/');
                if (sources.Length != targets.Length) continue;
                for (var index = 0; index < sources.Length; index++)
                    if (!SelectionTranslations.ContainsKey(sources[index]))
                        SelectionTranslations[sources[index]] = targets[index];
            }

            // The four drag-to-fill memory questions render their prefix and suffix as
            // separate SelectionGroup strings. Keep these reviewed fragments explicit;
            // they cannot reliably be recovered from the complete sentence at runtime.
            SelectionTranslations["諜報員"] = "정보원";
            DisplayFragmentTranslations["さっきまで"] = "아까까지 ";
            DisplayFragmentTranslations["の葬儀だったんだ"] = " 장례식이었어.";
            DisplayFragmentTranslations["から送られてきた写真がある。倉庫に兵器の部品が運び込まれている。"] =
                "에게 받은 사진이 있다. 창고로 무기 부품이 반입되는 모습이 찍혀 있다.";
            DisplayFragmentTranslations["悟られないように、<katrina>は義眼で"] =
                "눈치채지 못하게 <katrina>는 의안으로 ";
            DisplayFragmentTranslations["した。"] = "했다.";
            DisplayFragmentTranslations["注入した"] = "주입된 ";
            DisplayFragmentTranslations["が血液の流れに乗って脳に侵入する。"] =
                "이 혈류를 타고 뇌로 침입한다.";
        }

        private static void AddUpdatedMemoryTranslations()
        {
            // The August 2026 game update replaced parts of the memory assets while
            // retaining the same asset names. Keep the new reviewed lines available to
            // both live playback and menu logs; character tokens are handled by templates.
            var entries = new[]
            {
                new KeyValuePair<string, string>("<elara>はかつて自分たちが住んでいた場所を探した。", "<elara>는 예전에 자신들이 살던 곳을 찾았다."),
                new KeyValuePair<string, string>("でも、この空の上からでは判断がつかなかった。", "하지만 이 하늘 위에서는 분간할 수 없었다."),
                new KeyValuePair<string, string>("そしてそこには、もう<elara>が知っている景色はないだろう。", "그리고 그곳에는 이제 <elara>가 알던 풍경이 남아 있지 않을 것이다."),
                new KeyValuePair<string, string>("<elara>たちが地球を出ることが決まったあと、しばらくの間、生まれ育った土地を離れてシェルターで暮らした。宇宙船を待たなければならなかった。", "<elara> 일행은 지구를 떠나기로 한 뒤 한동안 고향을 떠나 대피소에서 살았다. 우주선을 기다려야 했기 때문이다."),
                new KeyValuePair<string, string>("その間、<elara>は一度だけ、かつて住んでいた家をこっそり見に行った。", "그동안 <elara>는 딱 한 번 몰래 예전에 살던 집을 찾아갔다."),
                new KeyValuePair<string, string>("けれど──", "하지만──"),
                new KeyValuePair<string, string>("そこはもう、彼女の知っている場所ではなかった。", "그곳은 더 이상 그녀가 알던 장소가 아니었다."),
                new KeyValuePair<string, string>("土地を買い取った人々が家を壊し、畑を潰して何か別のものにつくり変えようとしていた。", "땅을 사들인 사람들이 집을 허물고 밭을 밀어 다른 무언가로 바꾸려 하고 있었다."),
                new KeyValuePair<string, string>("それが何なのか<elara>にはわからない。彼らは地球の外から戻ってきた人々で、<elara>たちとは少し違った生活をしていた。", "그것이 무엇인지는 <elara>도 알 수 없었다. 그들은 지구 밖에서 돌아온 사람들이었고, <elara> 일행과는 조금 다른 방식으로 살았다."),
                new KeyValuePair<string, string>("<elara>の生まれ育った場所はなくなってしまった。", "<elara>가 태어나 자란 곳은 사라지고 말았다."),
                new KeyValuePair<string, string>("そのときに感じた強い孤独を、<elara>はよく憶えている。", "그때 느꼈던 깊은 고독을 <elara>는 또렷이 기억한다."),
                new KeyValuePair<string, string>("つい何日か前まで大切だったはずの場所に、今、どんな愛着も抱けなかった。", "불과 며칠 전까지 소중했던 곳인데, 이제는 아무런 애착도 느끼지 못했다."),
                new KeyValuePair<string, string>("ショックで、何もかもがどうでもよくなった。涙も出なかった。", "충격에 모든 것이 아무래도 좋아졌다. 눈물조차 나오지 않았다."),
                new KeyValuePair<string, string>("見下ろすこの青い星を、今は故郷だと思える。", "지금은 내려다보이는 이 푸른 행성을 고향이라 여길 수 있다."),
                new KeyValuePair<string, string>("でも、いつか、そうではなくなってしまうのだろうか。", "하지만 언젠가는 그렇지 않게 되는 걸까."),
                new KeyValuePair<string, string>("地球を出て行った人々は、みんな、こんな孤独の中を生きているのだろうか。", "지구를 떠난 사람들은 모두 이런 고독 속에서 살아가는 걸까."),
                new KeyValuePair<string, string>("いつか地球が地球でなくなってしまったら──", "언젠가 지구가 더 이상 지구가 아니게 된다면──"),
                new KeyValuePair<string, string>("そのときのことを、<elara>は想像することさえできない。", "그때의 일을 <elara>는 상상조차 할 수 없다."),
                new KeyValuePair<string, string>("廊下の隅で、職員が一人、血を流して倒れている。", "복도 구석에는 직원 한 명이 피를 흘리며 쓰러져 있다."),
                new KeyValuePair<string, string>("<elara>が人を撃ったのは初めてだった。", "<elara>가 사람을 쏜 것은 처음이었다."),
                new KeyValuePair<string, string>("これからどれだけの人が命を落とすだろう。", "앞으로 얼마나 많은 사람이 목숨을 잃게 될까."),
                new KeyValuePair<string, string>("声明も事前の警告によって、ほとんどの人は地球を脱出した。", "성명과 사전 경고 덕분에 대부분의 사람은 지구를 탈출했다."),
                new KeyValuePair<string, string>("しかし警告を信じない者もいた。手遅れになる前に、あとどれだけの人が地球を脱出できるだろう。", "하지만 경고를 믿지 않은 사람도 있었다. 늦기 전에 앞으로 얼마나 더 많은 사람이 지구를 탈출할 수 있을까."),
                new KeyValuePair<string, string>("発砲", "총격"),
                new KeyValuePair<string, string>("テロリストからの事前の警告によりほとんどの人は地球を脱出したが、数千人が逃げ遅れたと言われている。", "테러리스트의 사전 경고로 대부분의 사람은 지구를 탈출했지만, 수천 명이 미처 빠져나오지 못했다고 한다."),
                new KeyValuePair<string, string>("地球に行く老婆", "지구로 가는 노파"),
                new KeyValuePair<string, string>("ただのテロリスト", "그저 테러리스트일 뿐이야"),
                new KeyValuePair<string, string>("たくさんの人が地球を追い出されて", "수많은 사람이 지구에서 쫓겨났고"),
                new KeyValuePair<string, string>("私たちも、遠くに逃げた", "우리도 멀리 도망쳤어"),
                new KeyValuePair<string, string>("本当に、そのためにあれだけのことを……？", "정말, 그것 때문에 그런 짓까지 한 거야……?"),
                new KeyValuePair<string, string>("あれが、私たちが最後に選んだ手段だった", "그게 우리가 마지막으로 택한 수단이었어"),
                new KeyValuePair<string, string>("数えきれないくらいの人の故郷を奪った", "셀 수도 없이 많은 사람의 고향을 빼앗았어"),
                new KeyValuePair<string, string>("四人が去り、残った記憶が自分のものだった。", "네 사람이 떠나고, 남은 기억은 자신의 것이었다."),
                new KeyValuePair<string, string>("私は、地球に人が住めないようにした", "나는 지구에서 사람이 살 수 없게 만들었다"),
                new KeyValuePair<string, string>("リアムにはわかってもらえないかもしれないけれど", "리암은 이해해 주지 못할지도 모르지만"),
                new KeyValuePair<string, string>("あのとき逃げ遅れて死んだ人だって、たくさんいる", "그때 미처 도망치지 못해 죽은 사람도 많이 있어"),
                new KeyValuePair<string, string>("それでも為さねばならなかった。", "그래도 해야만 했다.")
            };
            foreach (var entry in entries) DisplayTranslations[entry.Key] = entry.Value;
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

            // Repair fragments for logs persisted by older partial-translation builds.
            DisplayFragmentTranslations["彼女の横顔が"] = "그녀의 옆얼굴이";
            DisplayFragmentTranslations["あまりにも寂し"] = "너무나 쓸쓸해";
            DisplayFragmentTranslations["そうだったから"] = "보였기 때문이다";
            DisplayFragmentTranslations["そして"] = "그리고";
            DisplayFragmentTranslations["言う。"] = "말한다.";
            DisplayFragmentTranslations["は思った。"] = "는 생각했다.";
            DisplayFragmentTranslations["思った。"] = "생각했다.";
            DisplayFragmentTranslations["の片足は"] = "의 한쪽 다리는";
            DisplayFragmentTranslations["安価な機械の義足になった。"] = "저렴한 기계식 의족이 되었다.";
            DisplayFragmentTranslations["にもわかった。"] = "도 알았다.";
            DisplayFragmentTranslations["にはまだわからない。"] = "는 아직 잘 모르겠다.";
            DisplayFragmentTranslations["が言った。"] = "가 말했다.";
            DisplayFragmentTranslations["それでも、"] = "그래도,";
            DisplayFragmentTranslations["は言う。"] = "는 말했다.";
            DisplayFragmentTranslations["は自慢げに言う。"] = "는 자랑스럽다는 듯 말했다.";
            DisplayFragmentTranslations["は好きだった。"] = "는 좋아했다.";
            DisplayFragmentTranslations["テロリストからの事前の警告により"] = "테러리스트의 사전 경고 덕분에";
            DisplayFragmentTranslations["ほとんどの人は地球を脱出したが"] = "대부분의 사람은 지구를 탈출했지만";
            DisplayFragmentTranslations["数千人が逃げ遅れたと言われている。"] = "수천 명이 미처 탈출하지 못했다고 한다.";
            DisplayFragmentTranslations["数千人が逃げ遅れた"] = "수천 명이 미처 탈출하지 못했다";
            DisplayFragmentTranslations["と言われている。"] = "고 한다.";

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
        private static void ExpandRenderedPunctuationEntries()
        {
            // Text logs normalize Japanese punctuation before the controller receives the
            // string (for example, "、" becomes ","). Keep exact lookup variants for that
            // rendered form so complete memory-log sentences are still translated.
            var renderedEntries = new Dictionary<string, string>();
            foreach (var pair in DisplayTranslations.ToArray())
            {
                var renderedSource = pair.Key.Replace('、', ',').Replace('，', ',')
                    .Replace('。', '.').Replace('！', '!').Replace('？', '?');
                if (renderedSource != pair.Key && !DisplayTranslations.ContainsKey(renderedSource))
                    renderedEntries[renderedSource] = pair.Value;
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
                if (decoded[0].StartsWith("tile-", StringComparison.Ordinal) &&
                    decoded[0].EndsWith("description", StringComparison.Ordinal))
                    TileDescriptionTranslations.Add(decoded[3]);
            }
        }

        private static void LoadStoryLogLines(string dataDirectory)
        {
            foreach (var path in Directory.GetFiles(dataDirectory, "Memory*.txt"))
            {
                List<StoryLogLineEntry> current = null;
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var fields = line.Split('\t');
                    if (fields.Length >= 2 && fields[0] == "@story")
                    {
                        if (!StoryLogLines.TryGetValue(fields[1], out current))
                        {
                            current = new List<StoryLogLineEntry>();
                            StoryLogLines[fields[1]] = current;
                        }
                        continue;
                    }
                    if (current == null || fields.Length < 2 || fields[0].StartsWith("@") ||
                        fields[0].StartsWith("###@") || string.IsNullOrWhiteSpace(fields[1])) continue;
                    current.Add(new StoryLogLineEntry(fields[0], fields[1]));
                }
            }
        }

        internal static string RepairPersistedStoryLog(string name, string text)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(text) ||
                !FirstJapaneseCharacter(text).HasValue) return text;
            List<StoryLogLineEntry> targetLines;
            if (!StoryLogLines.TryGetValue(name, out targetLines)) return text;
            var parts = Regex.Split(text, "(<br\\s*/?>|\\r?\\n)", RegexOptions.IgnoreCase);
            var contentIndex = 0;
            for (var index = 0; index < parts.Length; index += 2)
            {
                if (contentIndex >= targetLines.Count) break;
                var currentLine = parts[index];
                if (FirstJapaneseCharacter(currentLine).HasValue)
                    parts[index] = targetLines[contentIndex].Render(currentLine, text);
                contentIndex++;
            }
            return string.Concat(parts);
        }

        internal static void LogTextLogLayout(UnityEngine.UIElements.TextElement element, string value)
        {
            if (!TextLogActive || element == null || !FixedMenuLabelTranslations.Contains(value)) return;
            var chain = new StringBuilder();
            var current = element as UnityEngine.UIElements.VisualElement;
            for (var depth = 0; current != null && depth < 6; depth++, current = current.parent)
            {
                if (depth > 0) chain.Append(" <- ");
                chain.Append(current.GetType().Name).Append('(').Append(current.name ?? "").Append(')');
            }
            var identity = value + "|" + chain;
            if (LoggedTextLogLayouts.Add(identity))
                PocLog.LogInfo("KR_TEXTLOG_LAYOUT value=" + value + " chain=" + chain);
        }

        internal static void CenterCompletedTextLogChoices(TextLogElementController controller)
        {
            if (controller == null) return;
            foreach (var field in typeof(TextLogElementController).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(UnityEngine.UIElements.VisualElement).IsAssignableFrom(field.FieldType)) continue;
                var root = field.GetValue(controller) as UnityEngine.UIElements.VisualElement;
                if (root != null) CenterTextLogChoiceTree(root);
            }
        }

        private static void CenterTextLogChoiceTree(UnityEngine.UIElements.VisualElement element)
        {
            var textElement = element as UnityEngine.UIElements.TextElement;
            if (textElement != null)
            {
                var plainValue = Regex.Replace(textElement.text ?? "", "<[^>]+>", "");
                if (FixedMenuLabelTranslations.Contains(plainValue) && HasButtonAncestor(textElement))
                {
                    textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
                    textElement.style.paddingLeft = 0f;
                    textElement.style.paddingRight = 0f;
                    textElement.style.marginLeft = 0f;
                    textElement.style.marginRight = 0f;
                    if (!(textElement is UnityEngine.UIElements.Button))
                    {
                        textElement.style.position = UnityEngine.UIElements.Position.Absolute;
                        textElement.style.left = 0f;
                        textElement.style.right = 0f;
                        textElement.style.top = 0f;
                        textElement.style.bottom = 0f;
                    }
                }
            }
            for (var index = 0; index < element.childCount; index++)
                CenterTextLogChoiceTree(element.ElementAt(index));
        }

        private static bool HasButtonAncestor(UnityEngine.UIElements.VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current is UnityEngine.UIElements.Button) return true;
            return false;
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

        internal static string PatchTsvSource(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            var hash = Sha256(source);
            TextPatchEntry entry;
            if (!TextPatchesBySourceHash.TryGetValue(hash, out entry)) return source;
            var identity = "tsv|" + entry.AssetName + "|" + hash;
            if (AppliedTextAssets.Add(identity))
                PocLog.LogInfo("KR_TSV_PATCH_APPLIED name=" + entry.AssetName + " source_sha256=" + hash);
            return entry.PatchedText;
        }

        internal static bool TryTranslateDisplay(string source, out string translation)
        {
            if (string.IsNullOrEmpty(source)) { translation = null; return false; }
            if (DisplayAttemptCache.TryGetValue(source, out translation)) return translation != null;
            if (DisplayTranslations.TryGetValue(source, out translation) ||
                SelectionTranslations.TryGetValue(source, out translation) ||
                StoryTitleTranslations.TryGetValue(source, out translation) ||
                EntityTranslations.TryGetValue(source, out translation) ||
                DisplayFragmentTranslations.TryGetValue(source, out translation))
            {
                DisplayAttemptCache[source] = translation;
                return true;
            }
            var japanese = FirstJapaneseCharacter(source);
            if (japanese.HasValue)
            {
                var japaneseCharacters = source.Where(c =>
                        (c >= '\u3040' && c <= '\u30ff') || (c >= '\u3400' && c <= '\u9fff') || c == '、' || c == '。')
                    .Distinct().ToArray();
                // Character tokens such as <elara> are replaced with masked glyphs before
                // some memory playback controllers assign the text. Try the reviewed full
                // sentence templates before any substring replacement, regardless of whether
                // the literal token survives. This prevents mixed Korean/Japanese sentences.
                foreach (var character in japaneseCharacters)
                {
                    List<DisplayTemplateEntry> templates;
                    if (JapaneseDisplayTemplates.TryGetValue(character, out templates))
                        foreach (var template in templates)
                            if (template.TryApply(source, out translation))
                            {
                                DisplayAttemptCache[source] = translation;
                                return true;
                            }
                }
                // Rendered token variants were expanded into the direct map at load time,
                // so the cheap longest-first substring path is both correct and fast.
                foreach (var character in japaneseCharacters)
                {
                    List<KeyValuePair<string, string>> translations;
                    if (!JapaneseDisplayTranslations.TryGetValue(character, out translations)) continue;
                    foreach (var pair in translations)
                    {
                        if (pair.Key.Length > source.Length) continue;
                        var index = source.IndexOf(pair.Key, StringComparison.Ordinal);
                        if (index < 0) continue;
                        translation = source.Substring(0, index) + pair.Value + source.Substring(index + pair.Key.Length);
                        DisplayAttemptCache[source] = translation;
                        return true;
                    }
                }
            }
            translation = null;
            DisplayAttemptCache[source] = null;
            return false;
        }

        internal static string TranslateAllDisplaySegments(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string cached;
            if (CombinedDisplayCache.TryGetValue(value, out cached)) return cached;
            var original = value;
            var lines = Regex.Split(value, "(\\r?\\n|<br\\s*/?>)", RegexOptions.IgnoreCase);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex += 2)
            {
                var line = lines[lineIndex];
                // Older builds could persist a partially translated copy of this line in
                // the text log. It can no longer match the pristine Japanese source, so
                // repair the complete damaged line before ordinary lookup runs.
                if (line.Contains("ふむ") && line.Contains("普通の労働者") && line.Contains("スパイ"))
                {
                    var sentenceStart = line.IndexOf("ふむ", StringComparison.Ordinal);
                    line = line.Substring(0, sentenceStart) +
                        "흠……. 안타깝지만 나는 지극히 평범한 노동자야. 스파이가 아니지.";
                }
                // Text logs prepend the rendered speaker name to the dialogue and may
                // normalize Japanese punctuation/spacing. Resolve the dialogue tail as a
                // whole sentence before longest-substring fallback can create mixed text.
                var japaneseStart = FirstJapaneseCharacterIndex(line);
                if (japaneseStart >= 0)
                {
                    var tail = line.Substring(japaneseStart);
                    string normalizedTranslation;
                    if (NormalizedDisplayTranslations.TryGetValue(NormalizeDisplayLookup(tail), out normalizedTranslation))
                        line = line.Substring(0, japaneseStart) + normalizedTranslation;
                    else if (TryRepairPartiallyTranslatedLine(line, out normalizedTranslation))
                        line = normalizedTranslation;
                }
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

        private static void BuildSelectionQuestionFragmentTranslations()
        {
            // Cloze questions render the text before and after the answer as separate
            // elements. Derive those fragments from the reviewed full-sentence pairs.
            var candidates = new Dictionary<string, HashSet<string>>();
            foreach (var pair in DisplayTranslations)
            {
                foreach (var selection in SelectionTranslations)
                {
                    if (string.IsNullOrEmpty(selection.Key) || string.IsNullOrEmpty(selection.Value)) continue;
                    var sourceIndex = pair.Key.IndexOf(selection.Key, StringComparison.Ordinal);
                    int targetIndex;
                    int targetLength;
                    if (sourceIndex < 0 || !TryFindIgnoringWhitespace(pair.Value, selection.Value,
                            out targetIndex, out targetLength)) continue;
                    if (pair.Key.IndexOf(selection.Key, sourceIndex + selection.Key.Length, StringComparison.Ordinal) >= 0 ||
                        HasSecondIgnoringWhitespaceMatch(pair.Value, selection.Value, targetIndex + targetLength))
                        continue;
                    AddFragmentCandidate(candidates, pair.Key.Substring(0, sourceIndex),
                        pair.Value.Substring(0, targetIndex));
                    AddFragmentCandidate(candidates, pair.Key.Substring(sourceIndex + selection.Key.Length),
                        pair.Value.Substring(targetIndex + targetLength));
                }
            }
            foreach (var candidate in candidates)
                if (candidate.Value.Count == 1 && !DisplayFragmentTranslations.ContainsKey(candidate.Key))
                    DisplayFragmentTranslations[candidate.Key] = candidate.Value.First();

            // Some localized builds rename 葬儀 to 葬礼式 when constructing this prompt.
            DisplayFragmentTranslations["の葬礼式だったんだ"] = " 장례식이었어.";
        }

        private static bool TryFindIgnoringWhitespace(string text, string value,
            out int start, out int length)
        {
            start = -1;
            length = 0;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value)) return false;
            var textCharacters = new StringBuilder();
            var textIndices = new List<int>();
            for (var index = 0; index < text.Length; index++)
            {
                if (char.IsWhiteSpace(text[index])) continue;
                textCharacters.Append(text[index]);
                textIndices.Add(index);
            }
            var needle = new string(value.Where(character => !char.IsWhiteSpace(character)).ToArray());
            if (needle.Length == 0) return false;
            var normalizedIndex = textCharacters.ToString().IndexOf(needle, StringComparison.Ordinal);
            if (normalizedIndex < 0) return false;
            start = textIndices[normalizedIndex];
            var end = textIndices[normalizedIndex + needle.Length - 1] + 1;
            length = end - start;
            return true;
        }

        private static bool HasSecondIgnoringWhitespaceMatch(string text, string value, int searchStart)
        {
            if (searchStart >= text.Length) return false;
            int ignoredStart;
            int ignoredLength;
            return TryFindIgnoringWhitespace(text.Substring(searchStart), value,
                out ignoredStart, out ignoredLength);
        }

        private static void AddFragmentCandidate(Dictionary<string, HashSet<string>> candidates,
            string source, string target)
        {
            if (string.IsNullOrEmpty(source) || source == target || !FirstJapaneseCharacter(source).HasValue) return;
            HashSet<string> values;
            if (!candidates.TryGetValue(source, out values))
            {
                values = new HashSet<string>();
                candidates[source] = values;
            }
            values.Add(target);
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

        internal static void LogResidualJapanese(string value)
        {
            if (string.IsNullOrEmpty(value) || Regex.IsMatch(value, @"^[あ\s]+$") ||
                !FirstJapaneseCharacter(value).HasValue || !LoggedResidualJapanese.Add(value)) return;
            PocLog.LogWarning("KR_RESIDUAL_JAPANESE " + value.Replace("\r", " ").Replace("\n", " "));
        }
        internal static string NormalizeKoreanDisplay(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            // Dialogue punctuation is sometimes rendered as its own TextElement.
            // Normalize it even when this fragment does not contain Hangul.
            value = value.Replace('、', ',').Replace('，', ',').Replace('。', '.')
                .Replace('！', '!').Replace('？', '?');
            value = RepairResidualJapaneseSubjectParticle(value);
            if (value.Any(c => c >= '\uAC00' && c <= '\uD7A3'))
            {
                // Explicit line breaks must never carry indentation into the rendered line.
                value = Regex.Replace(value, @"(\r?\n)[ \t\u00A0]+", "$1");
                value = Regex.Replace(value, @"\.+([?!])", "$1");
                value = Regex.Replace(value, @"([?!])\.+", "$1");
                value = PreventOrphanPunctuationAndLeadingSpaces(value);

            }
            return value;
        }

        internal static string RepairCharacterColorTags(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.IndexOf("color=", StringComparison.OrdinalIgnoreCase) < 0) return value;

            // Word-joiners inserted for Korean wrapping must never become part of a tag.
            value = Regex.Replace(value, @"<[\u2060\u200B\u00AD\.]*color=", "<color=",
                RegexOptions.IgnoreCase);
            foreach (var character in MessageTranslations.Values)
            {
                if (string.IsNullOrEmpty(character.Translation)) continue;
                var pattern = "(<color=[^>]+>)" + Regex.Escape(character.Translation) +
                    "(?!</color>)";
                value = Regex.Replace(value, pattern,
                    "$1" + character.Translation + "</color>", RegexOptions.IgnoreCase);
            }
            return value;
        }

        private static string RepairResidualJapaneseSubjectParticle(string value)
        {
            if (value == "が") return "가";
            return Regex.Replace(value, @"(?<=[가-힣▓])が", match =>
            {
                var previous = value[match.Index - 1];
                if (previous < '\uAC00' || previous > '\uD7A3') return "가";
                return ((previous - '\uAC00') % 28) == 0 ? "가" : "이";
            });
        }
        internal static string WrapKoreanParagraphByWords(string value, int lineCharacterCount)
        {
            if (string.IsNullOrEmpty(value) || lineCharacterCount <= 0 ||
                !value.Any(IsHangulSyllable)) return value;

            var sourceLines = value.Replace("\r\n", "\n").Split('\n');
            var result = new StringBuilder(value.Length + sourceLines.Length);
            for (var lineIndex = 0; lineIndex < sourceLines.Length; lineIndex++)
            {
                if (lineIndex > 0) result.Append('\n');
                var words = Regex.Split(sourceLines[lineIndex].Trim(), @"[ \t\u00A0]+");
                var visibleLength = 0;
                foreach (var word in words)
                {
                    if (word.Length == 0) continue;
                    var wordLength = Regex.Replace(word, "<[^>]+>", "").Length;
                    if (visibleLength > 0 && visibleLength + 1 + wordLength > lineCharacterCount)
                    {
                        result.Append('\n');
                        visibleLength = 0;
                    }
                    else if (visibleLength > 0)
                    {
                        result.Append(' ');
                        visibleLength++;
                    }
                    result.Append(word);
                    visibleLength += wordLength;
                }
            }
            // A source line may contain a punctuation mark separated by whitespace.
            // Keep that mark at the end of the preceding rendered line.
            return Regex.Replace(result.ToString(),
                @"\n[ \t\u00A0]*([,\.!\?;:…%\)\]\}〉》」』】’”]+)[ \t\u00A0]*",
                "\u2060$1\n");
        }

        private static string PreventOrphanPunctuationAndLeadingSpaces(string value)
        {
            StringBuilder builder = null;
            for (var index = 0; index < value.Length; index++)
            {
                if (builder != null) builder.Append(value[index]);
                if (index + 1 >= value.Length) continue;
                var current = value[index];
                var next = value[index + 1];
                var hasVisiblePreviousCharacter = current != '\r' && current != '\n' &&
                    !char.IsWhiteSpace(current) && current != '\u2060';
                var keepsPunctuationWithPreviousCharacter =
                    hasVisiblePreviousCharacter && IsLineLeadingForbiddenPunctuation(next);
                var keepsTrailingSpaceWithPreviousCharacter = hasVisiblePreviousCharacter &&
                    (next == ' ' || next == '\u00A0' || next == '\t');
                if (!keepsPunctuationWithPreviousCharacter && !keepsTrailingSpaceWithPreviousCharacter) continue;
                if (builder == null) builder = new StringBuilder(value.Length * 2).Append(value, 0, index + 1);
                builder.Append('\u2060');
            }
            if (builder == null) return value;
            return builder.ToString();
        }

        private static bool IsLineLeadingForbiddenPunctuation(char value)
        {
            switch (value)
            {
                case ',': case '.': case '!': case '?': case ';': case ':': case '…': case '%':
                case ')': case ']': case '}': case '〉': case '》': case '」': case '』': case '】':
                case '’': case '”':
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsHangulSyllable(char value)
        {
            return value >= '\uAC00' && value <= '\uD7A3';
        }

        private static char? FirstJapaneseCharacter(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            foreach (var c in value)
                if ((c >= '\u3040' && c <= '\u30ff') || (c >= '\u3400' && c <= '\u9fff')) return c;
            return null;
        }

        private static int FirstJapaneseCharacterIndex(string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if ((character >= '\u3040' && character <= '\u30ff') ||
                    (character >= '\u3400' && character <= '\u9fff')) return index;
            }
            return -1;
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
        private static void Prefix(ref string name, ref string text, int lineCharacterCount)
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
                Plugin.DisplayTranslations.TryGetValue(value, out translated) ||
                Plugin.DisplayFragmentTranslations.TryGetValue(value, out translated))) value = translated;
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

    internal static class DetermineLogPatch
    {
        private static readonly FieldInfo[] TextFields = typeof(DetermineElementController)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => typeof(UnityEngine.UIElements.TextElement).IsAssignableFrom(field.FieldType))
            .ToArray();

        private static void Postfix(DetermineElementController __instance)
        {
            Refresh(__instance);
        }

        internal static void Refresh(DetermineElementController instance)
        {
            if (instance == null) return;
            foreach (var field in TextFields)
            {
                var element = field.GetValue(instance) as UnityEngine.UIElements.TextElement;
                if (element == null || string.IsNullOrEmpty(element.text)) continue;
                var translated = Plugin.TranslateAllDisplaySegments(element.text);
                if (translated != element.text) element.text = translated;
                if (translated.Contains("기억의 조각") && translated.Contains("누구의 기억일까"))
                {
                    element.style.unityTextAlign = TextAnchor.MiddleCenter;
                    element.style.flexGrow = 1f;
                    element.style.alignSelf = UnityEngine.UIElements.Align.Stretch;
                }
            }
        }
    }

    [HarmonyPatch(typeof(DetermineElementController), "open")]
    internal static class DetermineOpenPatch
    {
        private static void Prefix(ref Il2CppSystem.Func<int, string> getText)
        {
            Plugin.DetermineActive = true;
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

        private static void Postfix(DetermineElementController __instance)
        {
            DetermineLogPatch.Refresh(__instance);
        }
    }

    [HarmonyPatch(typeof(TextLogHolder), "add")]
    internal static class TextLogHolderAddPatch
    {
        private static void Prefix(ref string name, ref string text)
        {
            name = Plugin.TranslateAllDisplaySegments(name);
            text = Plugin.RepairPersistedStoryLog(name, text);
            text = Plugin.TranslateAllDisplaySegments(text);
        }
    }

    [HarmonyPatch(typeof(DetermineElementController), "close")]
    internal static class DetermineClosePatch
    {
        private static void Prefix()
        {
            Plugin.DetermineActive = false;
        }
    }

    [HarmonyPatch(typeof(TextLogElementController), "add")]
    internal static class TextLogElementAddPatch
    {
        private static void Prefix(TextLogHolder.Log __0)
        {
            if (__0 == null) return;
            __0.Name = Plugin.TranslateAllDisplaySegments(__0.Name);
            __0.Text = Plugin.RepairPersistedStoryLog(__0.Name, __0.Text);
            __0.Text = Plugin.TranslateAllDisplaySegments(__0.Text);
        }

        private static void Postfix(TextLogElementController __instance)
        {
            Plugin.CenterCompletedTextLogChoices(__instance);
        }
    }

    [HarmonyPatch(typeof(TextLogElementController), "open")]
    internal static class TextLogOpenPatch
    {
        private static void Prefix()
        {
            Plugin.TextLogActive = true;
        }
    }

    [HarmonyPatch(typeof(TextLogElementController), "close")]
    internal static class TextLogClosePatch
    {
        private static void Prefix()
        {
            Plugin.TextLogActive = false;
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

    [HarmonyPatch(typeof(TileElementController.Inventory), "hover")]
    internal static class TileInventoryHoverPatch
    {
        private static void Postfix(TileElementController.Inventory __instance)
        {
            if (__instance == null) return;
            if (__instance._Name != null) __instance._Name.style.top = 36f;
            if (__instance._Description != null) __instance._Description.style.top = -36f;
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
        private static void Prefix(ref string title, ref string correctMessage)
        {
            title = Plugin.TranslateAllDisplaySegments(title);
            correctMessage = Plugin.TranslateAllDisplaySegments(correctMessage);
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

    internal sealed class StoryLogLineEntry
    {
        internal readonly string Speaker;
        internal readonly string Text;

        internal StoryLogLineEntry(string speaker, string text)
        {
            Speaker = speaker ?? "";
            Text = text ?? "";
        }

        internal string Render(string currentLine, string completeLog)
        {
            var body = Plugin.CharacterTokenPattern.Replace(Text, match =>
            {
                var key = match.Value.Substring(1, match.Value.Length - 2);
                MessagePatchEntry character;
                if (Plugin.MessageTranslations.TryGetValue(key, out character) &&
                    !string.IsNullOrEmpty(character.Translation))
                {
                    var colored = Regex.Match(completeLog,
                        "<color=[^>]+>" + Regex.Escape(character.Translation) + "</color>",
                        RegexOptions.IgnoreCase);
                    if (colored.Success) return colored.Value;
                }
                return "▓▓▓▓";
            });
            if (string.IsNullOrEmpty(Speaker)) return body;
            var colon = currentLine.IndexOf(':');
            if (colon < 0) colon = currentLine.IndexOf('：');
            if (colon >= 0) return currentLine.Substring(0, colon + 1) + " " + body;
            MessagePatchEntry speaker;
            var key = Speaker.Trim('<', '>');
            var renderedSpeaker = Plugin.MessageTranslations.TryGetValue(key, out speaker) &&
                !string.IsNullOrEmpty(speaker.Translation) ? speaker.Translation : "▓▓▓▓";
            return renderedSpeaker + ": " + body;
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

    // All scenario tables pass through this method after TextAsset loading. Patching
    // here avoids relying on Unity asset names, which are absent on the IL2CPP path.
    [HarmonyPatch(typeof(app.Tsv), "parse")]
    internal static class TsvParsePatch
    {
        private static void Prefix(ref string source)
        {
            source = Plugin.PatchTsvSource(source);
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
            value = Plugin.TranslateAllDisplaySegments(value);
            value = Plugin.RepairCharacterColorTags(value);
            Plugin.LogResidualJapanese(value);
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
                var plainValue = Regex.Replace(value, "<[^>]+>", "");
                Plugin.LogTextLogLayout(__instance, plainValue);
                var insideTextLog = Plugin.TextLogActive && !Plugin.DetermineActive ||
                    HasAncestorRole(__instance, "TextLog");
                if (determineResult && insideTextLog)
                {
                    // A determination result is centered on the determination overlay,
                    // but becomes ordinary body copy after it is stored in the memory log.
                    __instance.style.unityTextAlign = TextAnchor.MiddleLeft;
                    __instance.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
                    __instance.style.alignSelf = UnityEngine.UIElements.Align.Stretch;
                }
                else if (determineTitle || determineResult)
                {
                    __instance.style.unityTextAlign = TextAnchor.MiddleCenter;
                    __instance.style.flexGrow = 1f;
                    __instance.style.alignSelf = UnityEngine.UIElements.Align.Stretch;
                }
                else if (insideTextLog &&
                    (__instance is UnityEngine.UIElements.Button) &&
                    Plugin.FixedMenuLabelTranslations.Contains(plainValue))
                {
                    // Wrong/correct choices use a separate colored Button state. Remove
                    // its asymmetric horizontal padding, but preserve the assigned width.
                    __instance.style.unityTextAlign = TextAnchor.MiddleCenter;
                    __instance.style.paddingLeft = 0f;
                    __instance.style.paddingRight = 0f;
                    __instance.style.marginLeft = 0f;
                    __instance.style.marginRight = 0f;
                }
                else if (insideTextLog &&
                    !(__instance is UnityEngine.UIElements.Button) &&
                    HasButtonParent(__instance) &&
                    Plugin.FixedMenuLabelTranslations.Contains(plainValue))
                {
                    // Static memory-log locations, items and speaker assignments use
                    // separate colored child labels. Stretch only those labels inside
                    // their existing button. Expanding the Button itself makes a narrow
                    // speaker cell consume the full log row and causes overlapping text.
                    __instance.style.unityTextAlign = TextAnchor.MiddleCenter;
                    __instance.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
                    __instance.style.flexGrow = 1f;
                    __instance.style.alignSelf = UnityEngine.UIElements.Align.Stretch;
                    __instance.style.paddingLeft = 0f;
                    __instance.style.paddingRight = 0f;
                    __instance.style.marginLeft = 0f;
                    __instance.style.marginRight = 0f;
                }
                else if (HasCenteredInteractiveRole(__instance, elementName))
                {
                    // Center only the glyphs. Changing the selection label's layout box
                    // offsets the cursor-following clone used by cloze questions.
                    __instance.style.unityTextAlign = TextAnchor.MiddleCenter;
                    if (!(__instance is UnityEngine.UIElements.Button) && HasButtonParent(__instance))
                    {
                        // Fixed menu buttons sometimes wrap colored text in a child label.
                        // Stretch that label to the button bounds so red/green states share
                        // the same center as the ordinary white state. The draggable cloze
                        // clone is itself a Button and is deliberately excluded.
                        __instance.style.flexGrow = 1f;
                        __instance.style.alignSelf = UnityEngine.UIElements.Align.Stretch;
                    }
                }
                else if (value.Length >= 20 && !Regex.IsMatch(elementName,
                    "title|button|selection|submit|option", RegexOptions.IgnoreCase))
                {
                    __instance.style.unityTextAlign = TextAnchor.MiddleLeft;
                }
            }
        }

        private static bool HasCenteredInteractiveRole(UnityEngine.UIElements.TextElement element, string elementName)
        {
            if (element is UnityEngine.UIElements.Button) return true;
            var current = element as UnityEngine.UIElements.VisualElement;
            for (var depth = 0; current != null && depth < 4; depth++, current = current.parent)
            {
                var identity = (current.name ?? "") + " " + current.GetType().Name;
                if (Regex.IsMatch(identity, "button|selection|choice|tab|submit|option|keyword|token", RegexOptions.IgnoreCase))
                    return true;
            }
            return Regex.IsMatch(elementName, "button|selection|choice|tab|submit|option|keyword|token", RegexOptions.IgnoreCase);
        }

        private static bool HasAncestorRole(UnityEngine.UIElements.VisualElement element, string role)
        {
            for (var current = element; current != null; current = current.parent)
            {
                var identity = (current.name ?? "") + " " + current.GetType().Name;
                if (identity.IndexOf(role, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool HasButtonParent(UnityEngine.UIElements.TextElement element)
        {
            var current = element.parent;
            for (var depth = 0; current != null && depth < 4; depth++, current = current.parent)
                if (current is UnityEngine.UIElements.Button) return true;
            return false;
        }

        internal static bool ContainsHangul(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (var c in value) if (c >= '\uAC00' && c <= '\uD7A3') return true;
            return false;
        }
    }

}
