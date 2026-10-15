using HarmonyLib;

namespace RTLC.BO.RussianTranslate
{
    [HarmonyPatch(typeof(QuestInfo), nameof(QuestInfo.UpdateQuestInfo))]
    internal static class QuestInfoInitialization
    {
        private static readonly System.Reflection.FieldInfo Manager = AccessTools.Field(typeof(QuestInfo), "GM");
        private static readonly System.Reflection.FieldInfo Quests = AccessTools.Field(typeof(QuestInfo), "questManager");

        private static void Prefix(QuestInfo __instance)
        {
            // Selecting the first enabled quest occurs synchronously in OnEnable,
            // before newly activated QuestInfo components receive their Start.
            GameManager manager = GameManager.Instance;
            if (manager == null) return;
            if (Manager.GetValue(__instance) as GameManager == null) Manager.SetValue(__instance, manager);
            if (Quests.GetValue(__instance) as QuestManager == null) Quests.SetValue(__instance, manager.QuestManager);
        }
    }
}
