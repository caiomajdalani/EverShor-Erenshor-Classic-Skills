using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ErenshorSkills.Patches
{
    /// <summary>
    /// Prevents a malformed or stale status effect from aborting Stats.Update
    /// every time the native periodic-effect tick runs. The first failure for
    /// each Stats object is recorded with enough state to identify the bad
    /// effect; later repeats are swallowed quietly.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "TickEffects")]
    internal static class Patch_StatsTickEffectsCrashGuard
    {
        private static readonly HashSet<int> LoggedStats =
            new HashSet<int>();

        internal static void Reset() => LoggedStats.Clear();

        private static Exception Finalizer(Stats __instance,
            Exception __exception)
        {
            if (__exception == null) return null;
            if (!(__exception is NullReferenceException)) return __exception;

            try { LogFailureOnce(__instance,
                (NullReferenceException)__exception); }
            catch { }

            // Do not swallow failures from the base game or unrelated mods.
            // This mitigation only owns the custom Thrall update path.
            if (__instance == null ||
                __instance.GetComponent<Necromancer.NecromancerPetMarker>() == null)
                return __exception;

            // TickEffects is called from Stats.Update. Let the rest of the
            // update run (regen, timers, etc.) after a bad periodic effect
            // aborts its own tick.
            return null;
        }

        private static void LogFailureOnce(Stats stats,
            NullReferenceException exception)
        {
            int instanceId = stats != null ? stats.GetInstanceID() : 0;
            if (!LoggedStats.Add(instanceId)) return;

            string objectName = stats != null && stats.transform != null
                ? stats.transform.name : "<missing Stats>";
            Character character = stats != null ? stats.Myself : null;
            string characterName = character != null
                ? character.transform.name : "<missing Character>";
            string effects = DescribeEffects(stats);
            string currentTarget = GameData.PlayerControl?.CurrentTarget != null
                ? GameData.PlayerControl.CurrentTarget.transform.name
                : "<none>";

            SkillsPlugin.Log?.LogError(
                "Stats.TickEffects NullReferenceException (suppressed only for Bone Thralls): " +
                "object=" + objectName + "; character=" + characterName +
                "; instance=" + instanceId + "; npcNull=" +
                (character == null || character.MyNPC == null) +
                "; inventoryNull=" + (stats?.MyInv == null) +
                "; effects=" + effects + "; currentTarget=" +
                currentTarget + "; trace=" + exception);
        }

        private static string DescribeEffects(Stats stats)
        {
            if (stats == null || stats.StatusEffects == null)
                return "<array-null>";

            var entries = new List<string>();
            for (int index = 0; index < stats.StatusEffects.Length; index++)
            {
                StatusEffect status = stats.StatusEffects[index];
                if (status == null)
                {
                    entries.Add(index + ":<null-slot>");
                    continue;
                }

                Spell effect = status.Effect;
                if (effect == null) continue;
                entries.Add(index + ":" + (effect.Id ?? "<no-id>") + "/" +
                    (effect.SpellName ?? "<no-name>") + ",type=" +
                    effect.Type + ",duration=" + status.Duration +
                    ",owner=" + (status.Owner != null
                        ? status.Owner.transform.name : "<none>") +
                    ",credit=" + (status.CreditDPS != null
                        ? status.CreditDPS.transform.name : "<none>"));
            }
            return entries.Count == 0 ? "<none>" : string.Join("|", entries);
        }
    }
}
