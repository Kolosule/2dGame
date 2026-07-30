using NUnit.Framework;
using Game.Hud.Core;

public class ToastFadeTests
{
    [Test]
    public void FullyOpaqueThroughTheHold()
    {
        Assert.AreEqual(1f, ToastFade.Alpha01(0f, 2f, 0.5f), 1e-4f);
        Assert.AreEqual(1f, ToastFade.Alpha01(2f, 2f, 0.5f), 1e-4f, "the hold boundary is still opaque");
    }

    [Test]
    public void FadesLinearlyThenStaysAtZero()
    {
        Assert.AreEqual(0.5f, ToastFade.Alpha01(2.25f, 2f, 0.5f), 1e-4f);
        Assert.AreEqual(0f, ToastFade.Alpha01(2.5f, 2f, 0.5f), 1e-4f);
        Assert.AreEqual(0f, ToastFade.Alpha01(99f, 2f, 0.5f), 1e-4f);
    }

    [Test]
    public void AZeroFadeCutsStraightToInvisible()
    {
        Assert.AreEqual(1f, ToastFade.Alpha01(2f, 2f, 0f), 1e-4f);
        Assert.AreEqual(0f, ToastFade.Alpha01(2.01f, 2f, 0f), 1e-4f);
    }
}
