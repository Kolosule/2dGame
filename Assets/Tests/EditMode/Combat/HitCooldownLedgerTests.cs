using NUnit.Framework;
using Game.Combat.Core;

public class HitCooldownLedgerTests
{
    private const ulong AttackerA = 101;
    private const ulong AttackerB = 202;

    [Test]
    public void FirstHitFromAnAttackerIsAllowed()
    {
        var ledger = new HitCooldownLedger();
        Assert.IsTrue(ledger.TryRegisterHit(AttackerA, currentTick: 100, cooldownTicks: 6));
    }

    [Test]
    public void SecondHitFromSameAttackerWithinCooldownIsBlocked()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 6);
        Assert.IsFalse(ledger.TryRegisterHit(AttackerA, 103, 6));
    }

    [Test]
    public void HitFromSameAttackerAfterCooldownIsAllowed()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 6);
        Assert.IsTrue(ledger.TryRegisterHit(AttackerA, 106, 6));
    }

    [Test]
    public void DifferentAttackersAreIndependent()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 6);
        // The review bug: attacker B landing 1 tick later must NOT be eaten.
        Assert.IsTrue(ledger.TryRegisterHit(AttackerB, 101, 6));
    }

    [Test]
    public void ZeroCooldownAlwaysAllows()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 0);
        Assert.IsTrue(ledger.TryRegisterHit(AttackerA, 100, 0));
    }

    [Test]
    public void ClearForgetsAllAttackers()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 6);
        ledger.Clear();
        Assert.IsTrue(ledger.TryRegisterHit(AttackerA, 101, 6));
    }

    [Test]
    public void BlockedHitDoesNotResetTheCooldownWindow()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 6);
        ledger.TryRegisterHit(AttackerA, 103, 6); // blocked — must not re-arm
        Assert.IsTrue(ledger.TryRegisterHit(AttackerA, 106, 6));
    }
}
