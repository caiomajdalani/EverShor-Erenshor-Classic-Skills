using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    internal enum MonkTechniqueKind
    {
        Passive,
        Attack,
        Mend,
        FocusedWill,
        StoneThrow,
        CenteredBreath,
        CounteringPalm,
        FeignDeath,
        BreathOfHarmony,
        SilentStep,
        ReedInTheWind,
        StoneStance,
        EmptyBody,
        PurifyBody,
        HundredFists,
        FivePointFocus
    }

    internal sealed class MonkTechniqueDefinition
    {
        internal string Key;
        internal string Name;
        internal string Description;
        internal int Level;
        internal float Cooldown;
        internal int Power;
        internal string Animation;
        internal MonkTechniqueKind Kind;
        internal bool AreaEffect;
        internal bool Interrupt;
        internal string EffectKey;
        internal int FollowUpHits;
        internal Color VfxColor;
        internal int VfxPattern;

        internal bool IsAttack => Kind == MonkTechniqueKind.Attack;
        internal bool IsOffensive => IsAttack ||
            Kind == MonkTechniqueKind.StoneThrow;
    }

    /// <summary>
    /// Thirty-two uniquely named Monk techniques. Together with the seven
    /// native martial skills in MonkSharedSkills this forms a thirty-nine-
    /// ability class kit, matching the lower end of Erenshor's native combined
    /// skill-and-spell counts without copying names or effects.
    /// Cooldowns use Erenshor's native frame/tick convention (60 = one second).
    /// </summary>
    internal static class MonkSkillData
    {
        internal const int ExpectedCustomCount = 32;
        internal const int ExpectedTotalCount = 39;

        internal static readonly IReadOnlyList<MonkTechniqueDefinition> All =
            new[]
            {
                new MonkTechniqueDefinition
                {
                    Key = "open_hand_discipline",
                    Name = "Open Hand Discipline",
                    Description = "Passive Skill: Your handwrap attacks deal 5% more physical damage. Your skill with handwraps grows naturally as you gain levels.",
                    Level = 1,
                    Cooldown = 0f,
                    Power = 0,
                    Animation = "",
                    Kind = MonkTechniqueKind.Passive,
                    VfxColor = new Color(1f, 0.66f, 0.16f, 1f),
                    VfxPattern = 0
                },
                new MonkTechniqueDefinition
                {
                    Key = "mend",
                    Name = "Mend",
                    Description = "Focus your breathing to restore 25% of maximum health. Deep Mend improves the amount restored.",
                    Level = 1,
                    Cooldown = 3600f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.Mend,
                    VfxColor = new Color(0.20f, 0.95f, 0.68f, 1f),
                    VfxPattern = 1
                },
                new MonkTechniqueDefinition
                {
                    Key = "jab",
                    Name = "Jab",
                    Description = "Strike with a fast straight punch that deals physical damage.",
                    Level = 1,
                    Cooldown = 240f,
                    Power = 2,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.Attack,
                    VfxColor = new Color(1f, 0.72f, 0.20f, 1f),
                    VfxPattern = 2
                },
                new MonkTechniqueDefinition
                {
                    Key = "shoulder_check",
                    Name = "Shoulder Check",
                    Description = "Drive through the target with a compact body strike that deals physical damage and interrupts spellcasting.",
                    Level = 3,
                    Cooldown = 480f,
                    Power = 2,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.Attack,
                    Interrupt = true,
                    VfxColor = new Color(0.90f, 0.68f, 0.28f, 1f),
                    VfxPattern = 3
                },
                new MonkTechniqueDefinition
                {
                    Key = "tiger_claw",
                    Name = "Tiger Claw",
                    Description = "Rake the target with a focused claw strike that deals physical damage.",
                    Level = 4,
                    Cooldown = 360f,
                    Power = 3,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.Attack,
                    VfxColor = new Color(1f, 0.48f, 0.08f, 1f),
                    VfxPattern = 4
                },
                new MonkTechniqueDefinition
                {
                    Key = "round_kick",
                    Name = "Round Kick",
                    Description = "Sweep a fast circular kick into the target for physical damage.",
                    Level = 5,
                    Cooldown = 300f,
                    Power = 3,
                    Animation = "Kick",
                    Kind = MonkTechniqueKind.Attack,
                    VfxColor = new Color(0.25f, 0.88f, 0.76f, 1f),
                    VfxPattern = 5
                },
                new MonkTechniqueDefinition
                {
                    Key = "pummel",
                    Name = "Pummel",
                    Description = "Unleash a blur of alternating fists. The opening blow deals physical damage and five rapid afterimage strikes follow it.",
                    Level = 5,
                    Cooldown = 900f,
                    Power = 3,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.Attack,
                    FollowUpHits = 5,
                    VfxColor = new Color(1f, 0.55f, 0.16f, 1f),
                    VfxPattern = 28
                },
                new MonkTechniqueDefinition
                {
                    Key = "focused_will",
                    Name = "Focused Will",
                    Description = "Break fear and stun through disciplined focus. This technique can be used while incapacitated.",
                    Level = 6,
                    Cooldown = 2700f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.FocusedWill,
                    VfxColor = new Color(1f, 0.80f, 0.28f, 1f),
                    VfxPattern = 6
                },
                new MonkTechniqueDefinition
                {
                    Key = "stone_throw",
                    Name = "Resonant Palm",
                    Description = "Cup focused martial force at your hip, then drive both palms forward to launch it up to 18 meters, dealing light physical damage and drawing an enemy toward you.",
                    Level = 7,
                    Cooldown = 720f,
                    Power = 2,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.StoneThrow,
                    VfxColor = new Color(0.32f, 0.76f, 0.82f, 1f),
                    VfxPattern = 7
                },
                new MonkTechniqueDefinition
                {
                    Key = "crescent_heel",
                    Name = "Crescent Heel",
                    Description = "Hook the target with a crescent kick, dealing physical damage and slowing its movement and attacks briefly.",
                    Level = 8,
                    Cooldown = 540f,
                    Power = 3,
                    Animation = "Kick",
                    Kind = MonkTechniqueKind.Attack,
                    EffectKey = "crescent_heel",
                    VfxColor = new Color(0.20f, 0.82f, 0.86f, 1f),
                    VfxPattern = 8
                },
                new MonkTechniqueDefinition
                {
                    Key = "centered_breath",
                    Name = "Centered Breath",
                    Description = "Center your breathing to restore 12% of maximum health over 9 seconds. Damage does not interrupt the recovery.",
                    Level = 9,
                    Cooldown = 2400f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.CenteredBreath,
                    VfxColor = new Color(0.35f, 0.92f, 0.70f, 1f),
                    VfxPattern = 9
                },
                new MonkTechniqueDefinition
                {
                    Key = "feign_death",
                    Name = "Feign Death",
                    Description = "Collapse convincingly, clearing enemy attention and preventing new aggression for a short time. Taking damage ends the feign.",
                    Level = 10,
                    Cooldown = 5400f,
                    Power = 0,
                    Animation = "",
                    Kind = MonkTechniqueKind.FeignDeath,
                    VfxColor = new Color(0.70f, 0.53f, 0.24f, 1f),
                    VfxPattern = 10
                },
                new MonkTechniqueDefinition
                {
                    Key = "countering_palm",
                    Name = "Countering Palm",
                    Description = "Adopt a poised guard for 8 seconds. The first melee attacker to strike you is answered with a retaliatory palm.",
                    Level = 11,
                    Cooldown = 1800f,
                    Power = 3,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.CounteringPalm,
                    VfxColor = new Color(0.32f, 0.90f, 0.74f, 1f),
                    VfxPattern = 11
                },
                new MonkTechniqueDefinition
                {
                    Key = "eagle_strike",
                    Name = "Eagle Strike",
                    Description = "Drive a precise hand strike into the target for physical damage.",
                    Level = 12,
                    Cooldown = 480f,
                    Power = 4,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.Attack,
                    VfxColor = new Color(0.94f, 0.78f, 0.34f, 1f),
                    VfxPattern = 12
                },
                new MonkTechniqueDefinition
                {
                    Key = "breath_of_harmony",
                    Name = "Breath of Harmony",
                    Description = "Guide restorative breath through your body, healing 20% of maximum health over 15 seconds.",
                    Level = 13,
                    Cooldown = 3600f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.BreathOfHarmony,
                    VfxColor = new Color(0.30f, 1f, 0.68f, 1f),
                    VfxPattern = 13
                },
                new MonkTechniqueDefinition
                {
                    Key = "silent_step",
                    Name = "Silent Step",
                    Description = "Muffle your presence for 12 seconds, increasing movement speed and reducing enemy notice. Attacking ends the technique.",
                    Level = 14,
                    Cooldown = 2700f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.SilentStep,
                    VfxColor = new Color(0.32f, 0.72f, 0.68f, 1f),
                    VfxPattern = 14
                },
                new MonkTechniqueDefinition
                {
                    Key = "fist_of_falling_rain",
                    Name = "Fist of Falling Rain",
                    Description = "Unleash a three-beat punch combination. The first blow deals physical damage and two lighter echoes follow it.",
                    Level = 15,
                    Cooldown = 720f,
                    Power = 3,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.Attack,
                    FollowUpHits = 2,
                    VfxColor = new Color(0.30f, 0.80f, 1f, 1f),
                    VfxPattern = 15
                },
                new MonkTechniqueDefinition
                {
                    Key = "flying_kick",
                    Name = "Flying Kick",
                    Description = "Leap into a crushing kick that deals heavy physical damage.",
                    Level = 16,
                    Cooldown = 600f,
                    Power = 5,
                    Animation = "Kick",
                    Kind = MonkTechniqueKind.Attack,
                    VfxColor = new Color(0.28f, 0.92f, 0.78f, 1f),
                    VfxPattern = 16
                },
                new MonkTechniqueDefinition
                {
                    Key = "reed_in_the_wind",
                    Name = "Reed in the Wind",
                    Description = "Yield without breaking, reducing incoming physical damage by 15% for 10 seconds.",
                    Level = 17,
                    Cooldown = 2400f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.ReedInTheWind,
                    VfxColor = new Color(0.32f, 0.88f, 0.52f, 1f),
                    VfxPattern = 17
                },
                new MonkTechniqueDefinition
                {
                    Key = "stone_stance",
                    Name = "Stone Stance",
                    Description = "Brace like living stone, reducing incoming damage by 25% for 12 seconds. Flowing Defense improves the reduction.",
                    Level = 18,
                    Cooldown = 3600f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.StoneStance,
                    VfxColor = new Color(0.72f, 0.67f, 0.50f, 1f),
                    VfxPattern = 18
                },
                new MonkTechniqueDefinition
                {
                    Key = "sweeping_crescent",
                    Name = "Sweeping Crescent",
                    Description = "Scythe a low kick through every enemy in melee range for physical damage.",
                    Level = 19,
                    Cooldown = 900f,
                    Power = 3,
                    Animation = "Kick",
                    Kind = MonkTechniqueKind.Attack,
                    AreaEffect = true,
                    VfxColor = new Color(0.20f, 0.88f, 0.78f, 1f),
                    VfxPattern = 19
                },
                new MonkTechniqueDefinition
                {
                    Key = "pressure_point",
                    Name = "Pressure Point",
                    Description = "Strike a precise nerve point for physical damage and a short stun.",
                    Level = 20,
                    Cooldown = 1200f,
                    Power = 4,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.Attack,
                    EffectKey = "pressure_point",
                    VfxColor = new Color(1f, 0.84f, 0.30f, 1f),
                    VfxPattern = 20
                },
                new MonkTechniqueDefinition
                {
                    Key = "gale_splitting_palm",
                    Name = "Gale-Splitting Palm",
                    Description = "Compress your breath into a broad cutting wave that travels to a distant enemy and deals physical damage.",
                    Level = 21,
                    Cooldown = 720f,
                    Power = 4,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.StoneThrow,
                    VfxColor = new Color(0.82f, 0.92f, 0.52f, 1f),
                    VfxPattern = 29
                },
                new MonkTechniqueDefinition
                {
                    Key = "whirlwind_kick",
                    Name = "Whirlwind Kick",
                    Description = "Spin through every enemy in melee range with a physical kick.",
                    Level = 22,
                    Cooldown = 900f,
                    Power = 3,
                    Animation = "Kick",
                    Kind = MonkTechniqueKind.Attack,
                    AreaEffect = true,
                    VfxColor = new Color(1f, 0.66f, 0.14f, 1f),
                    VfxPattern = 21
                },
                new MonkTechniqueDefinition
                {
                    Key = "empty_body",
                    Name = "Empty Body",
                    Description = "Release attachment to pain, reducing non-physical damage by 25% for 10 seconds.",
                    Level = 24,
                    Cooldown = 3600f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.EmptyBody,
                    VfxColor = new Color(0.62f, 0.88f, 0.92f, 1f),
                    VfxPattern = 22
                },
                new MonkTechniqueDefinition
                {
                    Key = "dragon_punch",
                    Name = "Rising Dragon Fist",
                    Description = "Explode from a coiled crouch into a fierce rising uppercut, dealing heavy physical damage, interrupting spellcasting, and briefly stunning the target.",
                    Level = 26,
                    Cooldown = 1200f,
                    Power = 6,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.Attack,
                    Interrupt = true,
                    EffectKey = "dragon_punch",
                    VfxColor = new Color(0.14f, 0.86f, 0.58f, 1f),
                    VfxPattern = 23
                },
                new MonkTechniqueDefinition
                {
                    Key = "vaulting_tempest",
                    Name = "Vaulting Tempest",
                    Description = "Compress into a low guarded crouch, then spring through a rising somersault kick and crescent wave for heavy physical damage that interrupts spellcasting.",
                    Level = 27,
                    Cooldown = 1080f,
                    Power = 6,
                    Animation = "Kick",
                    Kind = MonkTechniqueKind.Attack,
                    Interrupt = true,
                    VfxColor = new Color(0.68f, 1f, 0.20f, 1f),
                    VfxPattern = 30
                },
                new MonkTechniqueDefinition
                {
                    Key = "purify_body",
                    Name = "Purify Body",
                    Description = "Purge detrimental magical effects from your body without removing beneficial effects.",
                    Level = 28,
                    Cooldown = 3600f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.PurifyBody,
                    VfxColor = new Color(1f, 0.84f, 0.30f, 1f),
                    VfxPattern = 24
                },
                new MonkTechniqueDefinition
                {
                    Key = "hundred_fists",
                    Name = "Hundred Fists",
                    Description = "Enter a focused flurry, reducing main-hand and off-hand attack delays by 25% for 20 seconds. Lingering Flurry extends the duration.",
                    Level = 30,
                    Cooldown = 5400f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.HundredFists,
                    VfxColor = new Color(1f, 0.58f, 0.12f, 1f),
                    VfxPattern = 25
                },
                new MonkTechniqueDefinition
                {
                    Key = "five_point_focus",
                    Name = "Five-Point Focus",
                    Description = "Align breath, stance, sight, rhythm, and intent. Monk technique damage is increased by 15% for 12 seconds.",
                    Level = 31,
                    Cooldown = 3600f,
                    Power = 0,
                    Animation = "SkillBuff",
                    Kind = MonkTechniqueKind.FivePointFocus,
                    VfxColor = new Color(1f, 0.78f, 0.22f, 1f),
                    VfxPattern = 26
                },
                new MonkTechniqueDefinition
                {
                    Key = "skybreaker_rush",
                    Name = "Skybreaker Rush",
                    Description = "Rush into the enemy behind a guarded step, plant a driving punch, then release a sliding ground-level storm explosion for exceptional physical damage.",
                    Level = 34,
                    Cooldown = 2400f,
                    Power = 8,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.Attack,
                    Interrupt = true,
                    VfxColor = new Color(0.06f, 0.62f, 1f, 1f),
                    VfxPattern = 31
                },
                new MonkTechniqueDefinition
                {
                    Key = "quivering_palm",
                    Name = "Quivering Palm",
                    Description = "Focus every motion into one devastating palm strike. Deals exceptional physical damage on a long cooldown.",
                    Level = 35,
                    Cooldown = 3600f,
                    Power = 8,
                    Animation = "CastAtk",
                    Kind = MonkTechniqueKind.Attack,
                    Interrupt = true,
                    VfxColor = new Color(1f, 0.82f, 0.25f, 1f),
                    VfxPattern = 27
                }
            };

        /// <summary>Innate techniques, which are skills rather than spells.</summary>
        internal static int ExpectedPassiveCount =>
            All.Count(definition =>
                definition.Kind == MonkTechniqueKind.Passive);

        /// <summary>
        /// Techniques that get a spellbook entry. Passives are deliberately
        /// excluded: they are registered as innate Skills only.
        /// </summary>
        internal static int ExpectedSpellCount =>
            ExpectedCustomCount - ExpectedPassiveCount;
    }
}
