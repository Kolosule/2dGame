namespace Game.Combat.Core
{
    /// <summary>Retains shoot transitions across polls without replaying them when input is repeated.</summary>
    public sealed class ShootInputBuffer
    {
        private bool physicalHeld;
        private bool suppressedUntilRelease;
        private bool cancelled;

        public bool Held => physicalHeld && !suppressedUntilRelease;
        public uint Sequence { get; private set; }
        public uint CancellationSequence { get; private set; }

        public void SetHeld(bool held)
        {
            if (held == physicalHeld) return;
            physicalHeld = held;

            if (suppressedUntilRelease)
            {
                if (!held) suppressedUntilRelease = false;
                return;
            }

            if (held) cancelled = false;
            Sequence = unchecked(Sequence + 1);
        }

        public void Cancel()
        {
            if (!cancelled) CancellationSequence = unchecked(CancellationSequence + 1);
            cancelled = true;
            suppressedUntilRelease = physicalHeld;
        }
    }
}
