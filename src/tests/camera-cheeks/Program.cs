using System.Buffers.Binary;
using Qpro.GazeBridge;

int checks = 0;
try
{
    var camera = new CheekCameraReceiver();
    CheekPuffWeights native = new(0.2f, 0.3f);
    Check(camera.Resolve(0, false, native) is null, "no camera packet leaves unavailable source unchanged");
    Check(camera.Resolve(0, true, native) == native, "native values work without camera tracking");
    Check(camera.Receive(Packet(true, 0.5f, 0.1f), 100), "valid camera packet parses");
    Check(camera.Resolve(100, true, native) == new CheekPuffWeights(0.5f, 0.1f), "camera overrides native while fresh");
    Check(camera.Resolve(600, false, native) == new CheekPuffWeights(0.5f, 0.1f), "camera stays independent of native face validity");
    Check(camera.Resolve(601, true, native) == native, "expired camera restores native cheek style");
    Check(camera.Receive(Packet(true, 0.9f, 0.7f), 1000), "second packet parses");
    camera.Resolve(1000, false, native);
    Check(camera.Resolve(1501, false, native) == new CheekPuffWeights(0, 0), "expired camera clears its output when native is unavailable");
    Check(camera.Resolve(1502, false, native) is null, "unavailable source is cleared only once");
    Check(camera.Receive(Packet(true, 0.9f, 0.7f), 2000), "active packet before stop");
    camera.Resolve(2000, true, native);
    Check(camera.Receive(Packet(false, 0, 0), 2010), "disabled stop packet parses");
    Check(camera.Resolve(2010, true, native) == native, "stop immediately restores native without waiting for timeout");
    Check(camera.Receive(Packet(true, 0.3f, 0.4f), 3000), "active packet before module suspension");
    camera.Resolve(3000, false, native);
    camera.DiscardPackets();
    Check(camera.Resolve(3001, false, native) == new CheekPuffWeights(0, 0), "suspended module cannot reactivate stale camera data");
    foreach (byte[] invalid in new[] { Packet(true, float.NaN, 0), Packet(true, 0, float.PositiveInfinity),
                 Packet(true, -0.01f, 0), Packet(true, 0, 1.01f), new byte[2] })
        Check(!camera.Receive(invalid, 4000), "malformed camera data rejected");
    byte[] wrongFlags = Packet(true, 0, 0);
    wrongFlags[5] = 3;
    Check(!camera.Receive(wrongFlags, 4000), "unknown enable flags rejected");
    byte[] reserved = Packet(true, 0, 0);
    reserved[7] = 1;
    Check(!camera.Receive(reserved, 4000), "nonzero reserved field rejected");
    byte[] wrongMagic = Packet(true, 0, 0);
    wrongMagic[3] = (byte)'1';
    Check(!camera.Receive(wrongMagic, 4000), "native calibration packet is not a camera override");
    camera.Reset();
    Check(camera.Resolve(5000, false, native) is null, "module teardown clears camera state");
    Console.WriteLine($"Camera cheek packet and recovery checks passed: {checks}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Camera cheek check failed: {error}");
    return 1;
}

void Check(bool valid, string description)
{
    if (!valid) throw new InvalidOperationException(description);
    checks++;
}

static byte[] Packet(bool enabled, float left, float right)
{
    byte[] packet = new byte[CheekCameraReceiver.PacketBytes];
    packet[0] = (byte)'Q'; packet[1] = (byte)'P'; packet[2] = (byte)'C'; packet[3] = (byte)'O';
    packet[4] = 1; packet[5] = enabled ? (byte)1 : (byte)0;
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), BitConverter.SingleToInt32Bits(left));
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12), BitConverter.SingleToInt32Bits(right));
    return packet;
}
