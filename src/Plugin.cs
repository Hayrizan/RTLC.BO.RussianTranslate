using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using I2.Loc;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RTLC.BO.RussianTranslate
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Bo.exe")]
    public sealed class BoRussianPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.rtlc.bo.russian-translation";
        public const string PluginName = "RTLC.BO.RussainTranslate";
        public const string PluginVersion = "1.0.20";
        public const string RussianLanguageName = "Русский";
        public const string RussianLanguageCode = "ru";
        private const string LanguageInitializedKey = "RTLC.BO.RussianTranslate.LanguageInitialized";

        internal static ManualLogSource ModLog;
        internal static BoRussianPlugin Instance;
        private float _nextRefresh;
        private bool _firstUpdateLogged;
        private string _visibleSettings;
        private ConfigEntry<bool> _debugMode;
        private bool _loadedDebugMode;
        private string _translationStamp;
        private long _configWrite;
        private float _nextResourceCheck;
        internal string TranslationJson { get; private set; }

        private readonly HashSet<LanguageSourceData> _patchedSources = new HashSet<LanguageSourceData>();
        private readonly HashSet<int> _patchedDatabases = new HashSet<int>();
        private readonly List<DialogueTranslation> _dialogueTranslations = new List<DialogueTranslation>();
        private readonly List<ActorTranslation> _actorTranslations = new List<ActorTranslation>();
        private readonly Dictionary<string, string> _termTranslations = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, TranslationTerm> _pcTerms = new Dictionary<string, TranslationTerm>(StringComparer.Ordinal);

        private string _dialogueLanguage;
        private bool _startupPreferenceChecked;

        private void Awake()
        {
            ModLog = Logger;
            Instance = this;
            Logger.LogMessage(@" ____  _____ _      ____");
            Logger.LogMessage(@"|  _ \|_   _| |    / ___|");
            Logger.LogMessage(@"| |_) | | | | |   | |");
            Logger.LogMessage(@"|  _ <  | | | |___| |___");
            Logger.LogMessage(@"|_| \_\ |_| |_____|\____|");
            Logger.LogMessage("Русификатор разработан RTLC TEAM");
            _debugMode = Config.Bind("General", "debugmode", false,
                "false: перевод из rtlc.bundle. true: RussianData.json рядом с DLL; изменения применяются после сохранения файла.");
            _loadedDebugMode = _debugMode.Value;
            gameObject.hideFlags |= HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            Canvas.preWillRenderCanvases += PrepareVisibleSettings;
            LoadTranslations();

            Harmony harmony = new Harmony(PluginGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            foreach (MethodBase patched in harmony.GetPatchedMethods())
                Logger.LogInfo("Harmony подключён: " + patched.DeclaringType.FullName + "." + patched.Name);
            Logger.LogInfo("Подключены обработчики регистрации I2 и открытия списка языков.");
            LocalizationManager.UpdateSources();
            RefreshRuntime();
            Logger.LogInfo("Первичная регистрация I2: источников " + LocalizationManager.Sources.Count + ".");
        }

        private bool LoadTranslations()
        {
            try
            {
                string json = RussianAssets.ReadTranslation(_debugMode.Value);
                RussianTranslationData data;
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(RussianTranslationData));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    data = serializer.ReadObject(stream) as RussianTranslationData;
                if (data == null)
                {
                    Logger.LogError("Не удалось прочитать RussianData.json.");
                    return false;
                }
                if (data.formatVersion != 1 && data.formatVersion != 2)
                {
                    Logger.LogError("Неподдерживаемая версия RussianData.json: " + data.formatVersion);
                    return false;
                }
                if (data.formatVersion == 2)
                    UnifiedTranslations.Expand(data);

                // Parse/validate before replacing the active translation. An editor
                // can briefly leave a partial JSON file while saving.
                _termTranslations.Clear();
                _pcTerms.Clear();
                _dialogueTranslations.Clear();
                _actorTranslations.Clear();

                Logger.LogInfo(string.Format(
                    "Разобраны секции RussianData: terms={0}, dialogue={1}, actors={2}.",
                    data.terms == null ? "null" : data.terms.Length.ToString(),
                    data.dialogue == null ? "null" : data.dialogue.Length.ToString(),
                    data.actors == null ? "null" : data.actors.Length.ToString()));

                if (data.terms != null)
                {
                    foreach (TranslationTerm item in data.terms)
                    {
                        if (item != null && !string.IsNullOrEmpty(item.key) && !string.IsNullOrEmpty(item.value))
                        {
                            _termTranslations[item.key] = item.value;
                            if (!string.IsNullOrEmpty(item.sourceEnglish))
                                _pcTerms[item.key] = item;
                        }
                    }
                }
                if (data.dialogue != null)
                    _dialogueTranslations.AddRange(data.dialogue);
                if (data.actors != null)
                    _actorTranslations.AddRange(data.actors);

                Logger.LogInfo(string.Format(
                    "Загружены данные перевода: {0} строк I2, {1} реплик и {2} имён.",
                    _termTranslations.Count, _dialogueTranslations.Count, _actorTranslations.Count));
                TranslationJson = json;
                _loadedDebugMode = _debugMode.Value;
                _patchedSources.Clear();
                _patchedDatabases.Clear();
                _dialogueLanguage = null;
                _visibleSettings = null;
                Logger.LogInfo(_loadedDebugMode ? "Debug: перевод из " + RussianAssets.TranslationPath : "Перевод из rtlc.bundle (debugmode = false).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Ошибка чтения RussianData.json: " + ex);
                return false;
            }
        }

        private void PollTranslation()
        {
            if (Time.realtimeSinceStartup < _nextResourceCheck) return;
            _nextResourceCheck = Time.realtimeSinceStartup + 1f;
            try
            {
                long configWrite = File.GetLastWriteTimeUtc(Config.ConfigFilePath).Ticks;
                if (_configWrite != configWrite)
                {
                    _configWrite = configWrite;
                    Config.Reload();
                }
                var file = new FileInfo(RussianAssets.TranslationPath);
                string stamp = _debugMode.Value + "|" + (file.Exists ? file.LastWriteTimeUtc.Ticks + ":" + file.Length : "missing");
                if (_loadedDebugMode == _debugMode.Value && (!_debugMode.Value || _translationStamp == stamp)) return;
                if (_translationStamp == stamp) return;
                _translationStamp = stamp;
                if (LoadTranslations()) RefreshRuntime();
            }
            catch (Exception ex) { Logger.LogWarning("Не удалось перечитать debug-перевод: " + ex.Message); }
        }

        private void Update()
        {
            PollTranslation();
            if (!_firstUpdateLogged)
            {
                _firstUpdateLogged = true;
                Logger.LogInfo("Периодическое обновление локализации запущено.");
            }
            if (Time.realtimeSinceStartup < _nextRefresh)
                return;
            _nextRefresh = Time.realtimeSinceStartup + 0.25f;
            RefreshRuntime();
        }

        private void OnDestroy()
        {
            Canvas.preWillRenderCanvases -= PrepareVisibleSettings;
            ModLog.LogInfo("Unity-объект плагина остановлен; обработчики Harmony сохраняют данные.");
        }

        private void PrepareVisibleSettings()
        {
            if (_termTranslations.Count == 0) return;
            // Activation and localization finish before Canvas layout. Preparing
            // here avoids displaying one frame of native AutoSize in RU boxes.
            // Only page/locale changes trigger the work; the timer remains a fallback.
            var pages = new List<int>();
            foreach (VerticalLayoutGroup layout in Resources.FindObjectsOfTypeAll<VerticalLayoutGroup>())
                if (layout.gameObject.scene.IsValid() && layout.gameObject.activeInHierarchy
                    && layout.name.EndsWith(" Page", StringComparison.Ordinal) && layout.transform.parent != null
                    && layout.transform.parent.name == "Pages" && layout.transform.parent.parent != null
                    && layout.transform.parent.parent.name == "NewSettingsMenu") pages.Add(layout.GetInstanceID());
            pages.Sort();
            string signature = LocalizationManager.CurrentLanguageCode + "|" + string.Join(",", pages);
            if (_visibleSettings == signature) return;
            _visibleSettings = signature;
            if (pages.Count == 0) return;
            try
            {
                RussianFonts.Refresh();
                RussianDropdownLayout.Refresh();
                RussianMenuLayout.Refresh();
                RussianBindingLayout.Refresh();
            }
            catch (Exception ex) { Logger.LogError("Ошибка подготовки меню перед отображением: " + ex); }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ModLog.LogInfo("Обновление локализации после загрузки сцены: " + scene.name);
            RefreshRuntime();
        }

        internal void RefreshRuntime()
        {
            if (_termTranslations.Count == 0)
                return;
            try
            {
                AddRussianToSources();
                RestoreRussianStartupPreference();
                RussianFonts.Refresh();
                RussianDropdownLayout.Refresh();
                RussianMenuLayout.Refresh();
                RussianFastTravelLayout.Refresh();
                RussianBindingLayout.Refresh();
                RussianHitCounter.Refresh();
                SynchronizeDialogueLanguage();
                RefreshLanguageDropdowns();
            }
            catch (Exception ex)
            {
                Logger.LogError("Ошибка обновления русской локализации: " + ex);
            }
        }

        private void AddRussianToSources()
        {
            if (LocalizationManager.Sources == null || LocalizationManager.Sources.Count == 0)
                return;

            bool changed = false;
            List<LanguageSourceData> sources = new List<LanguageSourceData>(LocalizationManager.Sources);
            foreach (LanguageSourceData source in sources)
            {
                if (source == null || _patchedSources.Contains(source))
                    continue;

                int russianIndex = source.GetLanguageIndexFromCode(RussianLanguageCode, true, false);
                if (russianIndex < 0)
                {
                    source.AddLanguage(RussianLanguageName, RussianLanguageCode);
                    russianIndex = source.GetLanguageIndexFromCode(RussianLanguageCode, true, false);
                    int englishIndex = source.GetLanguageIndexFromCode("en", false, false);
                    foreach (TermData term in source.mTerms)
                    {
                        if (englishIndex >= 0 && term.Languages != null && englishIndex < term.Languages.Length)
                            term.Languages[russianIndex] = term.Languages[englishIndex];
                    }
                    changed = true;
                }
                if (russianIndex < 0)
                    continue;

                int applied = 0;
                int matched = 0;
                foreach (KeyValuePair<string, string> item in _termTranslations)
                {
                    TermData term = source.GetTermData(item.Key);
                    if (term == null)
                        continue;
                    matched++;

                    if (term.Languages == null || russianIndex >= term.Languages.Length)
                        Array.Resize(ref term.Languages, russianIndex + 1);
                    if (term.Languages[russianIndex] != item.Value)
                    {
                        term.Languages[russianIndex] = item.Value;
                        applied++;
                    }
                }

                source.mLanguages[russianIndex].SetEnabled(true);
                source.mLanguages[russianIndex].SetLoaded(true);
                source.mLanguages[russianIndex].SetCanBeUnLoaded(false);
                source.UpdateDictionary(true);
                _patchedSources.Add(source);
                Logger.LogInfo(string.Format(
                    "Добавлен язык {0} ({1}) в источник I2; совпало ключей: {2}, изменено ячеек: {3}.",
                    source.mLanguages[russianIndex].Name, RussianLanguageCode, matched, applied));
                changed = true;
            }

            if (changed)
                LocalizationManager.LocalizeAll(true);
        }

        internal void RefreshLanguageDropdowns()
        {
            List<string> languages = GetDisplayedLanguages();
            foreach (SetLanguageDropdown selector in Resources.FindObjectsOfTypeAll<SetLanguageDropdown>())
            {
                if (!selector.gameObject.scene.IsValid())
                    continue;
                TMP_Dropdown dropdown = selector.GetComponent<TMP_Dropdown>();
                if (dropdown == null || dropdown.IsExpanded)
                    continue;
                bool matches = dropdown.options.Count == languages.Count;
                for (int i = 0; matches && i < languages.Count; i++)
                    matches = dropdown.options[i].text == languages[i];
                if (!matches)
                {
                    dropdown.ClearOptions();
                    dropdown.AddOptions(languages);
                    dropdown.SetValueWithoutNotify(Math.Max(0, languages.IndexOf(LocalizationManager.CurrentLanguage)));
                    Logger.LogInfo("Список языков обновлён: " + string.Join(", ", languages.ToArray()));
                }

                int selectedIndex = languages.IndexOf(LocalizationManager.CurrentLanguage);
                TMP_FontAsset captionFont = RussianDropdownFontPatch.GetLanguageFont(selector, selectedIndex);
                if (dropdown.captionText != null && captionFont != null && dropdown.captionText.font != captionFont)
                {
                    dropdown.captionText.font = captionFont;
                    dropdown.RefreshShownValue();
                }
            }
        }

        internal static List<string> GetDisplayedLanguages()
        {
            List<string> languages = LocalizationManager.GetAllLanguages();
            int russianIndex = languages.IndexOf(RussianLanguageName);
            if (russianIndex < 0)
                russianIndex = languages.IndexOf("Russian");
            if (russianIndex > 0)
            {
                string russian = languages[russianIndex];
                languages.RemoveAt(russianIndex);
                languages.Insert(0, russian);
            }
            return languages;
        }

        private void RestoreRussianStartupPreference()
        {
            if (_startupPreferenceChecked || _patchedSources.Count == 0)
                return;

            _startupPreferenceChecked = true;
            try
            {
                bool firstLaunch = PersistentStorage.GetSetting_String(LanguageInitializedKey, string.Empty) != "1";
                string savedLanguage = PersistentStorage.GetSetting_String("I2 Language", string.Empty);
                if (firstLaunch || savedLanguage == RussianLanguageName || savedLanguage == "Russian")
                {
                    LocalizationManager.SetLanguageAndCode(RussianLanguageName, RussianLanguageCode, firstLaunch, true);
                    if (firstLaunch)
                    {
                        PersistentStorage.SetSetting_String(LanguageInitializedKey, "1");
                        PersistentStorage.ForceSaveSettings();
                        Logger.LogInfo("Первый запуск русификатора: выбран и сохранён Русский. В дальнейшем действует выбор игрока.");
                    }
                    else Logger.LogInfo("Восстановлен сохранённый выбор языка: Русский.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Не удалось восстановить сохранённый выбор языка: " + ex.Message);
            }
        }

        private void SynchronizeDialogueLanguage()
        {
            if (!DialogueManager.hasInstance)
                return;

            string languageCode = LocalizationManager.CurrentLanguageCode;
            if (!string.IsNullOrEmpty(languageCode) && !string.Equals(languageCode, _dialogueLanguage, StringComparison.Ordinal))
            {
                DialogueManager.SetLanguage(languageCode);
                _dialogueLanguage = languageCode;
            }

            DialogueDatabase database = DialogueManager.masterDatabase;
            if (database == null)
                return;

            int instanceId = database.GetInstanceID();
            if (_patchedDatabases.Contains(instanceId))
            {
                SynchronizeLuaActorNames(database);
                return;
            }

            int pcActors;
            int pcDialogue;
            int pcMenus;
            ApplyPcDialogueTerms(database, out pcActors, out pcDialogue, out pcMenus);
            int actors = pcActors + ApplyActorTranslations(database);
            int dialogue = pcDialogue + ApplyDialogueTranslations(database);
            _patchedDatabases.Add(instanceId);
            SynchronizeLuaActorNames(database);
            Logger.LogInfo(string.Format("Диалоговая база дополнена русскими именами: {0}, репликами: {1}.", actors, dialogue));
            Logger.LogInfo(string.Format("PC-термы диалоговой базы: имён={0}, реплик={1}, ответов меню={2}.", pcActors, pcDialogue, pcMenus));
            AuditDialogueCoverage(database);
        }

        private void SynchronizeLuaActorNames(DialogueDatabase database)
        {
            // CharacterInfo reads the Lua Actor table, which may already have
            // been built before our localized database fields were added.
            foreach (Actor actor in database.actors)
            {
                string translated = Field.LookupValue(actor.fields, "Display Name ru");
                if (string.IsNullOrEmpty(actor.Name) || string.IsNullOrEmpty(translated))
                    continue;
                string registeredName = DialogueLua.GetActorField(actor.Name, "Name").asString;
                if (string.IsNullOrEmpty(registeredName) || registeredName == "nil")
                    continue; // The Lua table is not ready; the next refresh retries.
                if (!string.Equals(DialogueLua.GetActorField(actor.Name, "Display Name ru").asString,
                    translated, StringComparison.Ordinal))
                    DialogueLua.SetActorField(actor.Name, "Display Name ru", translated);
            }
        }

        private void AuditDialogueCoverage(DialogueDatabase database)
        {
            int total = 0;
            List<string> missing = new List<string>();
            foreach (Conversation conversation in database.conversations)
            {
                foreach (DialogueEntry entry in conversation.dialogueEntries)
                {
                    string english = entry.DialogueText;
                    // START is the Dialogue System's routing node, not displayed dialogue.
                    if (string.IsNullOrWhiteSpace(english) || string.Equals(english, "START", StringComparison.Ordinal))
                        continue;
                    total++;
                    if (string.IsNullOrWhiteSpace(Field.LookupValue(entry.fields, RussianLanguageCode)))
                        missing.Add(conversation.id + ":" + entry.id + "\t" + english.Replace("\t", " ").Replace("\r", " ").Replace("\n", " "));
                }
            }
            Logger.LogInfo(string.Format("Покрытие непустых реплик masterDatabase: {0}/{1}; без русского поля: {2}.", total - missing.Count, total, missing.Count));
            if (missing.Count == 0)
                return;
            try
            {
                string path = Path.Combine(RussianAssets.DirectoryPath, "UntranslatedDialogue.tsv");
                File.WriteAllLines(path, missing, new UTF8Encoding(false));
                Logger.LogWarning("Список непереведённых реплик сохранён: " + path);
            }
            catch (Exception error)
            {
                Logger.LogWarning("Не удалось сохранить список покрытия: " + error.Message);
            }
        }

        private static string SanitizeI2Name(string value)
        {
            return I2Utils.RemoveNonASCII((value ?? string.Empty).Replace("/", ".").Replace("\\", ".").Replace("[", ".").Replace("]", "."));
        }

        private void ApplyPcDialogueTerms(DialogueDatabase database, out int actors, out int dialogue, out int menus)
        {
            actors = dialogue = menus = 0;
            foreach (Actor actor in database.actors)
            {
                string key = "Dialogue System/Actor/" + SanitizeI2Name(actor.Name) + "/Display Name";
                TranslationTerm term;
                string displayName = Field.LookupValue(actor.fields, "Display Name");
                string name = actor.Name ?? string.Empty;
                string leafName = name.Substring(name.LastIndexOf('/') + 1);
                if (!_pcTerms.TryGetValue(key, out term) ||
                    !string.Equals(string.IsNullOrEmpty(displayName) ? leafName : displayName, term.sourceEnglish, StringComparison.Ordinal))
                    continue;
                Field.SetValue(actor.fields, "Display Name ru", term.value, FieldType.Localization);
                actors++;
            }
            foreach (Conversation conversation in database.conversations)
            {
                string prefix = "Dialogue System/Conversation/" + SanitizeI2Name(Field.LookupValue(conversation.fields, "Title")) + "/Entry/";
                foreach (DialogueEntry entry in conversation.dialogueEntries)
                {
                    Actor actor = database.GetActor(entry.ActorID);
                    string header = I2Utils.RemoveNonASCII(prefix + entry.id.ToString("D3") + "/" + (actor == null ? string.Empty : actor.Name), true);
                    TranslationTerm term;
                    // Match both the full PC key and exact English text before adding a column.
                    if (_pcTerms.TryGetValue(header + "/Dialogue Text", out term) &&
                        string.Equals(entry.DialogueText, term.sourceEnglish, StringComparison.Ordinal))
                    {
                        Field.SetValue(entry.fields, RussianLanguageCode, term.value, FieldType.Text);
                        dialogue++;
                    }
                    if (_pcTerms.TryGetValue(header + "/Menu Text", out term) &&
                        string.Equals(entry.MenuText, term.sourceEnglish, StringComparison.Ordinal))
                    {
                        Field.SetValue(entry.fields, "Menu Text ru", term.value, FieldType.Text);
                        menus++;
                    }
                }
            }
        }

        private int ApplyActorTranslations(DialogueDatabase database)
        {
            int count = 0;
            foreach (ActorTranslation item in _actorTranslations)
            {
                if (item == null || string.IsNullOrEmpty(item.displayName))
                    continue;

                Actor actor = database.GetActor(item.id);
                if (actor == null || !string.Equals(Field.LookupValue(actor.fields, "Name"), item.sourceName, StringComparison.Ordinal))
                    continue;

                string title = "Display Name ru";
                if (string.IsNullOrEmpty(Field.LookupValue(actor.fields, title)))
                {
                    Field.SetValue(actor.fields, title, item.displayName, FieldType.Localization);
                    count++;
                }
            }
            return count;
        }

        private int ApplyDialogueTranslations(DialogueDatabase database)
        {
            int count = 0;
            foreach (DialogueTranslation item in _dialogueTranslations)
            {
                if (item == null || string.IsNullOrEmpty(item.sourceEnglish) || string.IsNullOrEmpty(item.russian))
                    continue;

                Conversation conversation = database.GetConversation(item.conversationId);
                DialogueEntry entry = conversation == null ? null : conversation.GetDialogueEntry(item.entryId);
                if (entry == null || !string.Equals(entry.DialogueText, item.sourceEnglish, StringComparison.Ordinal))
                    continue;

                if (string.IsNullOrEmpty(Field.LookupValue(entry.fields, RussianLanguageCode)))
                {
                    Field.SetValue(entry.fields, RussianLanguageCode, item.russian, FieldType.Text);
                    count++;
                }
            }
            return count;
        }
    }

#pragma warning disable CS0649
    [DataContract]
    public sealed class RussianTranslationData
    {
        [DataMember(Name = "formatVersion")]
        public int formatVersion;
        [DataMember(Name = "terms")]
        public TranslationTerm[] terms;
        [DataMember(Name = "actors")]
        public ActorTranslation[] actors;
        [DataMember(Name = "dialogue")]
        public DialogueTranslation[] dialogue;
        [DataMember(Name = "translations")]
        public UnifiedTranslation[] translations;
    }

    [DataContract]
    public sealed class TranslationTerm
    {
        [DataMember(Name = "key")]
        public string key;
        [DataMember(Name = "value")]
        public string value;
        [DataMember(Name = "sourceEnglish", EmitDefaultValue = false)]
        public string sourceEnglish;
    }

    [DataContract]
    public sealed class ActorTranslation
    {
        [DataMember(Name = "id")]
        public int id;
        [DataMember(Name = "sourceName")]
        public string sourceName;
        [DataMember(Name = "displayName")]
        public string displayName;
    }

    [DataContract]
    public sealed class DialogueTranslation
    {
        [DataMember(Name = "conversationId")]
        public int conversationId;
        [DataMember(Name = "entryId")]
        public int entryId;
        [DataMember(Name = "sourceEnglish")]
        public string sourceEnglish;
        [DataMember(Name = "russian")]
        public string russian;
    }
#pragma warning restore CS0649

    [HarmonyPatch(typeof(LocalizationManager), "AddSource")]
    internal static class RussianSourceRegistrationPatch
    {
        private static void Postfix()
        {
            if (!ReferenceEquals(BoRussianPlugin.Instance, null))
                BoRussianPlugin.Instance.RefreshRuntime();
        }
    }

    [HarmonyPatch(typeof(SetLanguageDropdown), "OnEnable")]
    internal static class RussianDropdownOptionsPatch
    {
        private static void Prefix(SetLanguageDropdown __instance)
        {
            if (ReferenceEquals(BoRussianPlugin.Instance, null))
                return;
            if (LocalizationManager.Sources.Count == 0)
                LocalizationManager.UpdateSources();
            BoRussianPlugin.Instance.RefreshRuntime();

            TMP_Dropdown dropdown = __instance.GetComponent<TMP_Dropdown>();
            if (dropdown == null)
                return;

            // The game's OnEnable sets dropdown.value using the unmodified I2 order.
            // Keep that order in place until the original callback has finished.
            dropdown.ClearOptions();
            dropdown.AddOptions(LocalizationManager.GetAllLanguages());
        }

        private static void Postfix(SetLanguageDropdown __instance)
        {
            if (ReferenceEquals(BoRussianPlugin.Instance, null))
                return;
            BoRussianPlugin.Instance.RefreshRuntime();
            BoRussianPlugin.Instance.RefreshLanguageDropdowns();
        }
    }

    [HarmonyPatch(typeof(SetLanguageDropdown), "OnValueChanged")]
    internal static class RussianDropdownSelectionPatch
    {
        private static void Prefix(SetLanguageDropdown __instance, ref int index)
        {
            TMP_Dropdown dropdown = __instance == null ? null : __instance.GetComponent<TMP_Dropdown>();
            if (dropdown == null || index < 0 || index >= dropdown.options.Count)
                return;

            string selectedLanguage = dropdown.options[index].text;
            List<string> sourceLanguages = LocalizationManager.GetAllLanguages();
            int sourceIndex = sourceLanguages.IndexOf(selectedLanguage);
            if (sourceIndex >= 0)
            {
                BoRussianPlugin.ModLog.LogInfo(string.Format(
                    "Выбор языка в меню: {0}, индекс списка {1} → исходный индекс I2 {2}.",
                    selectedLanguage, index, sourceIndex));
                index = sourceIndex;
            }
        }

        private static void Postfix(int index)
        {
            string currentLanguage = LocalizationManager.CurrentLanguage;
            string sample = LocalizationManager.GetTranslation("UI/Language", overrideLanguage: currentLanguage);
            string rawSample = "<source missing>";
            int russianIndex = -1;
            if (LocalizationManager.Sources != null && LocalizationManager.Sources.Count > 0)
            {
                LanguageSourceData source = LocalizationManager.Sources[0];
                russianIndex = source.GetLanguageIndex(BoRussianPlugin.RussianLanguageName, false, false);
                TermData term = source.GetTermData("UI/Language");
                if (term != null && term.Languages != null && russianIndex >= 0 && russianIndex < term.Languages.Length)
                    rawSample = term.Languages[russianIndex] ?? "<null>";
                else if (term != null)
                    rawSample = "<language column missing>";
            }
            BoRussianPlugin.ModLog.LogInfo(string.Format(
                "После выбора: I2={0} ({1}); UI/Language: индекс ru={2}, значение столбца='{3}', GetTranslation='{4}'.",
                currentLanguage, LocalizationManager.CurrentLanguageCode, russianIndex, rawSample, sample));
            LocalizationManager.LocalizeAll(true);
            if (!ReferenceEquals(BoRussianPlugin.Instance, null))
                BoRussianPlugin.Instance.RefreshRuntime();
        }
    }

    [HarmonyPatch(typeof(SetLanguageDropdown), "LanguageFont")]
    internal static class RussianDropdownFontPatch
    {
        private static void Postfix(SetLanguageDropdown __instance, int index, ref TMP_FontAsset __result)
        {
            try
            {
                List<string> languages = LocalizationManager.GetAllLanguages();
                if (index < 0 || index >= languages.Count)
                    return;

                TMP_FontAsset font = GetLanguageFont(__instance, index);
                if (font != null)
                    __result = font;
            }
            catch (Exception ex)
            {
                if (BoRussianPlugin.ModLog != null)
                    BoRussianPlugin.ModLog.LogWarning("Не удалось выбрать шрифт для русского пункта меню: " + ex.Message);
            }
        }

        internal static TMP_FontAsset GetLanguageFont(SetLanguageDropdown selector, int index)
        {
            if (selector == null || index < 0)
                return null;

            TMP_Dropdown dropdown = selector.GetComponent<TMP_Dropdown>();
            if (dropdown == null || index >= dropdown.options.Count)
                return null;
            string selected = dropdown.options[index].text;
            if (selected == BoRussianPlugin.RussianLanguageName || selected == "Russian" || LocalizationManager.GetLanguageCode(selected) == BoRussianPlugin.RussianLanguageCode)
            {
                FieldInfo englishField = typeof(SetLanguageDropdown).GetField("EnglishFont", BindingFlags.Instance | BindingFlags.NonPublic);
                return RussianFonts.DropdownFont(englishField == null ? null : englishField.GetValue(selector) as TMP_FontAsset);
            }
            string code = LocalizationManager.GetLanguageCode(selected);
            // The shipped English locale is en-US; other sources may also use regions.
            if (!string.IsNullOrEmpty(code) && code != "zh-CN" && code != "zh-TW" && code != "pt-BR")
                code = code.Split('-')[0];
            string fieldName = code == "en" ? "EnglishFont"
                : code == "es" ? "SpanishFont"
                : code == "fr" ? "FrenchFont"
                : code == "de" ? "GermanFont"
                : code == "ja" ? "JapaneseFont"
                : code == "zh-CN" ? "ChineseSimplifiedFont"
                : code == "ko" ? "KoreanFont"
                : code == "zh-TW" ? "ChineseTraditionalFont"
                : code == "uk" ? "UkrainianFont"
                : code == "pt-BR" ? "BPFont"
                : null;
            if (fieldName == null)
                return null;
            FieldInfo fontField = typeof(SetLanguageDropdown).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            TMP_FontAsset font = fontField == null ? null : fontField.GetValue(selector) as TMP_FontAsset;
            if (font != null && code != "en")
            {
                // Original Asian fonts omit some ASCII symbols (e.g. @ in resolutions).
                FieldInfo englishField = typeof(SetLanguageDropdown).GetField("EnglishFont", BindingFlags.Instance | BindingFlags.NonPublic);
                TMP_FontAsset english = englishField == null ? null : englishField.GetValue(selector) as TMP_FontAsset;
                if (english != null && english != font)
                {
                    if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    if (!font.fallbackFontAssetTable.Contains(english)) font.fallbackFontAssetTable.Add(english);
                }
            }
            return font;
        }
    }
}
