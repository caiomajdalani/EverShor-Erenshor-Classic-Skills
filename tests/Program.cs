using System;
using ErenshorSkills.Necromancer;

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
        Console.WriteLine(passed + " regression checks passed.");
        return 0;
    }
}
