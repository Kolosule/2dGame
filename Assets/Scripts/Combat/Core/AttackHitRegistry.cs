using System;
using System.Collections.Generic;

namespace Game.Combat.Core
{
    /// <summary>
    /// Reusable per-attack target registry. Network object ids are used instead of collider ids so
    /// multiple colliders or hitboxes on one target cannot apply damage more than once.
    /// </summary>
    public sealed class AttackHitRegistry
    {
        private readonly HashSet<ulong> targetIds;

        public AttackHitRegistry(int capacity = 32)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            targetIds = new HashSet<ulong>(capacity);
        }

        public bool TryRegister(ulong targetId) => targetIds.Add(targetId);

        public void Clear() => targetIds.Clear();
    }
}
