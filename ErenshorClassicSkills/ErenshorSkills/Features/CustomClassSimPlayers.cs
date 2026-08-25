using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Features
{
    /// <summary>
    /// Adds persistent Necromancer and Monk members to Erenshor's SimPlayer
    /// population without changing the game's six-class save schema. A small
    /// loader-neutral sidecar owns the custom identity while native saves keep
    /// a safe Arcanist or Duelist fallback.
    /// </summary>
    internal static class CustomClassSimPlayers
    {
        internal const string NecromancerName =
            Necromancer.NecromancerCatalog.ClassName;
        internal const string MonkName = Monk.MonkCatalog.ClassName;
        private const string FileName = "custom_sim_classes.json";
        private const int PopulationDivisor = 8;
        private const int PayloadMagic = 0x53435343; // "CSCS"
        private const int PayloadVersion = 1;
        private const int MaximumAssignments = 4096;
        private const int MaximumPayloadBytes = 1024 * 1024;
        private const int MaximumNameLength = 512;

        internal sealed class Assignment
        {
            public string SimName;
            public string ClassName;
        }

        [Serializable]
        internal sealed class AssignmentFile
        {
            public int Version = 1;
            public string AssignmentsPayload = "";
        }

        private static readonly Dictionary<string, string> Assignments =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        private static bool _suppressQaSave;

        private static string SavePath =>
            Path.Combine(RuntimePaths.DataDirectory, FileName);

        internal static void ReconcileCurrentPopulation()
        {
            SimPlayerMngr manager = GameData.SimMngr;
            if (manager?.Sims == null || manager.Sims.Count == 0) return;

            EnsurePopulation(manager.Sims);
            MethodInfo categorize = AccessTools.Method(typeof(SimPlayerMngr),
                "CategorizeAll");
            categorize?.Invoke(manager, null);

            foreach (SimPlayer sim in manager.ActiveSimInstances.ToArray())
            {
                if (sim == null) continue;
                ApplyRuntimeClass(sim);
                sim.LoadSimSkills();
                sim.LoadSimSpells();
                AddChatter(sim.GetComponent<SimPlayerLanguage>(),
                    GetClassName(sim.GetThisNPC()?.NPCName));
            }
        }

        internal static void EnsurePopulation(List<SimPlayerTracking> sims)
        {
            Load();
            if (sims == null || sims.Count == 0) return;

            List<SimPlayerTracking> eligible = sims.Where(IsEligible)
                .OrderBy(sim => StableHash(sim.SimName))
                .ThenBy(sim => sim.SimName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (eligible.Count == 0) return;

            bool changed = false;
            if (SkillsPlugin.CfgEnableNecromancer?.Value == true)
                changed |= FillClass(eligible, NecromancerName);
            if (SkillsPlugin.CfgEnableMonk?.Value == true)
                changed |= FillClass(eligible, MonkName);

            foreach (SimPlayerTracking sim in eligible)
            {
                if (TryGetClassName(sim.SimName, out string className) &&
                    IsEnabled(className))
                    sim.ClassName = className;
            }

            if (changed) Save();
            int necromancers = eligible.Count(sim =>
                string.Equals(sim.ClassName, NecromancerName,
                    StringComparison.OrdinalIgnoreCase));
            int monks = eligible.Count(sim =>
                string.Equals(sim.ClassName, MonkName,
                    StringComparison.OrdinalIgnoreCase));
            SkillsPlugin.Log?.LogInfo(
                $"Custom SimPlayers: assigned Necromancer={necromancers}, " +
                $"Monk={monks}, eligible={eligible.Count}.");
        }

        private static bool FillClass(List<SimPlayerTracking> eligible,
            string className)
        {
            int target = Math.Max(1, eligible.Count / PopulationDivisor);
            int current = eligible.Count(sim =>
                TryGetClassName(sim.SimName, out string assigned) &&
                string.Equals(assigned, className,
                    StringComparison.OrdinalIgnoreCase));
            if (current >= target) return false;

            bool changed = false;
            foreach (SimPlayerTracking sim in eligible)
            {
                if (current >= target) break;
                if (Assignments.ContainsKey(sim.SimName)) continue;
                Assignments[sim.SimName] = className;
                sim.ClassName = className;
                current++;
                changed = true;
            }
            return changed;
        }

        private static bool IsEligible(SimPlayerTracking sim)
        {
            return sim != null && !string.IsNullOrWhiteSpace(sim.SimName) &&
                !sim.IsGMCharacter && !sim.Rival && sim.TiedToSlot != 99;
        }

        internal static bool TryGetClassName(string simName,
            out string className)
        {
            Load();
            if (!string.IsNullOrWhiteSpace(simName) &&
                Assignments.TryGetValue(simName, out className) &&
                IsCustomClass(className))
                return true;
            className = null;
            return false;
        }

        private static string GetClassName(string simName)
        {
            return TryGetClassName(simName, out string value) &&
                IsEnabled(value) ? value : null;
        }

        private static bool IsCustomClass(string className)
        {
            return string.Equals(className, NecromancerName,
                       StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, MonkName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEnabled(string className)
        {
            if (string.Equals(className, NecromancerName,
                    StringComparison.OrdinalIgnoreCase))
                return SkillsPlugin.CfgEnableNecromancer?.Value == true;
            if (string.Equals(className, MonkName,
                    StringComparison.OrdinalIgnoreCase))
                return SkillsPlugin.CfgEnableMonk?.Value == true;
            return false;
        }

        internal static void ApplyRuntimeClass(SimPlayer sim)
        {
            if (sim?.MyStats == null ||
                !TryGetClassName(sim.GetThisNPC()?.NPCName,
                    out string className) || !IsEnabled(className)) return;

            Class customClass;
            if (string.Equals(className, NecromancerName,
                    StringComparison.OrdinalIgnoreCase))
            {
                Necromancer.NecromancerCatalog.TryInitialize();
                customClass = Necromancer.NecromancerCatalog.NecromancerClass;
            }
            else
            {
                Monk.MonkCatalog.TryInitialize();
                customClass = Monk.MonkCatalog.MonkClass;
            }
            if (customClass == null) return;

            sim.MyStats.CharacterClass = customClass;
            if (sim.myIndex >= 0 && GameData.SimMngr?.Sims != null &&
                sim.myIndex < GameData.SimMngr.Sims.Count)
                GameData.SimMngr.Sims[sim.myIndex].ClassName = className;
            try { sim.MyStats.CalcStats(); } catch { }
        }

        internal static void AddCustomSkills(SimPlayer sim)
        {
            if (sim?.MyStats == null || sim.GetThisNPC() == null ||
                !TryGetClassName(sim.GetThisNPC().NPCName,
                    out string className) || !IsEnabled(className)) return;

            IEnumerable<Skill> skills;
            if (string.Equals(className, MonkName,
                    StringComparison.OrdinalIgnoreCase))
            {
                skills = Monk.MonkCatalog.AllSkills.Where(skill =>
                    skill != null && skill.DuelistRequiredLevel > 0 &&
                    sim.MyStats.Level >= skill.DuelistRequiredLevel);
            }
            else
            {
                skills = (GameData.SkillDatabase?.SkillDatabase ??
                    Array.Empty<Skill>()).Where(skill =>
                    Necromancer.NecromancerSharedSkills.TryGetDefinition(
                        skill, out var definition) &&
                    sim.MyStats.Level >= definition.RequiredLevel);
            }

            NPC npc = sim.GetThisNPC();
            foreach (Skill skill in skills)
            {
                if (skill.TypeOfSkill == Skill.SkillType.Attack ||
                    skill.TypeOfSkill == Skill.SkillType.Ranged)
                {
                    if (!npc.MyAttackSkills.Contains(skill))
                        npc.MyAttackSkills.Add(skill);
                }
                if (skill.TypeOfSkill == Skill.SkillType.Utility &&
                    skill.StanceToUse != null &&
                    !sim.KnownStances.Contains(skill.StanceToUse))
                    sim.KnownStances.Add(skill.StanceToUse);
                if (!sim.Skillbook.Any(slot => slot?.skill == skill))
                    sim.Skillbook.Add(new SimPlayerSkillSlot(skill));
            }
            sim.Skillbook = sim.Skillbook.Where(slot => slot?.skill != null)
                .GroupBy(slot => slot.skill.Id)
                .Select(group => group.First()).ToList();
        }

        internal static void ApplyNativeFallback(SimPlayerSaveData data)
        {
            if (data == null || !TryGetClassName(data.NPCName,
                    out string className) || !IsEnabled(className)) return;
            data.War = false;
            data.Arc = false;
            data.Dru = false;
            data.Duel = false;
            data.Storm = false;
            data.Reav = false;
            if (string.Equals(className, NecromancerName,
                    StringComparison.OrdinalIgnoreCase))
                data.Arc = true;
            else
                data.Duel = true;
        }

        internal static void AddRoles(List<string> cc, List<string> dps)
        {
            HashSet<string> currentNames = new HashSet<string>(
                GameData.SimMngr?.Sims?.Where(sim => sim != null)
                    .Select(sim => sim.SimName) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> assignment in Assignments)
            {
                if (!IsEnabled(assignment.Value) ||
                    !currentNames.Contains(assignment.Key)) continue;
                if (string.Equals(assignment.Value, NecromancerName,
                        StringComparison.OrdinalIgnoreCase))
                    AddOnce(cc, assignment.Key);
                AddOnce(dps, assignment.Key);
            }
        }

        internal static void AddChatter(SimPlayerLanguage language,
            string className)
        {
            if (language == null) return;

            AddOnce(language.Impressed,
                "That Necromancer's bone thrall hits harder than it looks.");
            AddOnce(language.Impressed,
                "Monks make that footwork look easy.");
            AddOnce(language.Exclamations,
                "Did you see that Monk strike?");
            AddOnce(language.Exclamations,
                "That Necromancer brought the whole graveyard.");
            AddOnce(language.GenericLines,
                "I hear Necromancers and Monks are joining groups now.");

            if (string.Equals(className, NecromancerName,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddOnce(language.Greetings,
                    "Hail. The dead have been restless tonight.");
                AddOnce(language.ReturnGreeting,
                    "Back again? Good. The grave keeps poor company.");
                AddOnce(language.LFGPublic,
                    "Necromancer LFG - damage, fear, and a bone thrall ready.");
                AddOnce(language.Aggro,
                    "My thrall has it - burn it down!");
                AddOnce(language.Died,
                    "Apparently death still has a few rules.");
                AddOnce(language.LevelUpCelebration,
                    "Another secret pried from the grave.");
                AddOnce(language.GoodLastOuting,
                    "That was a fine hunt. Even the dead approved.");
            }
            else if (string.Equals(className, MonkName,
                         StringComparison.OrdinalIgnoreCase))
            {
                AddOnce(language.Greetings,
                    "Hail. Keep your footing and your breathing steady.");
                AddOnce(language.ReturnGreeting,
                    "Welcome back. Ready for another lesson?");
                AddOnce(language.LFGPublic,
                    "Monk LFG - fast hands and no need to roll on swords.");
                AddOnce(language.Aggro,
                    "I have the opening - strike now!");
                AddOnce(language.Died,
                    "I mistimed that dodge.");
                AddOnce(language.LevelUpCelebration,
                    "Discipline turns effort into mastery.");
                AddOnce(language.GoodLastOuting,
                    "A clean victory. Our timing was excellent.");
            }
        }

        private static void AddOnce(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value)) return;
            if (!list.Contains(value)) list.Add(value);
        }

        internal static void RemoveAssignment(string simName)
        {
            Load();
            if (!string.IsNullOrWhiteSpace(simName) &&
                Assignments.Remove(simName))
                Save();
        }

        internal static bool RunSelfTest(out string detail)
        {
            SimPlayerMngr manager = GameData.SimMngr;
            if (manager?.Sims == null || manager.Sims.Count == 0)
            {
                detail = "SimPlayer manager is not loaded";
                return false;
            }

            EnsurePopulation(manager.Sims);
            var errors = new List<string>();
            int necro = manager.Sims.Count(sim =>
                string.Equals(sim.ClassName, NecromancerName,
                    StringComparison.OrdinalIgnoreCase));
            int monk = manager.Sims.Count(sim =>
                string.Equals(sim.ClassName, MonkName,
                    StringComparison.OrdinalIgnoreCase));
            if (SkillsPlugin.CfgEnableNecromancer?.Value == true && necro == 0)
                errors.Add("no Necromancer assignments");
            if (SkillsPlugin.CfgEnableMonk?.Value == true && monk == 0)
                errors.Add("no Monk assignments");

            List<string> cc = GetRoleList(manager, "CC");
            List<string> dps = GetRoleList(manager, "DPS");
            if (manager.Sims.Any(sim =>
                    string.Equals(sim.ClassName, NecromancerName,
                        StringComparison.OrdinalIgnoreCase) &&
                    (cc == null || !cc.Contains(sim.SimName))))
                errors.Add("Necromancer missing CC role");
            if (manager.Sims.Any(sim => IsCustomClass(sim.ClassName) &&
                    (dps == null || !dps.Contains(sim.SimName))))
                errors.Add("custom Sim missing DPS role");

            ProbeClass(manager, NecromancerName, errors);
            ProbeClass(manager, MonkName, errors);
            if (!ValidatePersistedAssignments(out string persistenceDetail))
                errors.Add(persistenceDetail);

            bool pass = errors.Count == 0;
            detail = pass
                ? $"Necromancer={necro}, Monk={monk}; persistent classes, " +
                  "native fallbacks, combat kits, roles, and class chatter verified"
                : string.Join("; ", errors);
            return pass;
        }

        private static void ProbeClass(SimPlayerMngr manager,
            string className, List<string> errors)
        {
            if (!IsEnabled(className)) return;
            SimPlayerTracking tracking = manager.Sims.FirstOrDefault(sim =>
                string.Equals(sim.ClassName, className,
                    StringComparison.OrdinalIgnoreCase));
            if (tracking == null) return;

            var fallback = new SimPlayerSaveData(tracking.SimName, 1,
                new List<Item>(), 1f);
            ApplyNativeFallback(fallback);
            if (string.Equals(className, NecromancerName,
                    StringComparison.OrdinalIgnoreCase) &&
                (!fallback.Arc || fallback.Duel))
                errors.Add("Necromancer fallback is not Arcanist");
            if (string.Equals(className, MonkName,
                    StringComparison.OrdinalIgnoreCase) &&
                (!fallback.Duel || fallback.Arc))
                errors.Add("Monk fallback is not Duelist");

            SimPlayer original = tracking.MyAvatar;
            SimPlayer probe = original;
            bool created = false;
            try
            {
                if (probe == null)
                {
                    _suppressQaSave = true;
                    Vector3 position = GameData.PlayerControl != null
                        ? GameData.PlayerControl.transform.position +
                          new Vector3(string.Equals(className, MonkName,
                              StringComparison.OrdinalIgnoreCase) ? 2f : -2f,
                              0f, 2f)
                        : Vector3.zero;
                    probe = tracking.SpawnMeInGame(position, tracking);
                    created = probe != null;
                }

                Class expected = string.Equals(className, NecromancerName,
                    StringComparison.OrdinalIgnoreCase)
                    ? Necromancer.NecromancerCatalog.NecromancerClass
                    : Monk.MonkCatalog.MonkClass;
                if (probe?.MyStats?.CharacterClass != expected)
                    errors.Add(className + " runtime class did not load");
                if (probe?.Spellbook == null || probe.Spellbook.Count == 0)
                    errors.Add(className + " spellbook is empty");
                if (string.Equals(className, MonkName,
                        StringComparison.OrdinalIgnoreCase) &&
                    (probe?.Skillbook == null || probe.Skillbook.Count == 0))
                    errors.Add("Monk skillbook is empty");
                if (!HasClassChatter(probe?.GetComponent<SimPlayerLanguage>(),
                        className))
                    errors.Add(className + " chatter missing");
            }
            catch (Exception ex)
            {
                errors.Add(className + " probe failed: " + ex.Message);
            }
            finally
            {
                _suppressQaSave = false;
                if (created && probe != null)
                {
                    probe.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(probe.gameObject);
                    tracking.MyAvatar = original;
                }
            }
        }

        private static bool HasClassChatter(SimPlayerLanguage language,
            string className)
        {
            if (language == null) return false;
            string expected = string.Equals(className, NecromancerName,
                StringComparison.OrdinalIgnoreCase)
                ? "Another secret pried from the grave."
                : "Discipline turns effort into mastery.";
            return language.LevelUpCelebration?.Contains(expected) == true &&
                language.Impressed?.Contains(
                    "Monks make that footwork look easy.") == true &&
                language.Impressed?.Contains(
                    "That Necromancer's bone thrall hits harder than it looks.") == true;
        }

        private static bool ValidatePersistedAssignments(out string detail)
        {
            detail = null;
            try
            {
                if (!File.Exists(SavePath))
                {
                    detail = "custom-class sidecar is missing";
                    return false;
                }
                string json = File.ReadAllText(SavePath);
                AssignmentFile data = JsonUtility.FromJson<AssignmentFile>(json);
                List<Assignment> persisted = null;
                if (data == null || !TryDecodeAssignments(
                        data.AssignmentsPayload, out persisted) ||
                    persisted.Count != Assignments.Count)
                {
                    detail = "custom-class sidecar round trip mismatch: " +
                        $"disk={(data == null ? -1 : persisted?.Count ?? -1)}, " +
                        $"memory={Assignments.Count}";
                    return false;
                }
                foreach (KeyValuePair<string, string> assignment in Assignments)
                {
                    if (!persisted.Any(value => value != null &&
                            string.Equals(value.SimName, assignment.Key,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(value.ClassName, assignment.Value,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        detail = "custom-class sidecar lost assignment for " +
                            assignment.Key;
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                detail = "custom-class sidecar round trip failed: " + ex.Message;
                return false;
            }
        }

        private static List<string> GetRoleList(SimPlayerMngr manager,
            string name)
        {
            return AccessTools.Field(typeof(SimPlayerMngr), name)
                ?.GetValue(manager) as List<string>;
        }

        internal static bool SuppressQaSave => _suppressQaSave;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            Assignments.Clear();
            if (!File.Exists(SavePath)) return;
            if (!SafeFileIO.TryReadAllTextWithBackup(SavePath, IsValidJson,
                    out string json, out bool recovered, out string error))
            {
                SkillsPlugin.Log?.LogWarning(
                    "Custom SimPlayer assignments could not be loaded: " +
                    error);
                return;
            }

            AssignmentFile data = JsonUtility.FromJson<AssignmentFile>(json);
            if (data == null || !TryDecodeAssignments(
                    data.AssignmentsPayload, out List<Assignment> persisted))
                return;
            foreach (Assignment assignment in persisted)
            {
                if (assignment == null ||
                    string.IsNullOrWhiteSpace(assignment.SimName) ||
                    !IsCustomClass(assignment.ClassName)) continue;
                Assignments[assignment.SimName] = assignment.ClassName;
            }
            if (recovered)
                SkillsPlugin.Log?.LogWarning(
                    "Recovered custom SimPlayer assignments from backup.");
        }

        private static bool IsValidJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                AssignmentFile data = JsonUtility.FromJson<AssignmentFile>(json);
                return data != null && data.Version == 1 &&
                    TryDecodeAssignments(data.AssignmentsPayload,
                        out List<Assignment> persisted) &&
                    persisted.All(value =>
                        value != null &&
                        !string.IsNullOrWhiteSpace(value.SimName) &&
                        IsCustomClass(value.ClassName));
            }
            catch { return false; }
        }

        private static void Save()
        {
            var data = new AssignmentFile
            {
                AssignmentsPayload = EncodeAssignments()
            };
            string json = JsonUtility.ToJson(data, true);
            if (!SafeFileIO.TryWriteAllTextAtomic(SavePath, json,
                    out string error))
                SkillsPlugin.Log?.LogError(
                    "Custom SimPlayer assignments could not be saved: " +
                    error);
        }

        private static string EncodeAssignments()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream,
                new UTF8Encoding(false, true), true))
            {
                writer.Write(PayloadMagic);
                writer.Write(PayloadVersion);
                List<KeyValuePair<string, string>> ordered = Assignments
                    .OrderBy(value => value.Key,
                        StringComparer.OrdinalIgnoreCase).ToList();
                writer.Write(ordered.Count);
                foreach (KeyValuePair<string, string> assignment in ordered)
                {
                    writer.Write(assignment.Key ?? string.Empty);
                    writer.Write(assignment.Value ?? string.Empty);
                }
                writer.Flush();
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static bool TryDecodeAssignments(string payload,
            out List<Assignment> assignments)
        {
            assignments = new List<Assignment>();
            try
            {
                if (string.IsNullOrWhiteSpace(payload)) return false;
                byte[] bytes = Convert.FromBase64String(payload);
                if (bytes.Length <= 0 || bytes.Length > MaximumPayloadBytes)
                    return false;
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream,
                    new UTF8Encoding(false, true), true))
                {
                    if (reader.ReadInt32() != PayloadMagic ||
                        reader.ReadInt32() != PayloadVersion)
                        return false;
                    int count = reader.ReadInt32();
                    if (count < 0 || count > MaximumAssignments) return false;
                    for (int index = 0; index < count; index++)
                    {
                        string simName = reader.ReadString();
                        string className = reader.ReadString();
                        if (string.IsNullOrWhiteSpace(simName) ||
                            simName.Length > MaximumNameLength ||
                            className.Length > MaximumNameLength ||
                            !IsCustomClass(className))
                            return false;
                        assignments.Add(new Assignment
                        {
                            SimName = simName,
                            ClassName = className
                        });
                    }
                    return stream.Position == stream.Length;
                }
            }
            catch
            {
                assignments.Clear();
                return false;
            }
        }

        private static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in value ?? string.Empty)
                {
                    hash ^= char.ToUpperInvariant(character);
                    hash *= 16777619;
                }
                return hash;
            }
        }
    }

    [HarmonyPatch(typeof(SimPlayerMngr), "CategorizeAll")]
    internal static class Patch_CustomSimPlayerCategorization
    {
        private static void Prefix(SimPlayerMngr __instance)
        {
            CustomClassSimPlayers.EnsurePopulation(__instance.Sims);
        }

        private static void Postfix(List<string> ___CC, List<string> ___DPS)
        {
            CustomClassSimPlayers.AddRoles(___CC, ___DPS);
        }
    }

    [HarmonyPatch(typeof(SimPlayer), "LoadSimData")]
    internal static class Patch_CustomSimPlayerClassLoad
    {
        private static void Postfix(SimPlayer __instance)
        {
            CustomClassSimPlayers.ApplyRuntimeClass(__instance);
        }
    }

    [HarmonyPatch(typeof(SimPlayer), nameof(SimPlayer.LoadSimSkills))]
    internal static class Patch_CustomSimPlayerSkillLoad
    {
        private static void Postfix(SimPlayer __instance)
        {
            CustomClassSimPlayers.AddCustomSkills(__instance);
        }
    }

    [HarmonyPatch(typeof(SimPlayerDataManager),
        nameof(SimPlayerDataManager.SaveSimData))]
    internal static class Patch_CustomSimPlayerSave
    {
        private static bool Prefix(SimPlayerSaveData _data)
        {
            if (CustomClassSimPlayers.SuppressQaSave) return false;
            CustomClassSimPlayers.ApplyNativeFallback(_data);
            return true;
        }
    }

    [HarmonyPatch(typeof(SimPlayerDataManager),
        nameof(SimPlayerDataManager.DeleteSimSaveData))]
    internal static class Patch_CustomSimPlayerDelete
    {
        private static void Postfix(string npcName)
        {
            CustomClassSimPlayers.RemoveAssignment(npcName);
        }
    }

    [HarmonyPatch(typeof(SimPlayerTracking),
        nameof(SimPlayerTracking.SpawnMeInGame))]
    internal static class Patch_CustomSimPlayerChatter
    {
        private static void Postfix(SimPlayerTracking __instance,
            SimPlayer __result)
        {
            string className = CustomClassSimPlayers.TryGetClassName(
                __instance?.SimName, out string assigned) ? assigned : null;
            CustomClassSimPlayers.AddChatter(
                __result?.GetComponent<SimPlayerLanguage>(), className);
        }
    }
}
