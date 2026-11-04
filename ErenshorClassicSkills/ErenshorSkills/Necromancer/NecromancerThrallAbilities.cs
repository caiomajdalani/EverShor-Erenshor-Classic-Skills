using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    // Private NPC spells: never registered as learnable player spells.
    internal static class NecromancerThrallAbilities
    {
        private static readonly Dictionary<int, List<Spell>> Kits =
            new Dictionary<int, List<Spell>>();
        private static readonly string[] AttackNames = { "", "Bone Rake",
            "Sentry's Rebuke", "Grave Siphon", "Crypt Breaker", "Ossuary Verdict" };
        private static readonly int[] AttackDamage = { 0, 4, 12, 24, 36, 52 };

        internal static void Reset()
        {
            foreach (var kit in Kits.Values)
                foreach (Spell spell in kit)
                    if (spell != null) Object.Destroy(spell);
            Kits.Clear();
        }

        internal static List<Spell> GetKit(int tier)
        {
            tier = Mathf.Clamp(tier, 1, 5);
            if (Kits.TryGetValue(tier, out var existing) &&
                existing.All(spell => spell != null)) return existing;
            var kit = new List<Spell>();
            var database = GameData.SpellDatabase?.SpellDatabase;
            if (database == null) return kit;
            Spell template = database.FirstOrDefault(spell => spell != null &&
                spell.Type == Spell.SpellType.Damage && spell.TargetDamage > 0 &&
                !spell.SelfOnly && !spell.Lifetap && !spell.FearTarget &&
                !spell.RootTarget && !spell.CrowdControlSpell &&
                !spell.ApplyToCaster && !spell.InflictOnSelf &&
                !spell.HardcodedUseCase && !spell.ReapAndRenew &&
                !spell.GroupEffect && spell.StatusEffectToApply == null &&
                spell.AddProc == null && !spell.TauntSpell && !spell.JoltSpell &&
                !(spell.Id ?? "").StartsWith("evershor_thrall_"));
            if (template == null) return kit;

            // One tier-specific attack replaces its predecessor. The small
            // burst supplements weapon damage without multiplying the rotation.
            if (existing != null)
                foreach (Spell stale in existing)
                    if (stale != null) Object.Destroy(stale);
            var attack = Object.Instantiate(template);
            attack.Id = "evershor_thrall_attack_" + tier;
            attack.name = attack.Id;
            attack.SpellName = AttackNames[tier];
            attack.RequiredLevel = 1;
            attack.TargetDamage = AttackDamage[tier];
            attack.TargetHealing = 0;
            attack.CasterHealing = 0;
            attack.SpellDurationInTicks = 0;
            attack.InstantEffect = true;
            attack.MovementSpeed = 0f;
            attack.Haste = 0f;
            attack.StunTarget = false;
            attack.CrowdControlSpell = false;
            attack.StatusEffectToApply = null;
            attack.GroupEffect = false;
            attack.ApplyToCaster = false;
            attack.StunTarget = false;
            attack.CharmTarget = false;
            attack.JoltSpell = false;
            attack.ManaCost = tier * 3;
            attack.Cooldown = 6f;
            attack.SpellChargeTime = 0.5f;
            attack.SpellRange = 5f;
            attack.Aggro = AttackDamage[tier] * 2;
            attack.Lifetap = tier == 3;
            attack.AutomateAttack = false;
            attack.CanHitPlayers = false;
            attack.NoResonate = true;
            attack.TauntSpell = false;
            attack.SpellDesc = "A Bone Thrall combat technique. Requires an existing hostile combat target.";
            kit.Add(attack);
            var taunt = CreateTechnique(attack, tier, "taunt",
                "Grave Challenge", ThrallCombatPolicy.DirectTauntCooldown);
            taunt.TargetDamage = 1;
            taunt.Aggro = 25 + tier * 20;
            taunt.TauntSpell = true;
            taunt.SpellDesc = "Challenges the current enemy and generates additional threat.";
            kit.Add(taunt);

            if (tier >= 2)
            {
                var slow = CreateTechnique(attack, tier, "slow",
                    "Grasp of the Grave", 11f);
                slow.Type = Spell.SpellType.StatusEffect;
                slow.InstantEffect = false;
                slow.Line = Spell.SpellLine.Attack_Speed_Debuff;
                slow.TargetDamage = 0;
                slow.SpellDurationInTicks = 2;
                slow.MovementSpeed = -0.20f - tier * 0.04f;
                slow.Haste = -5f - tier * 2f;
                slow.Aggro = 20 + tier * 5;
                slow.SpellDesc = "Briefly slows the current enemy's movement and attacks.";
                kit.Add(slow);
            }
            if (tier >= 3)
            {
                var stun = CreateTechnique(attack, tier, "stun",
                    tier >= 5 ? "Ossuary Hammer" : "Crypt Bash", 15f);
                stun.Type = Spell.SpellType.StatusEffect;
                stun.InstantEffect = false;
                stun.Line = Spell.SpellLine.Stun;
                stun.TargetDamage = 0;
                stun.SpellDurationInTicks = 1;
                stun.StunTarget = true;
                stun.CrowdControlSpell = true;
                stun.Aggro = 30 + tier * 5;
                stun.SpellDesc = "Briefly stuns the current enemy. Subject to native control resistance.";
                kit.Add(stun);
            }
            Kits[tier] = kit;
            return kit;
        }

        private static Spell CreateTechnique(Spell source, int tier,
            string key, string name, float cooldown)
        {
            var spell = Object.Instantiate(source);
            spell.Id = "evershor_thrall_" + key + "_" + tier;
            spell.name = spell.Id;
            spell.SpellName = name;
            spell.Cooldown = cooldown;
            spell.ManaCost = 3 + tier * 2;
            spell.SpellChargeTime = 0.35f;
            spell.Lifetap = false;
            spell.SpellDurationInTicks = 0;
            spell.MovementSpeed = 0f;
            spell.Haste = 0f;
            spell.StunTarget = false;
            spell.CrowdControlSpell = false;
            return spell;
        }
    }
}
