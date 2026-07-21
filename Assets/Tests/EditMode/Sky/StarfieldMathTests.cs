using NUnit.Framework;
using Game.Sky.Core;

public class StarfieldMathTests
{
    [Test]
    public void Generate_Produces_Requested_Count()
    {
        StarPoint[] s = StarfieldMath.Generate(0f, 0f, 10f, 10f, 300, 1, 0.03f, 0.09f, 0.7f);
        Assert.AreEqual(300, s.Length);
    }

    [Test]
    public void Same_Seed_Is_Deterministic()
    {
        StarPoint[] a = StarfieldMath.Generate(-5f, -5f, 20f, 20f, 50, 42, 0.03f, 0.09f, 0.7f);
        StarPoint[] b = StarfieldMath.Generate(-5f, -5f, 20f, 20f, 50, 42, 0.03f, 0.09f, 0.7f);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.AreEqual(a[i].X, b[i].X, 1e-6f);
            Assert.AreEqual(a[i].Y, b[i].Y, 1e-6f);
            Assert.AreEqual(a[i].Size, b[i].Size, 1e-6f);
            Assert.AreEqual(a[i].Brightness, b[i].Brightness, 1e-6f);
        }
    }

    [Test]
    public void All_Stars_Inside_Bounds_And_Ranges()
    {
        float minX = -8f, minY = 3f, w = 40f, h = 12f;
        StarPoint[] s = StarfieldMath.Generate(minX, minY, w, h, 400, 7, 0.02f, 0.10f, 0.6f);
        foreach (StarPoint p in s)
        {
            Assert.GreaterOrEqual(p.X, minX);
            Assert.LessOrEqual(p.X, minX + w);
            Assert.GreaterOrEqual(p.Y, minY);
            Assert.LessOrEqual(p.Y, minY + h);
            Assert.GreaterOrEqual(p.Size, 0.02f - 1e-6f);
            Assert.LessOrEqual(p.Size, 0.10f + 1e-6f);
            Assert.GreaterOrEqual(p.Brightness, 0f);
            Assert.LessOrEqual(p.Brightness, 0.6f + 1e-6f);
        }
    }

    [Test]
    public void Negative_Count_Returns_Empty()
    {
        Assert.AreEqual(0, StarfieldMath.Generate(0f, 0f, 1f, 1f, -5, 1, 0.1f, 0.2f, 1f).Length);
    }
}
