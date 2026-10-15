using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace RTLC.BO.RussianTranslate
{
    internal static class UnifiedTranslations
    {
        internal static void Expand(RussianTranslationData data)
        {
            if (data.translations == null || data.translations.Length == 0)
                throw new InvalidDataException("В формате 2 отсутствуют записи translations.");
            if (data.terms != null || data.dialogue != null || data.actors != null)
                throw new InvalidDataException("Формат 2 должен содержать только translations, без старых секций перевода.");

            var terms = new List<TranslationTerm>();
            var dialogue = new List<DialogueTranslation>();
            var actors = new List<ActorTranslation>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (UnifiedTranslation item in data.translations)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id)
                    || item.sourceEnglish == null || string.IsNullOrWhiteSpace(item.russian))
                    throw new InvalidDataException("Некорректная или повторная запись translations.");
                int count = 0;
                foreach (string key in item.keys ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(key) || !targets.Add("term:" + key))
                        throw new InvalidDataException("Повторный или пустой ключ в " + item.id);
                    terms.Add(new TranslationTerm { key = key, sourceEnglish = item.sourceEnglish, value = item.russian });
                    count++;
                }
                foreach (DialogueTarget target in item.dialogueTargets ?? Array.Empty<DialogueTarget>())
                {
                    if (target == null || target.conversationId < 0 || target.entryId < 0
                        || !targets.Add("dialogue:" + target.conversationId + ":" + target.entryId))
                        throw new InvalidDataException("Некорректный адрес диалога в " + item.id);
                    dialogue.Add(new DialogueTranslation
                    {
                        conversationId = target.conversationId, entryId = target.entryId,
                        sourceEnglish = item.sourceEnglish, russian = item.russian
                    });
                    count++;
                }
                foreach (ActorTarget target in item.actorTargets ?? Array.Empty<ActorTarget>())
                {
                    if (target == null || target.id < 0 || string.IsNullOrWhiteSpace(target.sourceName)
                        || !targets.Add("actor:" + target.id))
                        throw new InvalidDataException("Некорректный адрес персонажа в " + item.id);
                    actors.Add(new ActorTranslation { id = target.id, sourceName = target.sourceName, displayName = item.russian });
                    count++;
                }
                if (count == 0) throw new InvalidDataException("В " + item.id + " отсутствуют адреса перевода.");
            }
            // Both game lookup mechanisms receive the same single authored string.
            data.terms = terms.ToArray();
            data.dialogue = dialogue.ToArray();
            data.actors = actors.ToArray();
            BoRussianPlugin.ModLog.LogInfo("Единый формат перевода: " + data.translations.Length + " записей; русский текст хранится один раз.");
        }
    }

#pragma warning disable CS0649
    [DataContract]
    public sealed class UnifiedTranslation
    {
        [DataMember(Name = "id")] public string id;
        [DataMember(Name = "sourceEnglish")] public string sourceEnglish;
        [DataMember(Name = "russian")] public string russian;
        [DataMember(Name = "keys", EmitDefaultValue = false)] public string[] keys;
        [DataMember(Name = "dialogueTargets", EmitDefaultValue = false)] public DialogueTarget[] dialogueTargets;
        [DataMember(Name = "actorTargets", EmitDefaultValue = false)] public ActorTarget[] actorTargets;
    }

    [DataContract]
    public sealed class DialogueTarget
    {
        [DataMember(Name = "conversationId", IsRequired = true)] public int conversationId;
        [DataMember(Name = "entryId", IsRequired = true)] public int entryId;
    }

    [DataContract]
    public sealed class ActorTarget
    {
        [DataMember(Name = "id", IsRequired = true)] public int id;
        [DataMember(Name = "sourceName")] public string sourceName;
    }
#pragma warning restore CS0649
}
