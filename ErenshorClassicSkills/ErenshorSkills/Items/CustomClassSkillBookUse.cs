using System;
using HarmonyLib;
using UnityEngine.EventSystems;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// Teaches an explicitly mapped custom-class skill book without ever
    /// impersonating one of Erenshor's native classes. The native ItemIcon
    /// method is intercepted only for those mapped books so its hard-coded
    /// native-class switch cannot process them. Mouse cleanup is reproduced
    /// before the book is learned or consumed.
    /// </summary>
    internal static class CustomClassSkillBookUse
    {
        internal struct State
        {
            internal ItemIcon Icon;
            internal Item Item;
            internal Skill Skill;
            internal Stats Stats;
            internal Class ExpectedClass;
            internal int RequiredLevel;
            internal bool Suppressed;
        }

        private static readonly System.Reflection.FieldInfo RightClickTime =
            AccessTools.Field(typeof(ItemIcon), "rightClickDownTime");
        private static readonly System.Reflection.FieldInfo RightButton =
            AccessTools.Field(typeof(ItemIcon), "rButton");
        private static readonly System.Reflection.FieldInfo LeftButton =
            AccessTools.Field(typeof(ItemIcon), "lButton");
        private static readonly System.Reflection.FieldInfo DoHotkey =
            AccessTools.Field(typeof(ItemIcon), "doHotkey");
        private static readonly System.Reflection.FieldInfo LeftClickTime =
            AccessTools.Field(typeof(ItemIcon), "leftClickDownTime");
        private static readonly System.Reflection.FieldInfo LastClicked =
            AccessTools.Field(typeof(ItemIcon), "lastClicked");
        private static readonly System.Reflection.MethodInfo
            CloseSpellDetails = AccessTools.Method(typeof(ItemIcon),
                "CloseSpellDetailsWindow");

        internal static State BeginMouse(ItemIcon icon,
            PointerEventData eventData, Class expectedClass,
            int requiredLevel)
        {
            bool rightClick = eventData != null && eventData.button ==
                PointerEventData.InputButton.Right;
            if (!rightClick || icon == null || icon.DisplayOnly ||
                icon.dragging || icon.DistSlot ||
                (GameData.HighlightedItem != icon &&
                    GameData.ItemOnCursor != icon) ||
                !ReadBool(RightButton, icon) ||
                ReadFloat(RightClickTime, icon) >= 20f)
                return default;

            State state = Begin(icon, expectedClass, requiredLevel,
                rejectMouseSlot: false);
            if (state.Suppressed) FinishMouseCleanup(icon);
            return state;
        }

        internal static State BeginGamepad(ItemIcon icon,
            Class expectedClass, int requiredLevel)
        {
            if (icon == null || icon.DisplayOnly || icon.DistSlot)
                return default;
            return Begin(icon, expectedClass, requiredLevel,
                rejectMouseSlot: true);
        }

        private static State Begin(ItemIcon icon, Class expectedClass,
            int requiredLevel, bool rejectMouseSlot)
        {
            var state = new State();
            Stats stats = GameData.PlayerStats;
            Item item = icon?.MyItem;
            Skill skill = item?.TeachSkill;
            if (stats == null || expectedClass == null ||
                !ReferenceEquals(stats.CharacterClass, expectedClass) ||
                skill == null || icon.VendorSlot || icon.LootSlot ||
                (rejectMouseSlot && icon.MouseSlot) ||
                !string.IsNullOrEmpty(item.BookTitle) ||
                (item.Classes != null && item.Classes.Count > 0 &&
                    !item.Classes.Contains(expectedClass)))
                return state;

            state.Icon = icon;
            state.Item = item;
            state.Skill = skill;
            state.Stats = stats;
            state.ExpectedClass = expectedClass;
            state.RequiredLevel = requiredLevel;
            state.Suppressed = true;

            // The Harmony prefix skips the native method only for this mapped
            // custom-class book. No item metadata or player class is changed.
            return state;
        }

        internal static void Complete(State state)
        {
            Restore(state);
            if (!state.Suppressed || state.Icon == null ||
                state.Skill == null || state.Stats == null ||
                state.ExpectedClass == null ||
                !ReferenceEquals(state.Stats.CharacterClass,
                    state.ExpectedClass))
                return;

            UseSkill useSkill = GameData.PlayerControl?
                .GetComponent<UseSkill>();
            if (useSkill?.KnownSkills == null) return;

            if (useSkill.KnownSkills.Contains(state.Skill))
            {
                TryLog("You already know this skill!", "yellow");
            }
            else if (state.Stats.Level < state.RequiredLevel)
            {
                TryLog("You are not experienced enough to learn this skill yet...",
                    "yellow");
            }
            else
            {
                useSkill.KnownSkills.Add(state.Skill);
                TryLog("Learned skill: " + state.Skill.SkillName,
                    "lightblue");
                TryPresentLearnedSkill(useSkill, state.Skill);
                GameData.PlayerInv?.RemoveItemFromInv(state.Icon);
            }
        }

        internal static void Restore(State state)
        {
            if (!state.Suppressed) return;
            if (state.Item != null)
                state.Item.TeachSkill = state.Skill;
            if (state.Stats != null && state.ExpectedClass != null &&
                !ReferenceEquals(state.Stats.CharacterClass,
                    state.ExpectedClass))
            {
                state.Stats.CharacterClass = state.ExpectedClass;
                SkillsPlugin.Log?.LogWarning(
                    "Blocked an unexpected player-class change during " +
                    "custom-class skill-book use.");
            }
        }

        private static void FinishMouseCleanup(ItemIcon icon)
        {
            WriteBool(DoHotkey, icon, false);
            WriteBool(LeftButton, icon, false);
            WriteFloat(LeftClickTime, icon, 0f);
            WriteFloat(LastClicked, icon, 20f);
            try { CloseSpellDetails?.Invoke(icon, null); } catch { }
            try
            {
                if (GameData.RequireRightClickInfo)
                    GameData.ItemInfoWindow?.CloseItemWindow();
            }
            catch { }
            WriteBool(RightButton, icon, false);
            WriteFloat(RightClickTime, icon, 0f);
        }

        private static void TryPresentLearnedSkill(UseSkill useSkill,
            Skill skill)
        {
            try { useSkill.LearnSkill?.Play(); } catch { }
            try
            {
                GameData.PlayerAud?.PlayOneShot(GameData.Misc.NewSkill,
                    0.6f * GameData.SFXVol * GameData.MasterVol);
            }
            catch { }
            try
            {
                if (GameData.PlayerSkillBook?.Skillbook?.activeSelf == true)
                    GameData.PlayerSkillBook.UpdateSkillList(
                        GameData.PlayerSkillBook.GetPage());
            }
            catch { }
            try
            {
                Hotkeys blank = GameData.HKMngr?.FindBlankHotkey();
                if (blank != null)
                    GameData.HKMngr.AssignNewSkillToHK(skill, blank);
            }
            catch { }
        }

        private static void TryLog(string text, string color)
        {
            try
            {
                UpdateSocialLog.LogAdd(new ChatLogLine(text,
                    ChatLogLine.LogType.SystemMessages, color));
            }
            catch { }
        }

        private static bool ReadBool(System.Reflection.FieldInfo field,
            ItemIcon icon)
        {
            try { return field != null && (bool)field.GetValue(icon); }
            catch { return false; }
        }

        private static float ReadFloat(System.Reflection.FieldInfo field,
            ItemIcon icon)
        {
            try { return field == null ? 0f : (float)field.GetValue(icon); }
            catch { return float.MaxValue; }
        }

        private static void WriteBool(System.Reflection.FieldInfo field,
            ItemIcon icon, bool value)
        {
            try { field?.SetValue(icon, value); } catch { }
        }

        private static void WriteFloat(System.Reflection.FieldInfo field,
            ItemIcon icon, float value)
        {
            try { field?.SetValue(icon, value); } catch { }
        }
    }
}
