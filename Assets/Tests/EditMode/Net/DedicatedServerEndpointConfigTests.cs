using System;
using System.Collections.Generic;
using NUnit.Framework;

public class DedicatedServerEndpointConfigTests
{
    private static readonly string[] EnvironmentVariables =
    {
        DedicatedServerEndpointConfig.GamePortEnvironmentVariable,
        DedicatedServerEndpointConfig.PublicIpEnvironmentVariable,
        DedicatedServerEndpointConfig.PublicPortEnvironmentVariable,
        DedicatedServerEndpointConfig.RelayOnlyEnvironmentVariable
    };

    private readonly Dictionary<string, string> originalEnvironment =
        new Dictionary<string, string>();

    [SetUp]
    public void SetUp()
    {
        originalEnvironment.Clear();
        foreach (string variable in EnvironmentVariables)
        {
            originalEnvironment[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string variable in EnvironmentVariables)
            Environment.SetEnvironmentVariable(variable, originalEnvironment[variable]);
    }

    [Test]
    public void Parse_DefaultsToPort27015AndDirectEnabled()
    {
        DedicatedServerEndpointConfig config = Parse();

        Assert.That(config.GamePort, Is.EqualTo(27015));
        Assert.That(config.PublicPort, Is.EqualTo(27015));
        Assert.That(config.PublicIp, Is.Null);
        Assert.That(config.RelayOnly, Is.False);
    }

    [Test]
    public void Parse_CommandLineGamePort_UsesCommandLinePort()
    {
        DedicatedServerEndpointConfig config = Parse("-gamePort", "28000");

        Assert.That(config.GamePort, Is.EqualTo(28000));
        Assert.That(config.PublicPort, Is.EqualTo(28000));
    }

    [Test]
    public void Parse_EnvironmentGamePort_UsesEnvironmentPort()
    {
        SetEnvironment(DedicatedServerEndpointConfig.GamePortEnvironmentVariable, "28001");

        DedicatedServerEndpointConfig config = Parse();

        Assert.That(config.GamePort, Is.EqualTo(28001));
    }

    [Test]
    public void Parse_CommandLineGamePort_TakesPrecedenceOverEnvironment()
    {
        SetEnvironment(DedicatedServerEndpointConfig.GamePortEnvironmentVariable, "28001");

        DedicatedServerEndpointConfig config = Parse("-gamePort", "28002");

        Assert.That(config.GamePort, Is.EqualTo(28002));
    }

    [Test]
    public void Parse_NoPublicPort_UsesResolvedGamePort()
    {
        DedicatedServerEndpointConfig config = Parse(
            "-gamePort", "28003",
            "-publicIp", "203.0.113.10");

        Assert.That(config.PublicPort, Is.EqualTo(28003));
    }

    [Test]
    public void Parse_ValidPublicIpv4AndPort_UsesExplicitEndpoint()
    {
        DedicatedServerEndpointConfig config = Parse(
            "-publicIp", "203.0.113.10",
            "-publicPort", "30000");

        Assert.That(config.PublicIp, Is.EqualTo("203.0.113.10"));
        Assert.That(config.PublicPort, Is.EqualTo(30000));
        Assert.That(config.HasPublicEndpoint, Is.True);
    }

    [Test]
    public void Parse_PublicEndpointEnvironment_UsesEnvironmentValues()
    {
        SetEnvironment(DedicatedServerEndpointConfig.PublicIpEnvironmentVariable, "198.51.100.20");
        SetEnvironment(DedicatedServerEndpointConfig.PublicPortEnvironmentVariable, "30001");

        DedicatedServerEndpointConfig config = Parse();

        Assert.That(config.PublicIp, Is.EqualTo("198.51.100.20"));
        Assert.That(config.PublicPort, Is.EqualTo(30001));
    }

    [Test]
    public void Parse_InvalidIp_Fails()
    {
        AssertParseFails(
            new[] { "-publicIp", "server.example.com" },
            "-publicIp",
            "IPv4");
    }

    [Test]
    public void Parse_PortZero_Fails()
    {
        AssertParseFails(new[] { "-gamePort", "0" }, "-gamePort", "1 to 65535");
    }

    [Test]
    public void Parse_PortAbove65535_Fails()
    {
        AssertParseFails(new[] { "-gamePort", "65536" }, "-gamePort", "1 to 65535");
    }

    [Test]
    public void Parse_InvalidEnvironmentPort_DoesNotUseDefault()
    {
        SetEnvironment(DedicatedServerEndpointConfig.GamePortEnvironmentVariable, "invalid");

        AssertParseFails(
            new string[0],
            DedicatedServerEndpointConfig.GamePortEnvironmentVariable,
            "1 to 65535");
    }

    [Test]
    public void Parse_MissingOptionValue_Fails()
    {
        AssertParseFails(new[] { "-publicIp" }, "-publicIp", "requires a value");
    }

    [Test]
    public void Parse_OptionFollowedByAnotherOption_FailsAsMissingValue()
    {
        AssertParseFails(
            new[] { "-gamePort", "-relayOnly" },
            "-gamePort",
            "requires a value");
    }

    [TestCase("true", true)]
    [TestCase("false", false)]
    [TestCase("1", true)]
    [TestCase("0", false)]
    [TestCase("TrUe", true)]
    [TestCase("FaLsE", false)]
    public void Parse_ValidCommandLineRelayOnlyValues_AreAccepted(string value, bool expected)
    {
        DedicatedServerEndpointConfig config = Parse("-relayOnly", value);

        Assert.That(config.RelayOnly, Is.EqualTo(expected));
    }

    [TestCase("true", true)]
    [TestCase("false", false)]
    [TestCase("1", true)]
    [TestCase("0", false)]
    public void Parse_ValidEnvironmentRelayOnlyValues_AreAccepted(string value, bool expected)
    {
        SetEnvironment(DedicatedServerEndpointConfig.RelayOnlyEnvironmentVariable, value);

        DedicatedServerEndpointConfig config = Parse();

        Assert.That(config.RelayOnly, Is.EqualTo(expected));
    }

    [Test]
    public void Parse_BareRelayOnlyOption_EnablesRelayOnly()
    {
        DedicatedServerEndpointConfig config = Parse("-relayOnly", "-batchmode");

        Assert.That(config.RelayOnly, Is.True);
    }

    [Test]
    public void Parse_InvalidRelayOnlyValue_Fails()
    {
        AssertParseFails(
            new[] { "-relayOnly", "yes" },
            "-relayOnly",
            "true, false, 1, or 0");
    }

    [Test]
    public void Parse_UnrelatedCommandLineArguments_AreIgnored()
    {
        DedicatedServerEndpointConfig config = Parse(
            "2dgame-server.x86_64",
            "-batchmode",
            "-nographics",
            "-logFile",
            "/tmp/server.log");

        Assert.That(config.GamePort, Is.EqualTo(27015));
        Assert.That(config.RelayOnly, Is.False);
    }

    [Test]
    public void Parse_CommandLineOptionNames_AreCaseInsensitive()
    {
        DedicatedServerEndpointConfig config = Parse(
            "-GAMEPORT", "29000",
            "-PUBLICIP", "192.0.2.40",
            "-PUBLICPORT", "29001",
            "-RELAYONLY", "FALSE");

        Assert.That(config.GamePort, Is.EqualTo(29000));
        Assert.That(config.PublicIp, Is.EqualTo("192.0.2.40"));
        Assert.That(config.PublicPort, Is.EqualTo(29001));
        Assert.That(config.RelayOnly, Is.False);
    }

    private static DedicatedServerEndpointConfig Parse(params string[] args)
    {
        bool ok = DedicatedServerEndpointConfig.TryParse(
            args,
            out DedicatedServerEndpointConfig config,
            out string error);

        Assert.That(ok, Is.True, error);
        Assert.That(config, Is.Not.Null);
        return config;
    }

    private static void AssertParseFails(string[] args, params string[] expectedErrorParts)
    {
        bool ok = DedicatedServerEndpointConfig.TryParse(
            args,
            out DedicatedServerEndpointConfig config,
            out string error);

        Assert.That(ok, Is.False);
        Assert.That(config, Is.Null);
        Assert.That(error, Is.Not.Null.And.Not.Empty);

        foreach (string expected in expectedErrorParts)
            StringAssert.Contains(expected, error);
    }

    private static void SetEnvironment(string variable, string value)
    {
        Environment.SetEnvironmentVariable(variable, value);
    }
}
