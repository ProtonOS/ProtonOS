// ProtonOS VirtioNet - Network Stack Unit Tests
// Tests for Ethernet, ARP, and NetworkStack without requiring hardware

using System;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Network.Stack;

namespace ProtonOS.Drivers.Network.VirtioNet;

/// <summary>
/// Unit tests for the network stack.
/// These tests verify protocol logic without requiring actual network hardware.
/// </summary>
public static unsafe class NetworkStackTests
{
    private static int _passed;
    private static int _failed;

    /// <summary>
    /// Run all network stack unit tests.
    /// </summary>
    /// <returns>True if all tests pass.</returns>
    public static bool RunAll()
    {
        _passed = 0;
        _failed = 0;

        Debug.WriteLine("[NetTests] Running network stack unit tests...");

        // Ethernet tests
        TestEthernetBuildHeader();
        TestEthernetParse();
        TestEthernetBroadcast();
        TestEthernetCompareMac();

        // ARP tests
        TestArpMakeIP();
        TestArpBuildRequest();
        TestArpBuildReply();
        TestArpParse();
        TestArpCache();

        // NetworkStack tests
        TestNetworkConfig();
        TestIsLocalSubnet();

        // ICMP tests
        TestIcmpChecksum();
        TestIcmpBuildEchoRequest();
        TestIcmpBuildEchoReply();
        TestIcmpParse();

        // UDP tests
        TestUdpBuildPacket();
        TestUdpBuildPacketWithChecksum();
        TestUdpParse();
        TestUdpChecksum();

        // TCP tests
        TestTcpBuildPacket();
        TestTcpBuildPacketWithChecksum();
        TestTcpParse();
        TestTcpChecksum();
        TestTcpBuildSyn();
        TestTcpBuildAck();
        TestTcpBuildFinAck();
        TestTcpBuildData();

        // Report results
        Debug.Write("[NetTests] Results: ");
        Debug.WriteDecimal(_passed);
        Debug.Write(" passed, ");
        Debug.WriteDecimal(_failed);
        Debug.WriteLine(" failed");

        return _failed == 0;
    }

    private static void Pass(string testName)
    {
        _passed++;
        Debug.Write("[NetTests] PASS: ");
        Debug.WriteLine(testName);
    }

    private static void Fail(string testName, string reason)
    {
        _failed++;
        Debug.Write("[NetTests] FAIL: ");
        Debug.Write(testName);
        Debug.Write(" - ");
        Debug.WriteLine(reason);
    }

    // ===== Ethernet Tests =====

    private static void TestEthernetBuildHeader()
    {
        byte* buffer = stackalloc byte[64];
        byte* destMac = stackalloc byte[6];
        byte* srcMac = stackalloc byte[6];

        // Set up MACs
        destMac[0] = 0x11; destMac[1] = 0x22; destMac[2] = 0x33;
        destMac[3] = 0x44; destMac[4] = 0x55; destMac[5] = 0x66;
        srcMac[0] = 0xAA; srcMac[1] = 0xBB; srcMac[2] = 0xCC;
        srcMac[3] = 0xDD; srcMac[4] = 0xEE; srcMac[5] = 0xFF;

        int len = Ethernet.BuildHeader(buffer, destMac, srcMac, EtherType.IPv4);

        if (len != 14)
        {
            Fail("EthernetBuildHeader", "wrong length");
            return;
        }

        // Check dest MAC
        bool destOk = buffer[0] == 0x11 && buffer[1] == 0x22 && buffer[2] == 0x33 &&
                      buffer[3] == 0x44 && buffer[4] == 0x55 && buffer[5] == 0x66;
        if (!destOk)
        {
            Fail("EthernetBuildHeader", "wrong dest MAC");
            return;
        }

        // Check src MAC
        bool srcOk = buffer[6] == 0xAA && buffer[7] == 0xBB && buffer[8] == 0xCC &&
                     buffer[9] == 0xDD && buffer[10] == 0xEE && buffer[11] == 0xFF;
        if (!srcOk)
        {
            Fail("EthernetBuildHeader", "wrong src MAC");
            return;
        }

        // Check EtherType (big endian: 0x0800)
        if (buffer[12] != 0x08 || buffer[13] != 0x00)
        {
            Fail("EthernetBuildHeader", "wrong EtherType");
            return;
        }

        Pass("EthernetBuildHeader");
    }

    private static void TestEthernetParse()
    {
        // Build a test frame
        byte* frame = stackalloc byte[64];

        // Dest MAC
        frame[0] = 0x11; frame[1] = 0x22; frame[2] = 0x33;
        frame[3] = 0x44; frame[4] = 0x55; frame[5] = 0x66;
        // Src MAC
        frame[6] = 0xAA; frame[7] = 0xBB; frame[8] = 0xCC;
        frame[9] = 0xDD; frame[10] = 0xEE; frame[11] = 0xFF;
        // EtherType: ARP (0x0806)
        frame[12] = 0x08; frame[13] = 0x06;
        // Payload
        frame[14] = 0xDE; frame[15] = 0xAD;

        EthernetFrame parsed;
        bool ok = Ethernet.Parse(frame, 60, out parsed);

        if (!ok)
        {
            Fail("EthernetParse", "parse failed");
            return;
        }

        if (parsed.EtherType != EtherType.ARP)
        {
            Fail("EthernetParse", "wrong EtherType");
            return;
        }

        if (parsed.PayloadLength != 46)
        {
            Fail("EthernetParse", "wrong payload length");
            return;
        }

        if (parsed.Payload[0] != 0xDE || parsed.Payload[1] != 0xAD)
        {
            Fail("EthernetParse", "wrong payload data");
            return;
        }

        Pass("EthernetParse");
    }

    private static void TestEthernetBroadcast()
    {
        byte* mac = stackalloc byte[6];

        // Test SetBroadcast
        Ethernet.SetBroadcast(mac);

        bool allFF = mac[0] == 0xFF && mac[1] == 0xFF && mac[2] == 0xFF &&
                     mac[3] == 0xFF && mac[4] == 0xFF && mac[5] == 0xFF;
        if (!allFF)
        {
            Fail("EthernetBroadcast", "SetBroadcast didn't set all 0xFF");
            return;
        }

        // Test IsBroadcast (should be true)
        if (!Ethernet.IsBroadcast(mac))
        {
            Fail("EthernetBroadcast", "IsBroadcast returned false for broadcast");
            return;
        }

        // Test IsBroadcast with non-broadcast
        mac[0] = 0x00;
        if (Ethernet.IsBroadcast(mac))
        {
            Fail("EthernetBroadcast", "IsBroadcast returned true for non-broadcast");
            return;
        }

        Pass("EthernetBroadcast");
    }

    private static void TestEthernetCompareMac()
    {
        byte* mac1 = stackalloc byte[6];
        byte* mac2 = stackalloc byte[6];

        // Set same values
        mac1[0] = 0x52; mac1[1] = 0x54; mac1[2] = 0x00;
        mac1[3] = 0x12; mac1[4] = 0x34; mac1[5] = 0x56;
        mac2[0] = 0x52; mac2[1] = 0x54; mac2[2] = 0x00;
        mac2[3] = 0x12; mac2[4] = 0x34; mac2[5] = 0x56;

        if (!Ethernet.CompareMac(mac1, mac2))
        {
            Fail("EthernetCompareMac", "same MACs returned false");
            return;
        }

        // Make them different
        mac2[5] = 0x99;
        if (Ethernet.CompareMac(mac1, mac2))
        {
            Fail("EthernetCompareMac", "different MACs returned true");
            return;
        }

        Pass("EthernetCompareMac");
    }

    // ===== ARP Tests =====

    private static void TestArpMakeIP()
    {
        // Test 10.0.2.15
        uint ip = ARP.MakeIP(10, 0, 2, 15);
        uint expected = (10u << 24) | (0u << 16) | (2u << 8) | 15u;

        if (ip != expected)
        {
            Fail("ArpMakeIP", "10.0.2.15 incorrect");
            return;
        }

        // Test 192.168.1.1
        ip = ARP.MakeIP(192, 168, 1, 1);
        expected = (192u << 24) | (168u << 16) | (1u << 8) | 1u;

        if (ip != expected)
        {
            Fail("ArpMakeIP", "192.168.1.1 incorrect");
            return;
        }

        Pass("ArpMakeIP");
    }

    private static void TestArpBuildRequest()
    {
        byte* buffer = stackalloc byte[64];
        byte* senderMac = stackalloc byte[6];

        senderMac[0] = 0x52; senderMac[1] = 0x54; senderMac[2] = 0x00;
        senderMac[3] = 0x12; senderMac[4] = 0x34; senderMac[5] = 0x56;

        uint senderIP = ARP.MakeIP(10, 0, 2, 15);
        uint targetIP = ARP.MakeIP(10, 0, 2, 2);

        int len = ARP.BuildRequest(buffer, senderMac, senderIP, targetIP);

        if (len != 28)
        {
            Fail("ArpBuildRequest", "wrong length");
            return;
        }

        // Check hardware type (Ethernet = 1)
        if (buffer[0] != 0x00 || buffer[1] != 0x01)
        {
            Fail("ArpBuildRequest", "wrong hardware type");
            return;
        }

        // Check protocol type (IPv4 = 0x0800)
        if (buffer[2] != 0x08 || buffer[3] != 0x00)
        {
            Fail("ArpBuildRequest", "wrong protocol type");
            return;
        }

        // Check operation (Request = 1)
        if (buffer[6] != 0x00 || buffer[7] != 0x01)
        {
            Fail("ArpBuildRequest", "wrong operation");
            return;
        }

        // Check target IP (10.0.2.2)
        if (buffer[24] != 10 || buffer[25] != 0 || buffer[26] != 2 || buffer[27] != 2)
        {
            Fail("ArpBuildRequest", "wrong target IP");
            return;
        }

        Pass("ArpBuildRequest");
    }

    private static void TestArpBuildReply()
    {
        byte* buffer = stackalloc byte[64];
        byte* senderMac = stackalloc byte[6];
        byte* targetMac = stackalloc byte[6];

        senderMac[0] = 0x52; senderMac[1] = 0x55; senderMac[2] = 0x0A;
        senderMac[3] = 0x00; senderMac[4] = 0x02; senderMac[5] = 0x02;
        targetMac[0] = 0x52; targetMac[1] = 0x54; targetMac[2] = 0x00;
        targetMac[3] = 0x12; targetMac[4] = 0x34; targetMac[5] = 0x56;

        uint senderIP = ARP.MakeIP(10, 0, 2, 2);
        uint targetIP = ARP.MakeIP(10, 0, 2, 15);

        int len = ARP.BuildReply(buffer, senderMac, senderIP, targetMac, targetIP);

        if (len != 28)
        {
            Fail("ArpBuildReply", "wrong length");
            return;
        }

        // Check operation (Reply = 2)
        if (buffer[6] != 0x00 || buffer[7] != 0x02)
        {
            Fail("ArpBuildReply", "wrong operation");
            return;
        }

        Pass("ArpBuildReply");
    }

    private static void TestArpParse()
    {
        // Build a valid ARP packet
        byte* buffer = stackalloc byte[28];

        // Hardware type: Ethernet (1)
        buffer[0] = 0x00; buffer[1] = 0x01;
        // Protocol type: IPv4 (0x0800)
        buffer[2] = 0x08; buffer[3] = 0x00;
        // Hardware length: 6
        buffer[4] = 6;
        // Protocol length: 4
        buffer[5] = 4;
        // Operation: Reply (2)
        buffer[6] = 0x00; buffer[7] = 0x02;
        // Sender MAC
        buffer[8] = 0x52; buffer[9] = 0x55; buffer[10] = 0x0A;
        buffer[11] = 0x00; buffer[12] = 0x02; buffer[13] = 0x02;
        // Sender IP: 10.0.2.2
        buffer[14] = 10; buffer[15] = 0; buffer[16] = 2; buffer[17] = 2;
        // Target MAC
        buffer[18] = 0x52; buffer[19] = 0x54; buffer[20] = 0x00;
        buffer[21] = 0x12; buffer[22] = 0x34; buffer[23] = 0x56;
        // Target IP: 10.0.2.15
        buffer[24] = 10; buffer[25] = 0; buffer[26] = 2; buffer[27] = 15;

        ArpPacket packet;
        bool ok = ARP.Parse(buffer, 28, out packet);

        if (!ok)
        {
            Fail("ArpParse", "parse failed");
            return;
        }

        if (packet.Operation != ArpOperation.Reply)
        {
            Fail("ArpParse", "wrong operation");
            return;
        }

        uint senderIP = ARP.GetSenderIP(&packet);
        uint expectedIP = ARP.MakeIP(10, 0, 2, 2);
        if (senderIP != expectedIP)
        {
            Fail("ArpParse", "wrong sender IP");
            return;
        }

        Pass("ArpParse");
    }

    private static void TestArpCache()
    {
        var cache = new ArpCache();
        byte* mac = stackalloc byte[6];
        byte* lookupMac = stackalloc byte[6];

        mac[0] = 0x52; mac[1] = 0x55; mac[2] = 0x0A;
        mac[3] = 0x00; mac[4] = 0x02; mac[5] = 0x02;

        uint ip = ARP.MakeIP(10, 0, 2, 2);

        // Should not find before adding
        if (cache.Lookup(ip, lookupMac))
        {
            Fail("ArpCache", "found entry before adding");
            return;
        }

        // Add entry
        cache.Update(ip, mac);

        // Should find after adding
        if (!cache.Lookup(ip, lookupMac))
        {
            Fail("ArpCache", "didn't find entry after adding");
            return;
        }

        // Verify MAC is correct
        bool macMatch = lookupMac[0] == 0x52 && lookupMac[1] == 0x55 && lookupMac[2] == 0x0A &&
                        lookupMac[3] == 0x00 && lookupMac[4] == 0x02 && lookupMac[5] == 0x02;
        if (!macMatch)
        {
            Fail("ArpCache", "MAC mismatch");
            return;
        }

        Pass("ArpCache");
    }

    // ===== NetworkStack Tests =====

    private static void TestNetworkConfig()
    {
        var config = new NetworkConfig();
        config.IPAddress = ARP.MakeIP(10, 0, 2, 15);
        config.SubnetMask = ARP.MakeIP(255, 255, 255, 0);
        config.Gateway = ARP.MakeIP(10, 0, 2, 2);

        if (config.IPAddress != ARP.MakeIP(10, 0, 2, 15))
        {
            Fail("NetworkConfig", "IP mismatch");
            return;
        }

        if (config.SubnetMask != ARP.MakeIP(255, 255, 255, 0))
        {
            Fail("NetworkConfig", "mask mismatch");
            return;
        }

        if (config.Gateway != ARP.MakeIP(10, 0, 2, 2))
        {
            Fail("NetworkConfig", "gateway mismatch");
            return;
        }

        Pass("NetworkConfig");
    }

    private static void TestIsLocalSubnet()
    {
        var config = new NetworkConfig();
        config.IPAddress = ARP.MakeIP(10, 0, 2, 15);
        config.SubnetMask = ARP.MakeIP(255, 255, 255, 0);

        // 10.0.2.2 should be local
        uint localIP = ARP.MakeIP(10, 0, 2, 2);
        if (!config.IsLocalSubnet(localIP))
        {
            Fail("IsLocalSubnet", "10.0.2.2 not detected as local");
            return;
        }

        // 10.0.2.100 should be local
        uint local2 = ARP.MakeIP(10, 0, 2, 100);
        if (!config.IsLocalSubnet(local2))
        {
            Fail("IsLocalSubnet", "10.0.2.100 not detected as local");
            return;
        }

        // 10.0.3.1 should NOT be local
        uint remoteIP = ARP.MakeIP(10, 0, 3, 1);
        if (config.IsLocalSubnet(remoteIP))
        {
            Fail("IsLocalSubnet", "10.0.3.1 incorrectly detected as local");
            return;
        }

        // 8.8.8.8 should NOT be local
        uint internet = ARP.MakeIP(8, 8, 8, 8);
        if (config.IsLocalSubnet(internet))
        {
            Fail("IsLocalSubnet", "8.8.8.8 incorrectly detected as local");
            return;
        }

        Pass("IsLocalSubnet");
    }

    // ===== ICMP Tests =====

    private static void TestIcmpChecksum()
    {
        // Build an ICMP echo request and verify checksum validates
        byte* buffer = stackalloc byte[64];

        int len = ICMP.BuildEchoRequest(buffer, 0x1234, 0x0001, null, 0);
        if (len != 8)
        {
            Fail("IcmpChecksum", "wrong echo request length");
            return;
        }

        // Verify checksum is valid
        if (!ICMP.VerifyChecksum(buffer, len))
        {
            Fail("IcmpChecksum", "checksum verification failed");
            return;
        }

        // Corrupt the packet and verify checksum fails
        buffer[4] ^= 0xFF;
        if (ICMP.VerifyChecksum(buffer, len))
        {
            Fail("IcmpChecksum", "corrupted packet passed checksum");
            return;
        }

        Pass("IcmpChecksum");
    }

    private static void TestIcmpBuildEchoRequest()
    {
        byte* buffer = stackalloc byte[64];

        int len = ICMP.BuildEchoRequest(buffer, 0x1234, 0x0005, null, 0);

        if (len != 8)
        {
            Fail("IcmpBuildEchoRequest", "wrong length");
            return;
        }

        // Type should be 8 (Echo Request)
        if (buffer[0] != IcmpType.EchoRequest)
        {
            Fail("IcmpBuildEchoRequest", "wrong type");
            return;
        }

        // Code should be 0
        if (buffer[1] != 0)
        {
            Fail("IcmpBuildEchoRequest", "wrong code");
            return;
        }

        // Identifier (big-endian): 0x1234
        if (buffer[4] != 0x12 || buffer[5] != 0x34)
        {
            Fail("IcmpBuildEchoRequest", "wrong identifier");
            return;
        }

        // Sequence (big-endian): 0x0005
        if (buffer[6] != 0x00 || buffer[7] != 0x05)
        {
            Fail("IcmpBuildEchoRequest", "wrong sequence");
            return;
        }

        Pass("IcmpBuildEchoRequest");
    }

    private static void TestIcmpBuildEchoReply()
    {
        byte* buffer = stackalloc byte[64];
        byte* payload = stackalloc byte[4];
        payload[0] = 0xDE; payload[1] = 0xAD; payload[2] = 0xBE; payload[3] = 0xEF;

        int len = ICMP.BuildEchoReply(buffer, 0x5678, 0x0003, payload, 4);

        if (len != 12) // 8 header + 4 payload
        {
            Fail("IcmpBuildEchoReply", "wrong length");
            return;
        }

        // Type should be 0 (Echo Reply)
        if (buffer[0] != IcmpType.EchoReply)
        {
            Fail("IcmpBuildEchoReply", "wrong type");
            return;
        }

        // Check payload was copied
        if (buffer[8] != 0xDE || buffer[9] != 0xAD || buffer[10] != 0xBE || buffer[11] != 0xEF)
        {
            Fail("IcmpBuildEchoReply", "payload not copied");
            return;
        }

        // Verify checksum
        if (!ICMP.VerifyChecksum(buffer, len))
        {
            Fail("IcmpBuildEchoReply", "checksum invalid");
            return;
        }

        Pass("IcmpBuildEchoReply");
    }

    private static void TestIcmpParse()
    {
        // Build and parse an echo request
        byte* buffer = stackalloc byte[64];
        int len = ICMP.BuildEchoRequest(buffer, 0xABCD, 0x0007, null, 0);

        IcmpPacket packet;
        bool ok = ICMP.Parse(buffer, len, out packet);

        if (!ok)
        {
            Fail("IcmpParse", "parse failed");
            return;
        }

        if (packet.Type != IcmpType.EchoRequest)
        {
            Fail("IcmpParse", "wrong type");
            return;
        }

        if (packet.Identifier != 0xABCD)
        {
            Fail("IcmpParse", "wrong identifier");
            return;
        }

        if (packet.Sequence != 0x0007)
        {
            Fail("IcmpParse", "wrong sequence");
            return;
        }

        if (packet.PayloadLength != 0)
        {
            Fail("IcmpParse", "wrong payload length");
            return;
        }

        Pass("IcmpParse");
    }

    // ===== UDP Tests =====

    private static void TestUdpBuildPacket()
    {
        byte* buffer = stackalloc byte[64];
        byte* payload = stackalloc byte[4];
        payload[0] = 0xDE; payload[1] = 0xAD; payload[2] = 0xBE; payload[3] = 0xEF;

        int len = UDP.BuildPacket(buffer, 12345, 53, payload, 4);

        if (len != 12) // 8 header + 4 payload
        {
            Fail("UdpBuildPacket", "wrong length");
            return;
        }

        // Source port (big-endian): 12345 = 0x3039
        if (buffer[0] != 0x30 || buffer[1] != 0x39)
        {
            Fail("UdpBuildPacket", "wrong source port");
            return;
        }

        // Dest port (big-endian): 53 = 0x0035
        if (buffer[2] != 0x00 || buffer[3] != 0x35)
        {
            Fail("UdpBuildPacket", "wrong dest port");
            return;
        }

        // Length (big-endian): 12 = 0x000C
        if (buffer[4] != 0x00 || buffer[5] != 0x0C)
        {
            Fail("UdpBuildPacket", "wrong length field");
            return;
        }

        // Checksum should be 0 (not computed)
        if (buffer[6] != 0x00 || buffer[7] != 0x00)
        {
            Fail("UdpBuildPacket", "checksum not zero");
            return;
        }

        // Payload
        if (buffer[8] != 0xDE || buffer[9] != 0xAD || buffer[10] != 0xBE || buffer[11] != 0xEF)
        {
            Fail("UdpBuildPacket", "payload not copied");
            return;
        }

        Pass("UdpBuildPacket");
    }

    private static void TestUdpBuildPacketWithChecksum()
    {
        byte* buffer = stackalloc byte[64];
        byte* payload = stackalloc byte[4];
        payload[0] = 0x48; payload[1] = 0x45; payload[2] = 0x4C; payload[3] = 0x4F; // "HELO"

        uint srcIP = ARP.MakeIP(10, 0, 2, 15);
        uint destIP = ARP.MakeIP(10, 0, 2, 2);

        int len = UDP.BuildPacketWithChecksum(buffer, 1234, 5678, payload, 4, srcIP, destIP);

        if (len != 12)
        {
            Fail("UdpBuildPacketWithChecksum", "wrong length");
            return;
        }

        // Checksum should NOT be zero
        ushort checksum = (ushort)((buffer[6] << 8) | buffer[7]);
        if (checksum == 0)
        {
            Fail("UdpBuildPacketWithChecksum", "checksum is zero");
            return;
        }

        // Verify the checksum is correct
        if (!UDP.VerifyChecksum(buffer, len, srcIP, destIP))
        {
            Fail("UdpBuildPacketWithChecksum", "checksum verification failed");
            return;
        }

        Pass("UdpBuildPacketWithChecksum");
    }

    private static void TestUdpParse()
    {
        // Build a UDP packet and parse it
        byte* buffer = stackalloc byte[64];
        byte* payload = stackalloc byte[5];
        payload[0] = (byte)'H'; payload[1] = (byte)'E'; payload[2] = (byte)'L'; payload[3] = (byte)'L'; payload[4] = (byte)'O';

        int len = UDP.BuildPacket(buffer, 8080, 80, payload, 5);

        UdpPacket packet;
        bool ok = UDP.Parse(buffer, len, out packet);

        if (!ok)
        {
            Fail("UdpParse", "parse failed");
            return;
        }

        if (packet.SourcePort != 8080)
        {
            Fail("UdpParse", "wrong source port");
            return;
        }

        if (packet.DestPort != 80)
        {
            Fail("UdpParse", "wrong dest port");
            return;
        }

        if (packet.Length != 13) // 8 header + 5 payload
        {
            Fail("UdpParse", "wrong length");
            return;
        }

        if (packet.PayloadLength != 5)
        {
            Fail("UdpParse", "wrong payload length");
            return;
        }

        // Verify payload content
        if (packet.Payload[0] != (byte)'H' || packet.Payload[1] != (byte)'E' ||
            packet.Payload[2] != (byte)'L' || packet.Payload[3] != (byte)'L' || packet.Payload[4] != (byte)'O')
        {
            Fail("UdpParse", "payload mismatch");
            return;
        }

        Pass("UdpParse");
    }

    private static void TestUdpChecksum()
    {
        byte* buffer = stackalloc byte[64];
        byte* payload = stackalloc byte[8];
        for (int i = 0; i < 8; i++) payload[i] = (byte)(i + 1);

        uint srcIP = ARP.MakeIP(192, 168, 1, 100);
        uint destIP = ARP.MakeIP(192, 168, 1, 1);

        int len = UDP.BuildPacketWithChecksum(buffer, 54321, 12345, payload, 8, srcIP, destIP);

        // Verify checksum is valid
        if (!UDP.VerifyChecksum(buffer, len, srcIP, destIP))
        {
            Fail("UdpChecksum", "checksum verification failed");
            return;
        }

        // Corrupt the packet and verify checksum fails
        buffer[10] ^= 0xFF;
        if (UDP.VerifyChecksum(buffer, len, srcIP, destIP))
        {
            Fail("UdpChecksum", "corrupted packet passed checksum");
            return;
        }

        Pass("UdpChecksum");
    }

    // ===== TCP Tests =====

    private static void TestTcpBuildPacket()
    {
        byte* buffer = stackalloc byte[64];
        byte* payload = stackalloc byte[4];
        payload[0] = 0xDE; payload[1] = 0xAD; payload[2] = 0xBE; payload[3] = 0xEF;

        int len = TCP.BuildPacket(buffer, 12345, 80, 0x12345678, 0xABCDEF00,
                                  TcpFlags.ACK, 8192, payload, 4);

        if (len != 24) // 20 header + 4 payload
        {
            Fail("TcpBuildPacket", "wrong length");
            return;
        }

        // Source port (big-endian): 12345 = 0x3039
        if (buffer[0] != 0x30 || buffer[1] != 0x39)
        {
            Fail("TcpBuildPacket", "wrong source port");
            return;
        }

        // Dest port (big-endian): 80 = 0x0050
        if (buffer[2] != 0x00 || buffer[3] != 0x50)
        {
            Fail("TcpBuildPacket", "wrong dest port");
            return;
        }

        // Sequence number (big-endian): 0x12345678
        if (buffer[4] != 0x12 || buffer[5] != 0x34 || buffer[6] != 0x56 || buffer[7] != 0x78)
        {
            Fail("TcpBuildPacket", "wrong seq number");
            return;
        }

        // ACK number (big-endian): 0xABCDEF00
        if (buffer[8] != 0xAB || buffer[9] != 0xCD || buffer[10] != 0xEF || buffer[11] != 0x00)
        {
            Fail("TcpBuildPacket", "wrong ack number");
            return;
        }

        // Data offset: 5 (20 bytes / 4) in upper nibble
        if ((buffer[12] >> 4) != 5)
        {
            Fail("TcpBuildPacket", "wrong data offset");
            return;
        }

        // Flags: ACK = 0x10
        if (buffer[13] != TcpFlags.ACK)
        {
            Fail("TcpBuildPacket", "wrong flags");
            return;
        }

        // Window (big-endian): 8192 = 0x2000
        if (buffer[14] != 0x20 || buffer[15] != 0x00)
        {
            Fail("TcpBuildPacket", "wrong window");
            return;
        }

        // Payload
        if (buffer[20] != 0xDE || buffer[21] != 0xAD || buffer[22] != 0xBE || buffer[23] != 0xEF)
        {
            Fail("TcpBuildPacket", "payload not copied");
            return;
        }

        Pass("TcpBuildPacket");
    }

    private static void TestTcpBuildPacketWithChecksum()
    {
        byte* buffer = stackalloc byte[64];
        byte* payload = stackalloc byte[4];
        payload[0] = 0x48; payload[1] = 0x45; payload[2] = 0x4C; payload[3] = 0x4F; // "HELO"

        uint srcIP = ARP.MakeIP(10, 0, 2, 15);
        uint destIP = ARP.MakeIP(10, 0, 2, 2);

        int len = TCP.BuildPacketWithChecksum(buffer, 1234, 5678, 0x11111111, 0x22222222,
                                               TcpFlags.PSH | TcpFlags.ACK, 4096,
                                               payload, 4, srcIP, destIP);

        if (len != 24) // 20 header + 4 payload
        {
            Fail("TcpBuildPacketWithChecksum", "wrong length");
            return;
        }

        // Checksum should NOT be zero
        ushort checksum = (ushort)((buffer[16] << 8) | buffer[17]);
        if (checksum == 0)
        {
            Fail("TcpBuildPacketWithChecksum", "checksum is zero");
            return;
        }

        // Verify the checksum is correct
        if (!TCP.VerifyChecksum(buffer, len, srcIP, destIP))
        {
            Fail("TcpBuildPacketWithChecksum", "checksum verification failed");
            return;
        }

        Pass("TcpBuildPacketWithChecksum");
    }

    private static void TestTcpParse()
    {
        // Build a TCP packet and parse it
        byte* buffer = stackalloc byte[64];
        byte* payload = stackalloc byte[5];
        payload[0] = (byte)'H'; payload[1] = (byte)'E'; payload[2] = (byte)'L'; payload[3] = (byte)'L'; payload[4] = (byte)'O';

        uint srcIP = ARP.MakeIP(10, 0, 2, 15);
        uint destIP = ARP.MakeIP(10, 0, 2, 2);

        int len = TCP.BuildPacketWithChecksum(buffer, 8080, 80, 0xDEADBEEF, 0xCAFEBABE,
                                               TcpFlags.PSH | TcpFlags.ACK, 16384,
                                               payload, 5, srcIP, destIP);

        TcpPacket packet;
        bool ok = TCP.Parse(buffer, len, out packet);

        if (!ok)
        {
            Fail("TcpParse", "parse failed");
            return;
        }

        if (packet.SourcePort != 8080)
        {
            Fail("TcpParse", "wrong source port");
            return;
        }

        if (packet.DestPort != 80)
        {
            Fail("TcpParse", "wrong dest port");
            return;
        }

        if (packet.SeqNum != 0xDEADBEEF)
        {
            Fail("TcpParse", "wrong seq number");
            return;
        }

        if (packet.AckNum != 0xCAFEBABE)
        {
            Fail("TcpParse", "wrong ack number");
            return;
        }

        if (packet.HeaderLength != 20)
        {
            Fail("TcpParse", "wrong header length");
            return;
        }

        if (packet.Flags != (TcpFlags.PSH | TcpFlags.ACK))
        {
            Fail("TcpParse", "wrong flags");
            return;
        }

        if (packet.Window != 16384)
        {
            Fail("TcpParse", "wrong window");
            return;
        }

        if (packet.PayloadLength != 5)
        {
            Fail("TcpParse", "wrong payload length");
            return;
        }

        // Verify payload content
        if (packet.Payload[0] != (byte)'H' || packet.Payload[1] != (byte)'E' ||
            packet.Payload[2] != (byte)'L' || packet.Payload[3] != (byte)'L' || packet.Payload[4] != (byte)'O')
        {
            Fail("TcpParse", "payload mismatch");
            return;
        }

        Pass("TcpParse");
    }

    private static void TestTcpChecksum()
    {
        byte* buffer = stackalloc byte[64];
        byte* payload = stackalloc byte[8];
        for (int i = 0; i < 8; i++) payload[i] = (byte)(i + 1);

        uint srcIP = ARP.MakeIP(192, 168, 1, 100);
        uint destIP = ARP.MakeIP(192, 168, 1, 1);

        int len = TCP.BuildPacketWithChecksum(buffer, 54321, 12345, 0x99999999, 0x88888888,
                                               TcpFlags.ACK, 32768, payload, 8, srcIP, destIP);

        // Verify checksum is valid
        if (!TCP.VerifyChecksum(buffer, len, srcIP, destIP))
        {
            Fail("TcpChecksum", "checksum verification failed");
            return;
        }

        // Corrupt the packet and verify checksum fails
        buffer[22] ^= 0xFF;
        if (TCP.VerifyChecksum(buffer, len, srcIP, destIP))
        {
            Fail("TcpChecksum", "corrupted packet passed checksum");
            return;
        }

        Pass("TcpChecksum");
    }

    private static void TestTcpBuildSyn()
    {
        byte* buffer = stackalloc byte[64];

        uint srcIP = ARP.MakeIP(10, 0, 2, 15);
        uint destIP = ARP.MakeIP(10, 0, 2, 2);

        int len = TCP.BuildSyn(buffer, 49152, 80, 0x12345678, 8192, srcIP, destIP);

        if (len != 20) // 20 bytes for SYN (no options)
        {
            Fail("TcpBuildSyn", "wrong length");
            return;
        }

        // Flags should be SYN only
        if (buffer[13] != TcpFlags.SYN)
        {
            Fail("TcpBuildSyn", "wrong flags");
            return;
        }

        // ACK number should be 0
        if (buffer[8] != 0 || buffer[9] != 0 || buffer[10] != 0 || buffer[11] != 0)
        {
            Fail("TcpBuildSyn", "ack number not zero");
            return;
        }

        // Verify checksum
        if (!TCP.VerifyChecksum(buffer, len, srcIP, destIP))
        {
            Fail("TcpBuildSyn", "checksum invalid");
            return;
        }

        Pass("TcpBuildSyn");
    }

    private static void TestTcpBuildAck()
    {
        byte* buffer = stackalloc byte[64];

        uint srcIP = ARP.MakeIP(10, 0, 2, 15);
        uint destIP = ARP.MakeIP(10, 0, 2, 2);

        int len = TCP.BuildAck(buffer, 49152, 80, 0x12345679, 0xABCDEF01, 8192, srcIP, destIP);

        if (len != 20)
        {
            Fail("TcpBuildAck", "wrong length");
            return;
        }

        // Flags should be ACK only
        if (buffer[13] != TcpFlags.ACK)
        {
            Fail("TcpBuildAck", "wrong flags");
            return;
        }

        // Verify checksum
        if (!TCP.VerifyChecksum(buffer, len, srcIP, destIP))
        {
            Fail("TcpBuildAck", "checksum invalid");
            return;
        }

        Pass("TcpBuildAck");
    }

    private static void TestTcpBuildFinAck()
    {
        byte* buffer = stackalloc byte[64];

        uint srcIP = ARP.MakeIP(10, 0, 2, 15);
        uint destIP = ARP.MakeIP(10, 0, 2, 2);

        int len = TCP.BuildFinAck(buffer, 49152, 80, 0x12345700, 0xABCDEF02, 8192, srcIP, destIP);

        if (len != 20)
        {
            Fail("TcpBuildFinAck", "wrong length");
            return;
        }

        // Flags should be FIN|ACK
        if (buffer[13] != (TcpFlags.FIN | TcpFlags.ACK))
        {
            Fail("TcpBuildFinAck", "wrong flags");
            return;
        }

        // Verify checksum
        if (!TCP.VerifyChecksum(buffer, len, srcIP, destIP))
        {
            Fail("TcpBuildFinAck", "checksum invalid");
            return;
        }

        Pass("TcpBuildFinAck");
    }

    private static void TestTcpBuildData()
    {
        byte* buffer = stackalloc byte[128];
        byte* data = stackalloc byte[10];
        for (int i = 0; i < 10; i++) data[i] = (byte)(0x41 + i); // 'A', 'B', 'C', ...

        uint srcIP = ARP.MakeIP(10, 0, 2, 15);
        uint destIP = ARP.MakeIP(10, 0, 2, 2);

        int len = TCP.BuildData(buffer, 49152, 80, 0x12345800, 0xABCDEF10, 8192,
                                data, 10, srcIP, destIP);

        if (len != 30) // 20 header + 10 data
        {
            Fail("TcpBuildData", "wrong length");
            return;
        }

        // Flags should be PSH|ACK
        if (buffer[13] != (TcpFlags.PSH | TcpFlags.ACK))
        {
            Fail("TcpBuildData", "wrong flags");
            return;
        }

        // Verify payload
        bool payloadOk = true;
        for (int i = 0; i < 10; i++)
        {
            if (buffer[20 + i] != (byte)(0x41 + i))
            {
                payloadOk = false;
                break;
            }
        }
        if (!payloadOk)
        {
            Fail("TcpBuildData", "payload mismatch");
            return;
        }

        // Verify checksum
        if (!TCP.VerifyChecksum(buffer, len, srcIP, destIP))
        {
            Fail("TcpBuildData", "checksum invalid");
            return;
        }

        Pass("TcpBuildData");
    }
}
