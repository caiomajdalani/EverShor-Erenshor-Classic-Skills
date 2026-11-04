namespace ErenshorSkills.Monk
{
    internal static class MonkCombatPolicy
    {
        internal static bool CanActivate(bool casterAlive, bool npc,
            bool offensive, bool targetAlive, bool selfTarget,
            bool matchesAggro, float distanceSquared, float reach)
        {
            if (!casterAlive) return false;
            // NPC utilities currently use local-player stance timers.
            if (!offensive) return !npc;
            return targetAlive && !selfTarget && (!npc || matchesAggro) &&
                distanceSquared >= 0f && reach > 0f &&
                distanceSquared <= reach * reach;
        }
    }
}
