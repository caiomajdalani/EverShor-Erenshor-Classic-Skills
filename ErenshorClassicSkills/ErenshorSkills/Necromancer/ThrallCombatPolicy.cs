namespace ErenshorSkills.Necromancer
{
    // Pure combat policy, shared with the regression test executable.
    internal static class ThrallCombatPolicy
    {
        internal const float AreaTauntRadius = 12f;
        internal const float AreaTauntCooldown = 2f;
        internal const float DirectTauntCooldown = 1.25f;

        internal static bool CanAreaTaunt(bool alive, bool charmed,
            bool neverAggro, bool engaged, float distanceSquared)
        {
            return alive && !charmed && !neverAggro && engaged &&
                distanceSquared >= 0f &&
                distanceSquared <= AreaTauntRadius * AreaTauntRadius;
        }
    }
}
