using NUnit.Framework;

public class NetworkBootModeTests
{
    [Test]
    public void Resolve_BatchMode_IsDedicatedServer()
    {
        var kind = NetworkBootMode.Resolve(true, new string[0]);
        Assert.AreEqual(NetworkBootKind.DedicatedServer, kind);
    }

    [Test]
    public void Resolve_DedicatedServerArg_IsDedicatedServer()
    {
        var kind = NetworkBootMode.Resolve(false, new[] { "-dedicatedServer" });
        Assert.AreEqual(NetworkBootKind.DedicatedServer, kind);
    }

    [Test]
    public void Resolve_Interactive_IsClient()
    {
        var kind = NetworkBootMode.Resolve(false, new string[0]);
        Assert.AreEqual(NetworkBootKind.Client, kind);
    }

    [Test]
    public void Resolve_UnrelatedArgs_IsClient()
    {
        var kind = NetworkBootMode.Resolve(false, new[] { "-screen-fullscreen", "0" });
        Assert.AreEqual(NetworkBootKind.Client, kind);
    }

    [Test]
    public void Resolve_NullArgs_DoesNotThrow()
    {
        var kind = NetworkBootMode.Resolve(false, null);
        Assert.AreEqual(NetworkBootKind.Client, kind);
    }
}
