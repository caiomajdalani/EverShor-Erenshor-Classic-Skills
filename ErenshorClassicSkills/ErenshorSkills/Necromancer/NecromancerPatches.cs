using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ErenshorSkills.Necromancer
{
    [HarmonyPatch(typeof(ClassDB), "Awake")]
    internal static class Patch_NecromancerClassDatabase
    {
        private static void Postfix()
        {
            NecromancerCatalog.EnsureClass();
        }
    }

    [HarmonyPatch(typeof(SpellDB), "Start")]
    internal static class Patch_NecromancerSpellDatabase
    {
        private static void Postfix()
        {
            NecromancerCatalog.TryInitialize();
        }
    }

    [HarmonyPatch(typeof(CharSelectManager), "Start")]
    internal static class Patch_NecromancerCharacterCreationStart
    {
        private static void Postfix(CharSelectManager __instance)
        {
            NecromancerCharacterCreation.InstallButton(__instance);
        }
    }

    [HarmonyPatch(typeof(CharSelectManager), nameof(CharSelectManager.SelectSlot))]
    internal static class Patch_NecromancerSelectSlot
    {
        private static void Postfix(CharSelectManager __instance)
        {
            NecromancerCharacterCreation.RestoreSelectedClass(__instance);
        }
    }

    [HarmonyPatch(typeof(CharSelectManager), nameof(CharSelectManager.SelectBackupSlot))]
    internal static class Patch_NecromancerSelectBackupSlot
    {
        private static void Postfix(CharSelectManager __instance)
        {
            NecromancerCharacterCreation.RestoreSelectedClass(__instance);
        }
    }

    [HarmonyPatch(typeof(CharSelectManager), nameof(CharSelectManager.Play))]
    internal static class Patch_NecromancerPlay
    {
        private static void Prefix(CharSelectManager __instance)
        {
            NecromancerCharacterCreation.RestoreSelectedClass(__instance);
        }
    }

    [HarmonyPatch(typeof(CharSelectManager), nameof(CharSelectManager.PlayAlternate))]
    internal static class Patch_NecromancerPlayAlternate
    {
        private static void Prefix(CharSelectManager __instance)
        {
            NecromancerCharacterCreation.RestoreSelectedClass(__instance);
        }
    }

    [HarmonyPatch(typeof(CharSelectManager), nameof(CharSelectManager.SaveChar))]
    internal static class Patch_NecromancerStartingItems
    {
        private static void Prefix(CharSelectManager __instance)
        {
            NecromancerCharacterCreation.GrantStartingItemsIfValid(__instance);
        }
    }

    [HarmonyPatch(typeof(RaidManager), "InitPlayerCard")]
    internal static class Patch_NecromancerPlayerClassIcon
    {
        private static void Postfix(RaidManager __instance)
        {
            NecromancerClassIconUi.ApplyPlayerCard(__instance);
        }
    }

    [HarmonyPatch(typeof(RaidManager), "AssignClassIcon")]
    internal static class Patch_NecromancerRaidClassIcon
    {
        private static void Postfix(RaidMemberSlot slot)
        {
            NecromancerClassIconUi.ApplyRaidSlot(slot);
        }
    }

    [HarmonyPatch(typeof(CastSpell), nameof(CastSpell.StartSpell),
        new[] { typeof(Spell), typeof(Stats) })]
    internal static class Patch_NecromancerCast
    {
        private static bool Prefix(CastSpell __instance, Spell _spell,
            ref Stats _target, ref bool __result)
        {
            if (!NecromancerCatalog.TryGetDefinition(_spell, out _)) return true;
            if (NecromancerRuntime.PrepareCast(__instance, _spell, ref _target,
                out string denial)) return true;
            if (!string.IsNullOrEmpty(denial))
                ChatHelper.SendCombat(denial, "#C8D0B4");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SpellVessel),
        nameof(SpellVessel.CreateSpellChargeEffect))]
    internal static class Patch_NecromancerCastStartVfx
    {
        private static void Postfix(Spell _spell, CastSpell _source,
            float _castTime)
        {
            NecromancerRuntime.OnCastStarted(_source, _spell, _castTime);
        }
    }

    [HarmonyPatch(typeof(SpellVessel), "ResolveSpell")]
    internal static class Patch_NecromancerSpellResolve
    {
        private static void Prefix(SpellVessel __instance,
            out NecromancerRuntime.ResolutionContext __state)
        {
            __state = NecromancerRuntime.CaptureResolutionContext(__instance);
        }

        private static void Postfix(SpellVessel __instance,
            NecromancerRuntime.ResolutionContext __state)
        {
            NecromancerRuntime.AfterResolve(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(Stats), nameof(Stats.AddStatusEffect),
        new[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character) })]
    internal static class Patch_NecromancerStatusVfx
    {
        private static void Postfix(Stats __instance, Spell spell, int __result)
        {
            NecromancerRuntime.OnStatusApplied(__instance, spell, __result);
        }
    }

    [HarmonyPatch(typeof(TradeWindow), nameof(TradeWindow.CompleteTrade))]
    internal static class Patch_NecromancerPetTrade
    {
        private static bool Prefix(TradeWindow __instance)
        {
            Transform tradee = __instance?.GetTradee();
            NecromancerPetMarker marker = tradee == null ? null :
                tradee.GetComponentInParent<NecromancerPetMarker>();
            if (marker == null || marker.Owner != GameData.PlayerControl?.Myself)
                return true;
            bool accepted = NecromancerPetEquipment.TryAcceptTrade(
                __instance, out string detail);
            ChatHelper.SendCombat(
                (accepted ? "Bone thrall equipment updated: " :
                    "The bone thrall refuses the trade: ") + detail + ".",
                "#C8D0B4");
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemIcon), nameof(ItemIcon.InteractItemSlot))]
    internal static class Patch_NecromancerPetTradeSnapshot
    {
        private static void Prefix(ItemIcon __instance)
        {
            NecromancerPetEquipment.CaptureTradeStart(__instance);
        }
    }

    [HarmonyPatch(typeof(TradeWindow), nameof(TradeWindow.CancelTrade))]
    internal static class Patch_NecromancerPetTradeCancel
    {
        private static void Postfix()
        {
            NecromancerPetEquipment.ClearTradeSnapshot();
        }
    }

    [HarmonyPatch(typeof(Character), "DoDeath")]
    internal static class Patch_NecromancerPetDeath
    {
        private static void Postfix(Character __instance)
        {
            NecromancerPets.OnPetDied(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "DamageMe",
        new[]
        {
            typeof(int), typeof(bool), typeof(GameData.DamageType),
            typeof(Character), typeof(bool), typeof(bool), typeof(int)
        })]
    internal static class Patch_NecromancerPetTaunt
    {
        private static void Postfix(Character __instance,
            Character _attacker, int __result)
        {
            if (__result <= 0 ||
                _attacker?.GetComponent<NecromancerPetMarker>() == null) return;
            NecromancerPets.TryGraveChallenge(__instance, _attacker,
                false, out _);
        }
    }

    [HarmonyPatch(typeof(CharmedNPC), nameof(CharmedNPC.GoAway))]
    internal static class Patch_NecromancerPetDismiss
    {
        private static void Prefix(out NPC __state)
        {
            __state = GameData.PlayerControl?.Myself?.MyCharmedNPC;
        }

        private static void Postfix(NPC __state)
        {
            if (__state?.GetComponent<NecromancerPetMarker>() != null)
                NecromancerPets.ReleasePetReference(__state, "dismissed");
        }
    }

    [HarmonyPatch(typeof(NPC), "PerformMeleeHit")]
    internal static class Patch_NecromancerPetWeaponAttack
    {
        [HarmonyPrefix, HarmonyPriority(Priority.VeryHigh)]
        private static bool Prefix(NPC __instance, bool isOffhand)
        {
            if (__instance?.GetComponent<NecromancerPetMarker>() == null)
                return true;
            if (!NecromancerPets.UsesEquippedBow(__instance))
            {
                // Erenshor's non-SimPlayer melee path ignores the
                // PerformMeleeHit baseDamage argument and rolls DamageRange
                // instead. Project the already capped pet/weapon damage into
                // that live range so equipped weapons affect actual hits.
                int damage = Mathf.Max(1, isOffhand
                    ? __instance.OHAtkDmg : __instance.BaseAtkDmg);
                __instance.DamageRange = new Vector2(damage, damage);
                NecromancerPets.RecordMeleeAttackAttempt(__instance);
                return true;
            }
            if (!isOffhand)
            {
                NecromancerPets.TryLaunchEquippedBowAttack(__instance,
                    __instance.CurrentAggroTarget, out string detail);
                if (!string.IsNullOrEmpty(detail))
                    SkillsPlugin.Log?.LogInfo(
                        "Bone Thrall bow attack: " + detail);
                Patches.SyncReflection.ResetAttackDelay(
                    __instance.GetChar()?.MyStats, true);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(WandBolt), "DeliverDamage")]
    internal static class Patch_NecromancerPetBowDelivery
    {
        private static void Prefix(WandBolt __instance, out int __state)
        {
            __state = __instance?.TargetChar?.MyStats?.CurrentHP ?? 0;
        }

        private static void Postfix(WandBolt __instance, int __state)
        {
            NecromancerPets.RecordBowProjectileDelivery(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.AggroOn))]
    internal static class Patch_NecromancerFeignAggroOn
    {
        private static bool Prefix(Character tar) =>
            !NecromancerRuntime.BlocksAggro(tar);
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.ForceAggroOn))]
    internal static class Patch_NecromancerFeignForceAggro
    {
        private static bool Prefix(Character tar) =>
            !NecromancerRuntime.BlocksAggro(tar);
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.ManageAggro))]
    internal static class Patch_NecromancerFeignManageAggro
    {
        private static bool Prefix(Character _attacker) =>
            !NecromancerRuntime.BlocksAggro(_attacker);
    }

    internal static class NecromancerCharacterCreation
    {
        private const string ButtonName = "CS_NecromancerClassButton";
        private static readonly HashSet<string> NativeClassLabels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Arcanist", "Windblade", "Druid", "Paladin",
                "Stormcaller", "Reaver"
            };
        private static readonly MethodInfo CheckUsedNames = AccessTools.Method(
            typeof(CharSelectManager), "CheckUsedNames");

        internal static void InstallButton(CharSelectManager manager)
        {
            if (SkillsPlugin.CfgEnableNecromancer?.Value != true || manager == null)
                return;
            NecromancerCatalog.TryInitialize();
            NecromancerCatalog.EnsureClass();
            if (NecromancerCatalog.NecromancerClass == null ||
                manager.CharCreate == null) return;

            Button sourceButton = manager.CharCreate
                .GetComponentsInChildren<Button>(true)
                .FirstOrDefault(candidate => string.Equals(
                    GetButtonLabel(candidate), "Reaver",
                    StringComparison.OrdinalIgnoreCase));
            if (sourceButton == null) return;
            Transform container = sourceButton.transform.parent;
            Transform existing = container.Find(ButtonName);
            if (existing != null) return;

            GameObject clone = UnityEngine.Object.Instantiate(
                sourceButton.gameObject, container, false);
            clone.name = ButtonName;
            RectTransform cloneRect = clone.GetComponent<RectTransform>();
            Button button = clone.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => SelectNecromancer(manager));
            TextMeshProUGUI text = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
            {
                text.text = NecromancerCatalog.ClassName;
                text.enableWordWrapping = false;
                text.enableAutoSizing = true;
                text.fontSizeMin = 10f;
                text.fontSizeMax = text.fontSize;
            }
            PositionClassLayout(manager, container,
                cloneRect);
            clone.SetActive(true);
            SkillsPlugin.Log.LogInfo(
                "Necromancer class option added to character creation.");
        }

        private static string GetButtonLabel(Button button)
        {
            return button?.GetComponentInChildren<TextMeshProUGUI>(true)?.text?
                .Trim() ?? string.Empty;
        }

        private static void PositionClassLayout(CharSelectManager manager,
            Transform container, RectTransform clone)
        {
            if (clone == null) return;
            List<RectTransform> native = container
                .GetComponentsInChildren<Button>(true)
                .Where(button => button.transform.parent == container &&
                    NativeClassLabels.Contains(GetButtonLabel(button)))
                .Select(button => button.GetComponent<RectTransform>())
                .Where(rect => rect != null && rect != clone)
                .ToList();
            if (native.Count < NativeClassLabels.Count)
            {
                SkillsPlugin.Log.LogWarning(
                    "Necromancer class button layout could not identify all native class buttons.");
                return;
            }

            List<float> rowCenters = new List<float>();
            foreach (float y in native.Select(rect => rect.anchoredPosition.y)
                .OrderByDescending(value => value))
            {
                if (rowCenters.All(existing => Mathf.Abs(existing - y) > 4f))
                    rowCenters.Add(y);
            }
            float rowSpacing = rowCenters.Count > 1
                ? rowCenters.Zip(rowCenters.Skip(1), (upper, lower) => upper - lower)
                    .Average()
                : clone.rect.height + 2f;
            rowSpacing = Mathf.Max(clone.rect.height + 1f, rowSpacing);
            float bottomY = native.Min(rect => rect.anchoredPosition.y);
            List<RectTransform> bottomRow = native
                .Where(rect => Mathf.Abs(rect.anchoredPosition.y - bottomY) <= 4f)
                .ToList();
            float centerX = bottomRow.Count > 0
                ? bottomRow.Average(rect => rect.anchoredPosition.x)
                : 0f;
            clone.anchoredPosition = new Vector2(centerX, bottomY - rowSpacing);

            RectTransform details = container as RectTransform;
            if (details != null)
                details.sizeDelta += new Vector2(0f, rowSpacing * 1.75f);

            RectTransform personalize = manager.Personalizer != null
                ? manager.Personalizer.GetComponent<RectTransform>()
                : null;
            if (personalize != null && personalize.parent == container)
            {
                personalize.anchoredPosition += Vector2.down * rowSpacing;
                foreach (RectTransform child in personalize
                    .Cast<Transform>()
                    .Select(item => item as RectTransform)
                    .Where(item => item != null &&
                        (item.name.StartsWith("InputField", StringComparison.Ordinal) ||
                         item.name.StartsWith("EditName", StringComparison.Ordinal))))
                    child.anchoredPosition += Vector2.up * rowSpacing;
            }

            foreach (RectTransform child in container
                .Cast<Transform>()
                .Select(item => item as RectTransform)
                .Where(item => item != null && item != personalize &&
                    item != clone && item.anchoredPosition.y < 0f))
                child.anchoredPosition += Vector2.down * rowSpacing;

            SkillsPlugin.Log.LogInfo(
                $"Necromancer class layout: button={clone.anchoredPosition}, " +
                $"rowSpacing={rowSpacing:F1}, panelHeight={details?.rect.height:F1}.");
        }

        internal static void SelectNecromancer(CharSelectManager manager)
        {
            if (manager == null || GameData.CurrentCharacterSlot == null) return;
            try
            {
                GameData.PlayerAud?.PlayOneShot(GameData.Misc.Click,
                    GameData.UIVolume * GameData.MasterVol);
            }
            catch { }
            SetClassDescription(manager);
            GameData.CurrentCharacterSlot.CharClass = NecromancerCatalog.ClassName;
            GameData.PlayerStats.CharacterClass =
                NecromancerCatalog.NecromancerClass;
            manager.selClass = NecromancerCatalog.NecromancerClass;
            GameData.PlayerStats.ClearSpentProfPoints();
            GameData.PlayerStats.CalcStats();
        }

        internal static void RestoreSelectedClass(CharSelectManager manager)
        {
            if (manager == null || GameData.CurrentCharacterSlot == null ||
                !string.Equals(GameData.CurrentCharacterSlot.CharClass,
                    NecromancerCatalog.ClassName,
                    StringComparison.OrdinalIgnoreCase)) return;
            NecromancerCatalog.TryInitialize();
            Class necromancer = NecromancerCatalog.NecromancerClass;
            if (necromancer == null) return;
            GameData.PlayerStats.CharacterClass = necromancer;
            manager.PlayerStats.CharacterClass = necromancer;
            manager.selClass = necromancer;
            if (manager.CharCreate != null && manager.CharCreate.activeInHierarchy &&
                manager.ClassDesc != null)
                SetClassDescription(manager);
            if (manager.CharInfo != null && manager.PlayerStats != null &&
                !string.IsNullOrEmpty(manager.PlayerStats.MyName))
                manager.CharInfo.text = manager.PlayerStats.MyName + " - Level " +
                    manager.PlayerStats.Level + " " + necromancer.DisplayName +
                    "\n" + GameData.SceneName;
            try { manager.PlayerStats.CalcStats(); } catch { }
        }

        internal static void GrantStartingItemsIfValid(CharSelectManager manager)
        {
            if (manager?.selClass == null ||
                !NecromancerCatalog.IsNecromancerClass(manager.selClass) ||
                GameData.CurrentCharacterSlot == null || manager.Name == null)
                return;
            string name = manager.Name.text;
            if (string.IsNullOrWhiteSpace(name) || name == "Name Character" ||
                !Regex.IsMatch(name, "^[a-zA-Z0-9 ]+$")) return;
            try
            {
                if (CheckUsedNames != null &&
                    (bool)CheckUsedNames.Invoke(manager, new object[] { name })) return;
            }
            catch { return; }

            NecromancerCatalog.TryInitialize();
            FreshCharacterState.ClearInheritedAura();
            IEnumerable<Item> nativeGear = manager.ArcanistStart ?? new List<Item>();
            foreach (Item item in nativeGear.Where(item => item != null &&
                item.TeachSpell == null && item.TeachSkill == null))
                AddOnce(item);
            // Supply both legal starter weapon families explicitly. One can
            // remain with the Necromancer while the other is placed in the
            // Thrall Bag to arm the first bone thrall immediately.
            AddOnce(Items.ItemFactory.ResolveItem("Rusty Dagger"));
            AddOnce(Items.ItemFactory.ResolveItem("Rusty Shortsword"));
            AddOnce(Items.ItemFactory.ResolveItem(
                Items.BagSystem.ThrallBagName));
            AddOnce(NecromancerCatalog.GetScroll("bone_thrall_1"));
            AddOnce(NecromancerCatalog.GetScroll("siphon_spark"));
            AddOnce(NecromancerCatalog.GetScroll("rotting_fever"));
        }

        internal static bool ValidateStarterWeapons(out string detail)
        {
            Item dagger = Items.ItemFactory.ResolveItem("Rusty Dagger");
            Item sword = Items.ItemFactory.ResolveItem("Rusty Shortsword");
            bool valid = dagger != null && sword != null &&
                dagger != sword &&
                dagger.ThisWeaponType == Item.WeaponType.OneHandDagger &&
                sword.ThisWeaponType == Item.WeaponType.OneHandMelee;
            detail = valid
                ? $"starter loadout grants {dagger.ItemName} and {sword.ItemName}"
                : $"starter weapon resolution invalid: dagger=" +
                  $"{dagger?.ItemName ?? "<missing>"}, sword=" +
                  $"{sword?.ItemName ?? "<missing>"}";
            return valid;
        }

        private static void SetClassDescription(CharSelectManager manager)
        {
            TextMeshProUGUI description = manager?.ClassDesc;
            if (description == null) return;
            description.enableWordWrapping = true;
            description.enableAutoSizing = true;
            description.fontSizeMin = 14f;
            description.fontSizeMax = Mathf.Max(14f, description.fontSize);
            description.overflowMode = TextOverflowModes.Truncate;
            description.text = NecromancerCatalog.ClassDescription;
        }

        private static void AddOnce(Item item)
        {
            if (item == null || GameData.PlayerInv == null) return;
            bool present = GameData.PlayerInv.StoredSlots?.Any(slot =>
                slot?.MyItem == item) == true;
            if (!present && !GameData.PlayerInv.AddItemToInv(item))
                GameData.PlayerInv.ForceItemToInv(item);
        }
    }

    internal static class NecromancerClassIconUi
    {
        // Native class glyphs are white medallions tinted by RaidManager.
        // Violet distinguishes Necromancer without changing that UI language.
        private static readonly Color NecromancerColor =
            new Color(0.68f, 0.37f, 0.88f, 1f);

        internal static void ApplyPlayerCard(RaidManager manager)
        {
            if (manager?.PlayerNameCard == null ||
                !NecromancerCatalog.IsNecromancerClass(
                    GameData.PlayerStats?.CharacterClass)) return;
            if (manager.PlayerNameCard.PlayerIcon != null)
            {
                manager.PlayerNameCard.PlayerIcon.sprite =
                    NecromancerCatalog.ClassIcon;
                manager.PlayerNameCard.PlayerIcon.color = NecromancerColor;
            }
            if (manager.PlayerNameCard.MyNamePlate != null)
                manager.PlayerNameCard.MyNamePlate.color = NecromancerColor;
        }

        internal static void ApplyRaidSlot(RaidMemberSlot slot)
        {
            if (slot?.AssignedAvatar == null ||
                !NecromancerCatalog.IsNecromancerClass(
                    slot.AssignedAvatar.MyStats?.CharacterClass)) return;
            if (slot.ClassIcon != null)
            {
                slot.ClassIcon.sprite = NecromancerCatalog.ClassIcon;
                slot.ClassIcon.color = NecromancerColor;
            }
            if (slot.MyNameplate != null)
                slot.MyNameplate.color = NecromancerColor;
        }
    }
}
