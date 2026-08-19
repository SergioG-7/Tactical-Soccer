using System;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.Core
{
    /// <summary>
    /// Static event bus decoupling gameplay systems that need to react to
    /// tactical route drawing (e.g. time control) from the input/player
    /// layers that produce those moments.
    /// </summary>
    public static class TacticalEvents
    {
        public static Action OnRouteDrawStarted;
        public static Action OnRouteDrawEnded;
        public static Action OnBallOutOfBounds;

        /// <summary>Carries the id of the scoring team: 0 = Blue, 1 = Red.</summary>
        public static Action<int> OnGoalScored;

        /// <summary>
        /// Raised whenever play restarts from the centre. Any system holding a
        /// reference to the ball must drop it here, otherwise it keeps pointing
        /// at a ball that has already been taken away from it.
        /// </summary>
        public static Action OnMatchReset;

        /// <summary>
        /// Raised once when the clock runs out. Nothing restores time after
        /// this, so anything that normally unfreezes the match has to check
        /// the match is still live before doing so.
        /// </summary>
        public static Action OnMatchOver;

        /// <summary>
        /// Raised when the first half ends. Unlike full time this one IS meant
        /// to be undone: the match is frozen until the interval screen sends the
        /// teams back out, and the substitutions made in between are the only
        /// ones anybody gets.
        /// </summary>
        public static Action OnHalftime;

        /// <summary>
        /// Raised when a defender reaches the ball carrier. First argument is
        /// the attacker in possession, second is the challenging defender.
        /// </summary>
        public static Action<TeamMember, TeamMember> OnClashInitiated;

        /// <summary>
        /// Raised when the carrier shoots on goal. First argument is the
        /// shooter, second is the keeper defending that goal. Separate from
        /// OnClashInitiated because the two duels read different stats and offer
        /// different moves, even though both freeze the match.
        /// </summary>
        public static Action<TeamMember, TeamMember> OnShotInitiated;

        /// <summary>
        /// Raised when a side's momentum bar fills and it enters the burn. The
        /// bar itself is polled by the UI every frame — it changes continuously —
        /// so this is only for the things that happen once, at the moment it
        /// lights: the shout, and anything that wants to punctuate it.
        /// </summary>
        public static Action<TeamId> OnTensionIgnited;

        /// <summary>
        /// Raised when a duel ends in a foul. Carries the offender, so the
        /// restart can work out which side gets the free kick and whether the
        /// spot is inside their own box.
        /// </summary>
        public static Action<TeamMember> OnFoulCommitted;

        // There is deliberately no interception event. Cutting a pass out is
        // settled where it happens, in real time, by a direct call into the
        // ClashManager — nothing else in the game needs to hear about it, and an
        // event implies a moment other systems are expected to react to.
    }
}
