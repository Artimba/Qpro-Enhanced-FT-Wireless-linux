using System.Buffers.Binary;

namespace Qpro.Shared;

// Only separated cheek strengths cross this loopback channel. The calibration
// window never needs camera frames or the rest of the face tracking stream.
internal static class CheekPuffTelemetry
{
    internal const int Port = 27278;
    internal const int PacketBytes = 16;
    internal const byte SourceVirtualDesktop = 0;
    internal const byte SourceSteamLink = 1;

    internal static byte[] Create(byte source, float left, float right)
    {
        if (source is not (SourceVirtualDesktop or SourceSteamLink))
            throw new ArgumentOutOfRangeException(nameof(source));
        byte[] packet = new byte[PacketBytes];
        packet[0] = (byte)'Q';
        packet[1] = (byte)'C';
        packet[2] = (byte)'P';
        packet[3] = (byte)'1';
        packet[4] = source;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8),
            BitConverter.SingleToInt32Bits(Clean(left)));
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12),
            BitConverter.SingleToInt32Bits(Clean(right)));
        return packet;
    }

    internal static bool TryParse(ReadOnlySpan<byte> packet, out byte source,
        out float left, out float right)
    {
        source = 0;
        left = 0;
        right = 0;
        if (packet.Length != PacketBytes ||
            packet[0] != (byte)'Q' || packet[1] != (byte)'C' ||
            packet[2] != (byte)'P' || packet[3] != (byte)'1' ||
            packet[4] is not (SourceVirtualDesktop or SourceSteamLink) ||
            packet[5] != 0 || packet[6] != 0 || packet[7] != 0)
            return false;

        float parsedLeft = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(8, 4)));
        float parsedRight = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(12, 4)));
        if (!float.IsFinite(parsedLeft) || !float.IsFinite(parsedRight) ||
            parsedLeft is < 0 or > 1 || parsedRight is < 0 or > 1)
            return false;
        source = packet[4];
        left = parsedLeft;
        right = parsedRight;
        return true;
    }

    private static float Clean(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0.0f, 1.0f) : 0.0f;
}
