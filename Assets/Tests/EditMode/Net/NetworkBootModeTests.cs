using NUnit.Framework;

public class NetworkBootModeTests
{
    [Test]
    public void Resolve_BatchMode_IsDedicatedServer()
    {
        var kind = NetworkBootMode.Resolve(true, new string[0], singlePlayerMode: true);
        Assert.AreEqual(NetworkBootKind.DedicatedServer, kind);
    }

    [Test]
    public void Resolve_DedicatedServerArg_IsDedicatedServer()
    {
        var kind = NetworkBootMode.Resolve(false, new[] { "-dedicatedServer" }, singlePlayerMode: true);
        Assert.AreEqual(NetworkBootKind.DedicatedServer, kind);
    }

    [Test]
    public void Resolve_Interactive_SinglePlayerTrue_IsSinglePlayerHost()
    {
        var kind = NetworkBootMode.Resolve(false, new string[0], singlePlayerMode: true);
        Assert.AreEqual(NetworkBootKind.SinglePlayerHost, kind);
    }

    [Test]
    public void Resolve_Interactive_SinglePlayerFalse_IsClient()
    {
        var kind = NetworkBootMode.Resolve(false, new string[0], singlePlayerMode: false);
        Assert.AreEqual(NetworkBootKind.Client, kind);
    }

    [Test]
    public void Resolve_NullArgs_DoesNotThrow()
    {
        var kind = NetworkBootMode.Resolve(false, null, singlePlayerMode: false);
        Assert.AreEqual(NetworkBootKind.Client, kind);
    }
}
