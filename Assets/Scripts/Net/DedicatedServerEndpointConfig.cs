using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Validated command-line and environment configuration for a dedicated server's UDP endpoint.
/// This type has no Unity dependency so configuration failures can be covered by EditMode tests.
/// </summary>
public sealed class DedicatedServerEndpointConfig
{
    public const ushort DefaultGamePort = 27015;

    public const string GamePortEnvironmentVariable = "GAME_PORT";
    public const string PublicIpEnvironmentVariable = "PUBLIC_IP";
    public const string PublicPortEnvironmentVariable = "PUBLIC_PORT";
    public const string RelayOnlyEnvironmentVariable = "FUSION_RELAY_ONLY";

    private const string GamePortOption = "-gamePort";
    private const string PublicIpOption = "-publicIp";
    private const string PublicPortOption = "-publicPort";
    private const string RelayOnlyOption = "-relayOnly";

    private DedicatedServerEndpointConfig(
        ushort gamePort,
        string publicIp,
        ushort publicPort,
        bool relayOnly)
    {
        GamePort = gamePort;
        PublicIp = publicIp;
        PublicPort = publicPort;
        RelayOnly = relayOnly;
    }

    public ushort GamePort { get; }
    public string PublicIp { get; }
    public ushort PublicPort { get; }
    public bool RelayOnly { get; }
    public bool HasPublicEndpoint => PublicIp != null;

    public static bool TryParse(
        IReadOnlyList<string> commandLineArgs,
        out DedicatedServerEndpointConfig config,
        out string error)
    {
        return TryParse(commandLineArgs, Environment.GetEnvironmentVariable, out config, out error);
    }

    public static bool TryParse(
        IReadOnlyList<string> commandLineArgs,
        Func<string, string> getEnvironmentVariable,
        out DedicatedServerEndpointConfig config,
        out string error)
    {
        if (getEnvironmentVariable == null)
            throw new ArgumentNullException(nameof(getEnvironmentVariable));

        config = null;
        error = null;

        if (!TryReadCommandLine(commandLineArgs, out CommandLineValues commandLine, out error))
            return false;

        string rawGamePort = SelectValue(
            commandLine.HasGamePort,
            commandLine.GamePort,
            GamePortEnvironmentVariable,
            getEnvironmentVariable,
            out string gamePortSource);

        ushort gamePort = DefaultGamePort;
        if (rawGamePort != null && !TryParsePort(rawGamePort, gamePortSource, out gamePort, out error))
            return false;

        string rawPublicIp = SelectValue(
            commandLine.HasPublicIp,
            commandLine.PublicIp,
            PublicIpEnvironmentVariable,
            getEnvironmentVariable,
            out string publicIpSource);

        string publicIp = null;
        if (rawPublicIp != null && !TryNormalizeIpv4(rawPublicIp, out publicIp))
        {
            error = $"{publicIpSource} must be an IPv4 address in four-part dotted-decimal form.";
            return false;
        }

        string rawPublicPort = SelectValue(
            commandLine.HasPublicPort,
            commandLine.PublicPort,
            PublicPortEnvironmentVariable,
            getEnvironmentVariable,
            out string publicPortSource);

        ushort publicPort = gamePort;
        if (rawPublicPort != null &&
            !TryParsePort(rawPublicPort, publicPortSource, out publicPort, out error))
        {
            return false;
        }

        string rawRelayOnly = SelectValue(
            commandLine.HasRelayOnly,
            commandLine.RelayOnly,
            RelayOnlyEnvironmentVariable,
            getEnvironmentVariable,
            out string relayOnlySource);

        bool relayOnly = false;
        if (rawRelayOnly != null && !TryParseBoolean(rawRelayOnly, out relayOnly))
        {
            error = $"{relayOnlySource} must be true, false, 1, or 0.";
            return false;
        }

        config = new DedicatedServerEndpointConfig(gamePort, publicIp, publicPort, relayOnly);
        return true;
    }

    private static string SelectValue(
        bool hasCommandLineValue,
        string commandLineValue,
        string environmentVariable,
        Func<string, string> getEnvironmentVariable,
        out string source)
    {
        if (hasCommandLineValue)
        {
            source = CommandLineOptionFor(environmentVariable);
            return commandLineValue;
        }

        source = environmentVariable;
        return getEnvironmentVariable(environmentVariable);
    }

    private static string CommandLineOptionFor(string environmentVariable)
    {
        switch (environmentVariable)
        {
            case GamePortEnvironmentVariable:
                return GamePortOption;
            case PublicIpEnvironmentVariable:
                return PublicIpOption;
            case PublicPortEnvironmentVariable:
                return PublicPortOption;
            default:
                return RelayOnlyOption;
        }
    }

    private static bool TryReadCommandLine(
        IReadOnlyList<string> args,
        out CommandLineValues values,
        out string error)
    {
        values = default;
        error = null;

        if (args == null)
            return true;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];

            if (OptionEquals(arg, GamePortOption))
            {
                if (!TryReadRequiredValue(args, ref i, GamePortOption, out values.GamePort, out error))
                    return false;
                values.HasGamePort = true;
            }
            else if (OptionEquals(arg, PublicIpOption))
            {
                if (!TryReadRequiredValue(args, ref i, PublicIpOption, out values.PublicIp, out error))
                    return false;
                values.HasPublicIp = true;
            }
            else if (OptionEquals(arg, PublicPortOption))
            {
                if (!TryReadRequiredValue(args, ref i, PublicPortOption, out values.PublicPort, out error))
                    return false;
                values.HasPublicPort = true;
            }
            else if (OptionEquals(arg, RelayOnlyOption))
            {
                values.HasRelayOnly = true;
                values.RelayOnly = "true";

                if (i + 1 < args.Count &&
                    args[i + 1] != null &&
                    !LooksLikeOption(args[i + 1]))
                {
                    values.RelayOnly = args[++i];
                }
            }
        }

        return true;
    }

    private static bool TryReadRequiredValue(
        IReadOnlyList<string> args,
        ref int optionIndex,
        string option,
        out string value,
        out string error)
    {
        int valueIndex = optionIndex + 1;
        if (valueIndex >= args.Count ||
            string.IsNullOrEmpty(args[valueIndex]) ||
            LooksLikeOption(args[valueIndex]))
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }

        value = args[valueIndex];
        optionIndex = valueIndex;
        error = null;
        return true;
    }

    private static bool OptionEquals(string value, string option)
    {
        return string.Equals(value, option, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeOption(string value)
    {
        return value.Length > 0 && value[0] == '-';
    }

    private static bool TryParsePort(string value, string source, out ushort port, out string error)
    {
        if (uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed) &&
            parsed >= 1 &&
            parsed <= ushort.MaxValue)
        {
            port = (ushort)parsed;
            error = null;
            return true;
        }

        port = 0;
        error = $"{source} must be a whole-number port from 1 to 65535.";
        return false;
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1")
        {
            result = true;
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || value == "0")
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryNormalizeIpv4(string value, out string normalized)
    {
        normalized = null;
        if (string.IsNullOrEmpty(value))
            return false;

        string[] parts = value.Split('.');
        if (parts.Length != 4)
            return false;

        var octets = new byte[4];
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length == 0 || part.Length > 3)
                return false;

            for (int character = 0; character < part.Length; character++)
            {
                if (part[character] < '0' || part[character] > '9')
                    return false;
            }

            if (!byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out octets[i]))
                return false;
        }

        normalized = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}.{2}.{3}",
            octets[0],
            octets[1],
            octets[2],
            octets[3]);
        return true;
    }

    private struct CommandLineValues
    {
        public bool HasGamePort;
        public string GamePort;
        public bool HasPublicIp;
        public string PublicIp;
        public bool HasPublicPort;
        public string PublicPort;
        public bool HasRelayOnly;
        public string RelayOnly;
    }
}
