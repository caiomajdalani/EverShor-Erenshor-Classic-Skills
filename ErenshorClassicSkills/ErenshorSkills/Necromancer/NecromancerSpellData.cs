using System;
using System.Collections.Generic;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    internal enum NecromancerSpellKind
    {
        Native,
        SummonPet,
        PetHeal,
        HealthToMana,
        ReclaimPet,
        FeignDeath,
        SoulTransfusion,
        PetBuff,
        Harmshield
    }

    internal enum NecromancerVfxStyle
    {
        Bone,
        Drain,
        Disease,
        Shadow,
        Mend,
        Fear,
        Blood,
        Curse,
        Death,
        Cloud,
        Ward,
        Fire
    }

    internal sealed class NecromancerSpellDefinition
    {
        internal string Key;
        internal string Name;
        internal string Description;
        internal string IconTheme;
        internal int Level;
        internal Spell.SpellType Type;
        internal Spell.SpellLine Line;
        internal GameData.DamageType DamageType;
        internal NecromancerSpellKind Kind;
        internal NecromancerVfxStyle VfxStyle;
        internal Color VfxColor;
        internal int ManaCost;
        internal int TargetDamage;
        internal int DurationTicks;
        internal int Aggro;
        internal int CustomAmount;
        internal int PetTier;
        internal int Hp;
        internal int Ac;
        internal int Str;
        internal int Pr;
        internal int Vr;
        internal int DamageShield;
        internal float MovementSpeed;
        internal float CastSeconds;
        internal float CooldownSeconds;
        internal float Range = 60f;
        internal float ResistModifier;
        internal bool Lifetap;
        internal bool Fear;
        internal bool Root;
        internal bool SelfOnly;
        internal bool Pbaoe;
        internal bool BreakOnAnyAction;
        internal bool GrantInvisibility;
        internal bool HiddenEffect;
        internal bool SimUsable = true;
        internal string AppliedEffectKey;
    }

    /// <summary>
    /// Authoritative Necromancer progression. Erenshor's native classes do not
    /// all have the same scroll count. The July 2026 Reaver has 35 learnable
    /// abilities, so this kit mirrors that count while keeping a distinct
    /// pet/attrition identity. Damage-over-time values are per three-second
    /// game tick.
    /// </summary>
    internal static class NecromancerSpellData
    {
        internal const int ExpectedTotalSpellCount = 37;
        internal const int ExpectedLearnableSpellCount = 35;

        internal static readonly string[] DisallowedRankSuffixes =
        {
            " I", " II", " III", " IV", " V", " VI", " VII", " VIII",
            " IX", " X"
        };

        internal static readonly IReadOnlyList<NecromancerSpellDefinition> All =
            new List<NecromancerSpellDefinition>
            {
                Pet("bone_thrall_1", "Cavorting Bones", 1, 35, 1,
                    "Raises a fragile skeletal warrior. Bone thralls can wield a weapon and carry a shield."),
                Damage("siphon_spark", "Siphon Spark", 1, 20, 20, true,
                    "Damages the target and heals the caster for the damage dealt.", "Lifetap",
                    NecromancerVfxStyle.Drain, new Color(0.48f, 0.12f, 0.72f)),
                Dot("rotting_fever", "Rotting Fever", 1, 18, 7, 6,
                    GameData.DamageType.Poison, Spell.SpellLine.Global_Poison_DOT,
                    "Afflicts the target with an efficient disease that worsens over time.",
                    "Disease", NecromancerVfxStyle.Disease,
                    new Color(0.36f, 0.72f, 0.18f)),

                Buff("bone_armor_1", "Bone Armor", 2, 25, 10,
                    "Wraps the caster in grave-bound bone for thirty seconds, granting 35 health and 12 armor.",
                    "Pet Ward", NecromancerVfxStyle.Bone,
                    new Color(0.70f, 0.78f, 0.66f), hp: 35, ac: 12),

                Dot("clinging_shadow", "Clinging Shadow", 3, 45, 12, 6,
                    GameData.DamageType.Void, Spell.SpellLine.Reav_Movespeed,
                    "Darkness gnaws at the target and slows its movement.",
                    "Shadow", NecromancerVfxStyle.Shadow,
                    new Color(0.18f, 0.08f, 0.36f), movement: -0.25f),
                Special("mend_bones", "Mend Bones", 4, 45,
                    NecromancerSpellKind.PetHeal, 35,
                    "Repairs at least one quarter of the Necromancer's active bone thrall's maximum health.", "Pet Mend",
                    NecromancerVfxStyle.Mend, new Color(0.58f, 0.86f, 0.58f),
                    cast: 1.5f, cooldown: 6f),
                Damage("noxious_bolt", "Noxious Bolt", 5, 70, 85, false,
                    "Hurls a concentrated bolt of grave poison at the target.",
                    "Disease", NecromancerVfxStyle.Disease,
                    new Color(0.42f, 0.76f, 0.16f), cast: 1.75f,
                    cooldown: 3f, damageType: GameData.DamageType.Poison),
                Status("fear", "Fear", 6, 80, 2,
                    Spell.SpellLine.Reav_Fear, GameData.DamageType.Magic,
                    "Fills a non-boss target with terror for up to six seconds.",
                    "Fear", NecromancerVfxStyle.Fear,
                    new Color(0.56f, 0.16f, 0.82f), fear: true,
                    cooldown: 18f, resist: -5f),
                Buff("gather_shadows", "Gather Shadows", 7, 45, 20,
                    "Draws darkness around the caster, granting invisibility for up to one minute.",
                    "Shadow", NecromancerVfxStyle.Shadow,
                    new Color(0.16f, 0.08f, 0.32f), grantInvisibility: true,
                    cooldown: 8f, simUsable: false),

                Pet("bone_thrall_2", "Risen Sentry", 8, 180, 2,
                    "Raises a sturdier skeletal warrior with improved offense and defenses."),
                Dot("venomous_blood", "Venomous Blood", 8, 100, 30, 8,
                    GameData.DamageType.Poison, Spell.SpellLine.Dru_Poison_DOT,
                    "Poisons the target with corrupted blood.", "Disease",
                    NecromancerVfxStyle.Blood, new Color(0.52f, 0.72f, 0.08f)),
                Special("dark_covenant_1", "Dark Covenant", 10, 0,
                    NecromancerSpellKind.HealthToMana, 20,
                    "For thirty seconds, sacrifices 8 health to restore 20 mana every three seconds. The sacrifice pauses below 25% health.",
                    "Covenant", NecromancerVfxStyle.Blood,
                    new Color(0.65f, 0.05f, 0.16f), cast: 1f, cooldown: 30f,
                    duration: 10, hp: -8, selfOnly: true),
                Status("heart_flutter", "Heart Flutter", 11, 180, 6,
                    Spell.SpellLine.Reav_Suffering, GameData.DamageType.Void,
                    "Weakens the target's strength and armor while dealing void damage.",
                    "Curse", NecromancerVfxStyle.Curse,
                    new Color(0.58f, 0.08f, 0.28f), damage: 70, str: -12,
                    ac: -20, resist: -5f),
                Damage("siphon_soul", "Siphon Soul", 12, 250, 300, true,
                    "Damages the target and heals the caster for the damage dealt.",
                    "Lifetap", NecromancerVfxStyle.Drain,
                    new Color(0.56f, 0.12f, 0.78f), cast: 2f),
                Buff("bone_armor_2", "Ossified Carapace", 13, 150, 20,
                    "Fortifies the caster with layered bone for one minute, granting 175 health and 60 armor.",
                    "Pet Ward", NecromancerVfxStyle.Bone,
                    new Color(0.64f, 0.74f, 0.62f), hp: 175, ac: 60),
                Special("reclaim_servant", "Reclaim Servant", 14, 0,
                    NecromancerSpellKind.ReclaimPet, 25,
                    "Reclaims the active thrall for 25% of the caster's maximum mana. Pet equipment remains safely escrowed.",
                    "Reclaim", NecromancerVfxStyle.Death,
                    new Color(0.72f, 0.78f, 0.62f), cast: 1.5f,
                    cooldown: 5f, selfOnly: true),
                Special("deathly_guise", "Deathly Guise", 15, 100,
                    NecromancerSpellKind.FeignDeath, 0,
                    "Drops hostile attention and prevents new NPC aggression for six seconds. Any action breaks the guise.",
                    "Feign Death", NecromancerVfxStyle.Death,
                    new Color(0.34f, 0.36f, 0.42f), cast: 0.5f,
                    cooldown: 60f, duration: 2, selfOnly: true,
                    breakOnAction: true),
                Damage("leeching_curse", "Leeching Curse", 16, 400, 350, true,
                    "Damages the target and immediately heals the caster for the damage dealt, then leaves a lingering void curse.",
                    "Lifetap", NecromancerVfxStyle.Drain,
                    new Color(0.42f, 0.08f, 0.58f), cast: 2.25f,
                    appliedEffect: "leeching_curse_dot"),
                Special("intensify_death", "Intensify Death", 17, 200,
                    NecromancerSpellKind.PetBuff, 0,
                    "Empowers the active thrall for thirty seconds: +350 HP, +45 AC, and +20 Strength.",
                    "Pet Ward", NecromancerVfxStyle.Ward,
                    new Color(0.46f, 0.70f, 0.56f), cast: 1.75f,
                    cooldown: 8f, duration: 10, hp: 350, ac: 45, str: 20),

                Pet("bone_thrall_3", "Gravebound Soldier", 18, 350, 3,
                    "Raises a veteran skeletal warrior with improved offense and defenses."),
                Pbaoe("infectious_cloud", "Infectious Cloud", 19, 300, 180,
                    "A burst of plague damages and infects nearby enemies.",
                    "Plague Cloud", NecromancerVfxStyle.Cloud,
                    new Color(0.34f, 0.68f, 0.12f), 18f,
                    "infectious_cloud_dot"),
                Special("dark_covenant_2", "Blood Communion", 20, 0,
                    NecromancerSpellKind.HealthToMana, 75,
                    "For thirty seconds, sacrifices 15 health to restore 75 mana every three seconds. The sacrifice pauses below 25% health.",
                    "Covenant", NecromancerVfxStyle.Blood,
                    new Color(0.76f, 0.04f, 0.18f), cast: 1f, cooldown: 30f,
                    duration: 10, hp: -15, selfOnly: true),
                Status("screaming_terror", "Screaming Terror", 21, 380, 4,
                    Spell.SpellLine.Reav_Fear, GameData.DamageType.Magic,
                    "Overwhelms a non-boss target with terror for up to twelve seconds.",
                    "Fear", NecromancerVfxStyle.Fear,
                    new Color(0.68f, 0.12f, 0.86f), fear: true,
                    cooldown: 24f, resist: 5f),
                Status("scent_of_decay", "Scent of Decay", 22, 220, 6,
                    Spell.SpellLine.Global_Magic_Debuff, GameData.DamageType.Void,
                    "Lowers the target's poison and void resistance.",
                    "Curse", NecromancerVfxStyle.Curse,
                    new Color(0.30f, 0.52f, 0.12f), pr: -25, vr: -25,
                    resist: 8f),
                Special("bond_of_death", "Bond of Death", 23, 350,
                    NecromancerSpellKind.PetBuff, 0,
                    "Strengthens the active thrall for thirty seconds: +900 HP, +90 AC, and +35 Strength.",
                    "Pet Ward", NecromancerVfxStyle.Ward,
                    new Color(0.48f, 0.72f, 0.60f), cast: 2f, cooldown: 8f,
                    duration: 10, hp: 900, ac: 90, str: 35),
                Pet("bone_thrall_4", "Crypt Vanguard", 24, 600, 4,
                    "Raises an elite skeletal warrior with improved offense and defenses."),
                Special("soul_transfusion", "Soul Transfusion", 25, 180,
                    NecromancerSpellKind.SoulTransfusion, 140,
                    "Transfers 140 mana to another player or grouped SimPlayer at an efficiency loss.",
                    "Soul Transfer", NecromancerVfxStyle.Drain,
                    new Color(0.22f, 0.56f, 0.84f), cast: 2.5f,
                    cooldown: 12f),
                Buff("bone_armor_3", "Sepulchral Plate", 26, 450, 30,
                    "Encases the caster in ancient bone for ninety seconds, granting 500 health and 150 armor.",
                    "Pet Ward", NecromancerVfxStyle.Bone,
                    new Color(0.58f, 0.68f, 0.58f), hp: 500, ac: 150),
                Dot("devouring_darkness", "Devouring Darkness", 27, 550, 650, 7,
                    GameData.DamageType.Void, Spell.SpellLine.Reav_Movespeed,
                    "Devouring shadow deals heavy damage and strongly slows the target.",
                    "Shadow", NecromancerVfxStyle.Shadow,
                    new Color(0.12f, 0.04f, 0.30f), movement: -0.40f,
                    resist: 5f),
                Damage("drain_spirit", "Drain Spirit", 29, 850, 1300, true,
                    "Damages the target and heals the caster for the damage dealt.",
                    "Lifetap", NecromancerVfxStyle.Drain,
                    new Color(0.62f, 0.10f, 0.82f), cast: 2.75f,
                    cooldown: 4f),
                Dot("funeral_pyre", "Gravefire Immolation", 30, 700, 850, 7,
                    GameData.DamageType.Elemental, Spell.SpellLine.Global_Element_DOT,
                    "Cremates the target in persistent grave-fire.", "Grave Fire",
                    NecromancerVfxStyle.Fire, new Color(0.92f, 0.20f, 0.05f),
                    resist: 5f),
                Pet("bone_thrall_5", "Ossuary Champion", 32, 1400, 5,
                    "Raises the strongest skeletal warrior available to a mortal Necromancer."),
                Buff("dread_shield", "Dread Shield", 33, 500, 4,
                    "Surrounds the caster with vengeful spirits for twelve seconds, returning 150 damage to melee attackers.",
                    "Harmshield", NecromancerVfxStyle.Ward,
                    new Color(0.56f, 0.62f, 0.82f), damageShield: 150,
                    cooldown: 45f),
                Special("harmshield", "Harmshield", 34, 500,
                    NecromancerSpellKind.Harmshield, 0,
                    "Makes the caster invulnerable but immobile for six seconds. Any action breaks the shield.",
                    "Harmshield", NecromancerVfxStyle.Ward,
                    new Color(0.72f, 0.76f, 0.90f), cast: 0f,
                    cooldown: 180f, duration: 2, selfOnly: true,
                    breakOnAction: true, movement: -100f),
                Dot("plague_of_souls", "Plague of Souls", 35, 900, 1150, 7,
                    GameData.DamageType.Void, Spell.SpellLine.Global_Void_DOT,
                    "The Necromancer's capstone plague ravages body and spirit over eighteen seconds.",
                    "Soul Plague", NecromancerVfxStyle.Cloud,
                    new Color(0.26f, 0.72f, 0.34f), cooldown: 18f,
                    resist: 10f),

                // Internal effects applied by learnable spells. They are added
                // to the spell DB for save/load, but never sold or counted.
                HiddenDot("leeching_curse_dot", "Leeching Curse: Affliction", 16,
                    450, 5, GameData.DamageType.Void,
                    Spell.SpellLine.Dru_Void_DOT, "Lifetap",
                    NecromancerVfxStyle.Drain, new Color(0.42f, 0.08f, 0.58f)),
                HiddenDot("infectious_cloud_dot", "Infectious Cloud: Plague", 19,
                    130, 5, GameData.DamageType.Poison,
                    Spell.SpellLine.Arc_Poison_DOT, "Plague Cloud",
                    NecromancerVfxStyle.Cloud, new Color(0.34f, 0.68f, 0.12f))
            };

        private static NecromancerSpellDefinition Pet(string key, string name,
            int level, int mana, int tier, string desc)
        {
            return new NecromancerSpellDefinition
            {
                Key = key, Name = name, Description = desc +
                    " It automatically uses Grave Challenge to taunt an enemy " +
                    "attacking its master, up to once every six seconds.",
                IconTheme = "Bone Thrall",
                Level = level, Type = Spell.SpellType.Pet,
                Line = Spell.SpellLine.Generic,
                DamageType = GameData.DamageType.Void,
                Kind = NecromancerSpellKind.SummonPet,
                VfxStyle = NecromancerVfxStyle.Bone,
                VfxColor = new Color(0.72f, 0.80f, 0.66f), ManaCost = mana,
                PetTier = tier, CastSeconds = 4f, CooldownSeconds = 8f,
                Range = 60f, SelfOnly = true
            };
        }

        private static NecromancerSpellDefinition Damage(string key, string name,
            int level, int mana, int damage, bool lifetap, string desc,
            string icon, NecromancerVfxStyle vfx, Color color, float cast = 1.5f,
            float cooldown = 2.5f, string appliedEffect = null,
            GameData.DamageType damageType = GameData.DamageType.Void)
        {
            return new NecromancerSpellDefinition
            {
                Key = key, Name = name, Description = desc, IconTheme = icon,
                Level = level, Type = Spell.SpellType.Damage,
                Line = Spell.SpellLine.Direct_Damage,
                DamageType = damageType,
                Kind = NecromancerSpellKind.Native, VfxStyle = vfx,
                VfxColor = color, ManaCost = mana, TargetDamage = damage,
                Aggro = Math.Max(1, damage), Lifetap = lifetap,
                CastSeconds = cast, CooldownSeconds = cooldown,
                AppliedEffectKey = appliedEffect
            };
        }

        private static NecromancerSpellDefinition Dot(string key, string name,
            int level, int mana, int damage, int duration,
            GameData.DamageType damageType, Spell.SpellLine line, string desc,
            string icon, NecromancerVfxStyle vfx, Color color,
            float movement = 0f, float cooldown = 3f, float resist = 0f)
        {
            return new NecromancerSpellDefinition
            {
                Key = key, Name = name, Description = desc, IconTheme = icon,
                Level = level, Type = Spell.SpellType.StatusEffect, Line = line,
                DamageType = damageType, Kind = NecromancerSpellKind.Native,
                VfxStyle = vfx, VfxColor = color, ManaCost = mana,
                TargetDamage = damage, DurationTicks = duration,
                MovementSpeed = movement, Aggro = Math.Max(1, damage * duration / 2),
                CastSeconds = 2.25f, CooldownSeconds = cooldown,
                ResistModifier = resist
            };
        }

        private static NecromancerSpellDefinition HiddenDot(string key,
            string name, int level, int damage, int duration,
            GameData.DamageType damageType, Spell.SpellLine line, string icon,
            NecromancerVfxStyle vfx, Color color)
        {
            NecromancerSpellDefinition value = Dot(key, name, level, 0, damage,
                duration, damageType, line, "Internal persistent effect.", icon,
                vfx, color);
            value.HiddenEffect = true;
            return value;
        }

        private static NecromancerSpellDefinition Status(string key,
            string name, int level, int mana, int duration,
            Spell.SpellLine line, GameData.DamageType damageType, string desc,
            string icon, NecromancerVfxStyle vfx, Color color, int damage = 0,
            int str = 0, int ac = 0, int pr = 0, int vr = 0,
            bool fear = false, bool root = false, float cooldown = 5f,
            float resist = 0f)
        {
            return new NecromancerSpellDefinition
            {
                Key = key, Name = name, Description = desc, IconTheme = icon,
                Level = level, Type = Spell.SpellType.StatusEffect, Line = line,
                DamageType = damageType, Kind = NecromancerSpellKind.Native,
                VfxStyle = vfx, VfxColor = color, ManaCost = mana,
                DurationTicks = duration, TargetDamage = damage,
                Str = str, Ac = ac, Pr = pr, Vr = vr, Fear = fear, Root = root,
                Aggro = Math.Max(12, damage * duration / 2), CastSeconds = 2f,
                CooldownSeconds = cooldown, ResistModifier = resist
            };
        }

        private static NecromancerSpellDefinition Buff(string key,
            string name, int level, int mana, int duration, string desc,
            string icon, NecromancerVfxStyle vfx, Color color, int hp = 0,
            int ac = 0, int damageShield = 0,
            bool grantInvisibility = false, float cast = 2f,
            float cooldown = 5f, bool simUsable = true)
        {
            return new NecromancerSpellDefinition
            {
                Key = key, Name = name, Description = desc, IconTheme = icon,
                Level = level, Type = Spell.SpellType.Beneficial,
                Line = damageShield > 0 ? Spell.SpellLine.Aura_DamageShield :
                    Spell.SpellLine.Global_Buff,
                DamageType = GameData.DamageType.Void,
                Kind = NecromancerSpellKind.Native, VfxStyle = vfx,
                VfxColor = color, ManaCost = mana, DurationTicks = duration,
                Hp = hp, Ac = ac, DamageShield = damageShield,
                GrantInvisibility = grantInvisibility, SelfOnly = true,
                SimUsable = simUsable,
                CastSeconds = cast, CooldownSeconds = cooldown,
                Aggro = 0
            };
        }

        private static NecromancerSpellDefinition Special(string key,
            string name, int level, int mana, NecromancerSpellKind kind,
            int amount, string desc, string icon, NecromancerVfxStyle vfx,
            Color color, float cast = 1f, float cooldown = 5f,
            int duration = 0, int hp = 0, int ac = 0, int str = 0,
            bool selfOnly = false, bool breakOnAction = false,
            float movement = 0f)
        {
            return new NecromancerSpellDefinition
            {
                Key = key, Name = name, Description = desc, IconTheme = icon,
                Level = level, Type = Spell.SpellType.Misc,
                Line = Spell.SpellLine.Generic,
                DamageType = GameData.DamageType.Void, Kind = kind,
                VfxStyle = vfx, VfxColor = color, ManaCost = mana,
                CustomAmount = amount, CastSeconds = cast,
                CooldownSeconds = cooldown, DurationTicks = duration,
                Hp = hp, Ac = ac, Str = str, SelfOnly = selfOnly,
                BreakOnAnyAction = breakOnAction, MovementSpeed = movement
            };
        }

        private static NecromancerSpellDefinition Pbaoe(string key,
            string name, int level, int mana, int damage, string desc,
            string icon, NecromancerVfxStyle vfx, Color color, float cooldown,
            string appliedEffect)
        {
            return new NecromancerSpellDefinition
            {
                Key = key, Name = name, Description = desc, IconTheme = icon,
                Level = level, Type = Spell.SpellType.PBAE,
                Line = Spell.SpellLine.Global_Poison_DOT,
                DamageType = GameData.DamageType.Poison,
                Kind = NecromancerSpellKind.Native, VfxStyle = vfx,
                VfxColor = color, ManaCost = mana, TargetDamage = damage,
                Aggro = damage * 2, CastSeconds = 2.5f,
                CooldownSeconds = cooldown, Range = 12f,
                AppliedEffectKey = appliedEffect, Pbaoe = true
            };
        }
    }
}
