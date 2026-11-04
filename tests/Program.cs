using System;
using ErenshorSkills.Necromancer;
using ErenshorSkills.Monk;

internal static class Program
{
    private static int Main()
    {
        int passed = 0;
        void Check(string name, bool result)
        {
            if (!result) throw new InvalidOperationException(name);
            passed++;
            Console.WriteLine("PASS " + name);
        }

        Check("engaged enemy in range", ThrallCombatPolicy.CanAreaTaunt(true, false, false, true, 4));
        Check("neutral NPC next to owner", !ThrallCombatPolicy.CanAreaTaunt(true, false, false, false, 0));
        Check("dead hostile", !ThrallCombatPolicy.CanAreaTaunt(false, false, false, true, 4));
        Check("charmed hostile", !ThrallCombatPolicy.CanAreaTaunt(true, true, false, true, 4));
        Check("NeverAggro overrides combat", !ThrallCombatPolicy.CanAreaTaunt(true, false, true, true, 4));
        Check("radius boundary inclusive", ThrallCombatPolicy.CanAreaTaunt(true, false, false, true, 144));
        Check("large collider outside center radius", !ThrallCombatPolicy.CanAreaTaunt(true, false, false, true, 145));
        Check("invalid negative distance", !ThrallCombatPolicy.CanAreaTaunt(true, false, false, true, -1));
        Check("invalid NaN distance", !ThrallCombatPolicy.CanAreaTaunt(true, false, false, true, float.NaN));
        Check("infinite distance", !ThrallCombatPolicy.CanAreaTaunt(true, false, false, true, float.PositiveInfinity));
        Check("direct taunt within requested 1-2 seconds", ThrallCombatPolicy.DirectTauntCooldown >= 1 && ThrallCombatPolicy.DirectTauntCooldown <= 2);
        Check("area taunt every two seconds", ThrallCombatPolicy.AreaTauntCooldown == 2);
        Check("Monk player melee at reach", MonkCombatPolicy.CanActivate(true, false, true, true, false, false, 25, 5));
        Check("dead Monk cannot attack", !MonkCombatPolicy.CanActivate(false, false, true, true, false, true, 1, 5));
        Check("Monk cannot attack self", !MonkCombatPolicy.CanActivate(true, false, true, true, true, true, 0, 5));
        Check("Monk cannot attack dead target", !MonkCombatPolicy.CanActivate(true, false, true, false, false, true, 1, 5));
        Check("Sim Monk requires own aggro target", !MonkCombatPolicy.CanActivate(true, true, true, true, false, false, 1, 5));
        Check("Sim Monk accepts own aggro target", MonkCombatPolicy.CanActivate(true, true, true, true, false, true, 1, 5));
        Check("Sim cannot mutate player utility timers", !MonkCombatPolicy.CanActivate(true, true, false, true, true, false, 0, 5));
        Check("player utility remains allowed", MonkCombatPolicy.CanActivate(true, false, false, true, true, false, 0, 5));
        Check("melee cannot hit from ranged distance", !MonkCombatPolicy.CanActivate(true, true, true, true, false, true, 100, 5));
        Check("stone throw remains ranged", MonkCombatPolicy.CanActivate(true, true, true, true, false, true, 100, 18));
        Check("invalid Monk distance rejected", !MonkCombatPolicy.CanActivate(true, true, true, true, false, true, float.NaN, 5));
        Console.WriteLine(passed + " regression checks passed.");
        return 0;
    }
}
