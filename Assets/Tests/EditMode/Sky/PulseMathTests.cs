using NUnit.Framework;
using Game.Sky.Core;

public class PulseMathTests
{
    [Test]
    public void Stays_Within_Amplitude_Band()
    {
        for (float t = 0f; t < 20f; t += 0.13f)
        {
            float m = PulseMath.Multiplier(t, 0.9f, 0.12f, 0f);
            Assert.GreaterOrEqual(m, 1f - 0.12f - 1e-4f);
            Assert.LessOrEqual(m, 1f + 0.12f + 1e-4f);
        }
    }

    [Test]
    public void Zero_Amplitude_Is_Flat_One()
    {
        Assert.AreEqual(1f, PulseMath.Multiplier(3.3f, 0.9f, 0f, 0f), 1e-6f);
    }

    [Test]
    public void Phase_Shifts_The_Curve()
    {
        float a = PulseMath.Multiplier(0f, 1f, 0.2f, 0f);
        float b = PulseMath.Multiplier(0f, 1f, 0.2f, 1.5707963f); // +pi/2
        Assert.AreEqual(1f, a, 1e-4f);            // sin(0) = 0
        Assert.AreEqual(1.2f, b, 1e-4f);          // sin(pi/2) = 1
    }
}
