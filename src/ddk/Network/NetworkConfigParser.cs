// ProtonOS DDK - Network Configuration Parser
// Parses /etc/network/interfaces style configuration files.

using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Network;

/// <summary>
/// Parsed interface configuration from config file.
/// </summary>
public struct InterfaceConfig
{
    public string Name;
    public ConfigMode Mode;
    public uint Address;
    public uint Netmask;
    public uint Gateway;
    public uint DnsServer;
    public uint DnsServer2;
    public bool AutoStart;
}

/// <summary>
/// Parser for network configuration files.
///
/// Config file format:
/// <code>
/// # Comment
/// [interface-name]
/// type=dhcp|static|loopback
/// address=192.168.1.100
/// netmask=255.255.255.0
/// gateway=192.168.1.1
/// dns=8.8.8.8
/// dns2=8.8.4.4
/// auto=yes|no
/// </code>
/// </summary>
public static unsafe class NetworkConfigParser
{
    /// <summary>
    /// Parse a network configuration file.
    /// </summary>
    /// <param name="data">File content.</param>
    /// <param name="length">Content length.</param>
    /// <param name="configs">Output array for parsed configurations.</param>
    /// <param name="maxConfigs">Maximum configurations to parse.</param>
    /// <param name="configCount">Output: number of configurations parsed.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool Parse(byte* data, int length, InterfaceConfig[] configs,
                             out int configCount)
    {
        configCount = 0;

        if (data == null || length <= 0 || configs == null || configs.Length <= 0)
            return false;

        int pos = 0;
        InterfaceConfig currentConfig = new InterfaceConfig();
        bool inSection = false;

        while (pos < length && configCount < configs.Length)
        {
            // Skip whitespace
            while (pos < length && (data[pos] == ' ' || data[pos] == '\t'))
                pos++;

            if (pos >= length)
                break;

            // Skip empty lines
            if (data[pos] == '\n' || data[pos] == '\r')
            {
                pos++;
                continue;
            }

            // Skip comments
            if (data[pos] == '#')
            {
                while (pos < length && data[pos] != '\n')
                    pos++;
                continue;
            }

            // Section header: [interface-name]
            if (data[pos] == '[')
            {
                // Save previous section if valid
                if (inSection && currentConfig.Name != null)
                {
                    configs[configCount++] = currentConfig;
                }

                // Start new section
                currentConfig = new InterfaceConfig();
                currentConfig.AutoStart = true;  // Default to auto
                inSection = true;

                pos++;  // Skip '['
                int nameStart = pos;
                while (pos < length && data[pos] != ']' && data[pos] != '\n')
                    pos++;

                int nameLen = pos - nameStart;
                if (nameLen > 0 && pos < length && data[pos] == ']')
                {
                    currentConfig.Name = ParseString(data + nameStart, nameLen);
                    pos++;  // Skip ']'
                }

                // Skip to end of line
                while (pos < length && data[pos] != '\n')
                    pos++;
                continue;
            }

            // Key=value pair
            if (inSection)
            {
                int keyStart = pos;
                while (pos < length && data[pos] != '=' && data[pos] != '\n')
                    pos++;

                if (pos < length && data[pos] == '=')
                {
                    int keyLen = pos - keyStart;
                    pos++;  // Skip '='

                    int valueStart = pos;
                    while (pos < length && data[pos] != '\n' && data[pos] != '\r')
                        pos++;
                    int valueLen = pos - valueStart;

                    // Trim trailing whitespace from value
                    while (valueLen > 0 && (data[valueStart + valueLen - 1] == ' ' ||
                                            data[valueStart + valueLen - 1] == '\t'))
                        valueLen--;

                    ProcessKeyValue(ref currentConfig, data + keyStart, keyLen,
                                   data + valueStart, valueLen);
                }
            }

            // Skip to end of line
            while (pos < length && data[pos] != '\n')
                pos++;
        }

        // Save last section
        if (inSection && currentConfig.Name != null)
        {
            configs[configCount++] = currentConfig;
        }

        return configCount > 0;
    }

    /// <summary>
    /// Process a key=value pair.
    /// </summary>
    private static void ProcessKeyValue(ref InterfaceConfig config,
                                         byte* key, int keyLen,
                                         byte* value, int valueLen)
    {
        if (MatchKey(key, keyLen, "type"))
        {
            if (MatchValue(value, valueLen, "dhcp"))
                config.Mode = ConfigMode.DHCP;
            else if (MatchValue(value, valueLen, "static"))
                config.Mode = ConfigMode.Static;
            else if (MatchValue(value, valueLen, "loopback"))
                config.Mode = ConfigMode.Static;  // Loopback uses static config
        }
        else if (MatchKey(key, keyLen, "address"))
        {
            config.Address = ParseIPAddress(value, valueLen);
        }
        else if (MatchKey(key, keyLen, "netmask"))
        {
            config.Netmask = ParseIPAddress(value, valueLen);
        }
        else if (MatchKey(key, keyLen, "gateway"))
        {
            config.Gateway = ParseIPAddress(value, valueLen);
        }
        else if (MatchKey(key, keyLen, "dns"))
        {
            config.DnsServer = ParseIPAddress(value, valueLen);
        }
        else if (MatchKey(key, keyLen, "dns2"))
        {
            config.DnsServer2 = ParseIPAddress(value, valueLen);
        }
        else if (MatchKey(key, keyLen, "auto"))
        {
            config.AutoStart = MatchValue(value, valueLen, "yes") ||
                              MatchValue(value, valueLen, "true") ||
                              MatchValue(value, valueLen, "1");
        }
    }

    /// <summary>
    /// Check if key matches expected string (case-insensitive).
    /// </summary>
    private static bool MatchKey(byte* key, int keyLen, string expected)
    {
        if (keyLen != expected.Length)
            return false;

        for (int i = 0; i < keyLen; i++)
        {
            byte c = key[i];
            // Convert to lowercase
            if (c >= 'A' && c <= 'Z')
                c = (byte)(c + 32);

            if (c != expected[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Check if value matches expected string (case-insensitive).
    /// </summary>
    private static bool MatchValue(byte* value, int valueLen, string expected)
    {
        if (valueLen != expected.Length)
            return false;

        for (int i = 0; i < valueLen; i++)
        {
            byte c = value[i];
            if (c >= 'A' && c <= 'Z')
                c = (byte)(c + 32);

            if (c != expected[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Parse an IP address string to host byte order uint.
    /// </summary>
    /// <param name="str">IP address string (e.g., "192.168.1.1").</param>
    /// <param name="len">String length.</param>
    /// <returns>IP address in host byte order, or 0 on error.</returns>
    public static uint ParseIPAddress(byte* str, int len)
    {
        if (str == null || len <= 0)
            return 0;

        uint result = 0;
        int octetCount = 0;
        uint currentOctet = 0;

        for (int i = 0; i <= len; i++)
        {
            byte c = (i < len) ? str[i] : (byte)'.';  // Treat end as '.'

            if (c >= '0' && c <= '9')
            {
                currentOctet = currentOctet * 10 + (uint)(c - '0');
                if (currentOctet > 255)
                    return 0;  // Invalid octet
            }
            else if (c == '.')
            {
                if (octetCount >= 4)
                    return 0;  // Too many octets

                result = (result << 8) | currentOctet;
                currentOctet = 0;
                octetCount++;
            }
            else if (c != ' ' && c != '\t')
            {
                return 0;  // Invalid character
            }
        }

        if (octetCount != 4)
            return 0;  // Not enough octets

        return result;
    }

    /// <summary>
    /// Parse a string from bytes.
    /// </summary>
    private static string ParseString(byte* data, int len)
    {
        if (data == null || len <= 0)
            return "";

        char[] chars = new char[len];
        for (int i = 0; i < len; i++)
            chars[i] = (char)data[i];

        return new string(chars);
    }
}
