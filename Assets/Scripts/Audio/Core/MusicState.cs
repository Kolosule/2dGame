using Game.Match.Core;

namespace Game.Audio.Core
{
    /// <summary>What the music layer should be doing right now. A pure value -- MusicDirector
    /// diffs it against what is currently playing and only acts on the difference.</summary>
    public readonly struct MusicPlan
    {
        /// <summary>Looping music bed, or None to stop music entirely.</summary>
        public readonly MusicTrackId Bed;

        /// <summary>Whether the looping arena ambience should be running.</summary>
        public readonly bool Ambient;

        /// <summary>One-shot to fire on entering this state, or None.</summary>
        public readonly AudioCueId Stinger;

        public readonly MixerSnapshotId Snapshot;

        public MusicPlan(MusicTrackId bed, bool ambient, AudioCueId stinger, MixerSnapshotId snapshot)
        {
            Bed = bed;
            Ambient = ambient;
            Stinger = stinger;
            Snapshot = snapshot;
        }
    }

    /// <summary>
    /// Maps match state onto music state. Pure, engine-free, and fully table-tested, because this
    /// is the one place where getting it wrong is loudly wrong: telling a losing player they won.
    ///
    /// Team and winner are plain ints (TeamUtil.ToNumber convention: 0 = None/draw). This assembly
    /// cannot reference the Team enum -- Team lives in Assembly-CSharp, which no asmdef can
    /// reference -- so the caller converts at the boundary. Same convention as
    /// Game.Hud.Core.ScoreboardSort and Game.Match.Core.MatchResolver.
    /// </summary>
    public static class MusicState
    {
        public const int TeamNone = 0;
        public const int WinnerDraw = 0;

        public static MusicPlan Resolve(bool hasMatch, MatchPhase phase, int winner, int localTeam)
        {
            if (!hasMatch)
                return new MusicPlan(MusicTrackId.MenuLoop, false, AudioCueId.None, MixerSnapshotId.Menu);

            switch (phase)
            {
                case MatchPhase.Warmup:
                    return new MusicPlan(MusicTrackId.LobbyLoop, false, AudioCueId.None, MixerSnapshotId.Menu);

                case MatchPhase.Countdown:
                    return new MusicPlan(MusicTrackId.GameplayLoop, true, AudioCueId.None, MixerSnapshotId.Default);

                case MatchPhase.Live:
                    return new MusicPlan(MusicTrackId.GameplayLoop, true, AudioCueId.None, MixerSnapshotId.Default);

                case MatchPhase.SuddenDeath:
                    return new MusicPlan(MusicTrackId.SuddenDeathLoop, true, AudioCueId.None, MixerSnapshotId.SuddenDeath);

                case MatchPhase.PostMatch:
                    return new MusicPlan(MusicTrackId.None, false, ResolveStinger(winner, localTeam), MixerSnapshotId.Stinger);

                case MatchPhase.Intermission:
                    return new MusicPlan(MusicTrackId.LobbyLoop, false, AudioCueId.None, MixerSnapshotId.Menu);

                default:
                    return new MusicPlan(MusicTrackId.MenuLoop, false, AudioCueId.None, MixerSnapshotId.Menu);
            }
        }

        /// <summary>An unassigned local team (spectator, or a team that hasn't replicated) gets the
        /// neutral stinger. Fail toward neutral, never toward falsely celebratory.</summary>
        private static AudioCueId ResolveStinger(int winner, int localTeam)
        {
            if (winner == WinnerDraw) return AudioCueId.DrawStinger;
            if (localTeam == TeamNone) return AudioCueId.DrawStinger;
            return winner == localTeam ? AudioCueId.VictoryStinger : AudioCueId.DefeatStinger;
        }
    }
}
