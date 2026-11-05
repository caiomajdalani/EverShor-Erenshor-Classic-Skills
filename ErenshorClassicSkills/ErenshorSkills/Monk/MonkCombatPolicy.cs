namespace ErenshorSkills.Monk
{
    internal static class MonkCombatPolicy
    {
        internal static bool ShouldAutoEngagePlayer(bool localCaster,
            bool autoEngageEnabled) => localCaster && autoEngageEnabled;

        internal static bool CanContinueFollowUp(bool enabled, bool actorIsMonk,
            bool actorAlive, bool targetAlive, bool stillTargeting,
            float distanceSquared)
        {
            return enabled && actorIsMonk && stillTargeting &&
                CanActivate(actorAlive, false, true, targetAlive, false,
                    true, distanceSquared, 5f);
        }

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
