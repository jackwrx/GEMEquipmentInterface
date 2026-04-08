// Core/SecsItem.cs
namespace GemEquipmentInterface.Core;

/// <summary>
/// SEMI E5 SECS-II data item format codes.
/// Every SECS message is built from these typed items.
/// </summary>
public enum SecsFormat : byte
{
    List    = 0x00,
    Binary  = 0x08,
    Boolean = 0x09,
    Ascii   = 0x10,
    I1      = 0x61,
    I2      = 0x62,
    I4      = 0x64,
    F4      = 0x74,
    F8      = 0x70,
    U1      = 0xA4,
    U2      = 0xA8,
    U4      = 0xB0,
}

/// <summary>
/// Represents a SECS-II data item.
/// Items are the atomic data units in every GEM message.
///
/// Wire format:
///   [Format byte + LengthBytes(2 bits)] [Length (1-3 bytes)] [Data bytes]
///
/// Example — ASCII "OK":
///   0x41 0x02 0x4F 0x4B
///   ↑         ↑
///   A(2)      "OK"
/// </summary>
public class SecsItem
{
    public SecsFormat     Format   { get; }
    public List<SecsItem> Items    { get; } = [];
    public byte[]         RawData  { get; } = [];

    // ── Constructors ─────────────────────────────────────────────

    // ✅ Changed from private → internal so subclasses and
    //    factory methods within the assembly can use it freely
    internal SecsItem(SecsFormat format, byte[] data)
    {
        Format  = format;
        RawData = data;
    }

    private SecsItem(List<SecsItem> items)
    {
        Format = SecsFormat.List;
        Items  = items;
    }

    // ── Factory methods ───────────────────────────────────────────

    /// <summary>L — ordered list of items.</summary>
    public static SecsItem L(params SecsItem[] items)
        => new(items.ToList());

    public static SecsItem L(List<SecsItem> items)
        => new(items);

    /// <summary>A — ASCII string.</summary>
    public static SecsItem A(string value)
        => new(SecsFormat.Ascii,
               System.Text.Encoding.ASCII.GetBytes(value ?? ""));

    /// <summary>B — binary byte array.</summary>
    public static SecsItem B(params byte[] values)
        => new(SecsFormat.Binary, values);

    /// <summary>U1 — unsigned 8-bit integer.</summary>
    public static SecsItem U1(byte value)
        => new(SecsFormat.U1, [value]);

    /// <summary>U2 — unsigned 16-bit integer (big-endian).</summary>
    public static SecsItem U2(ushort value)
        => new(SecsFormat.U2, [
            (byte)(value >> 8),
            (byte)(value & 0xFF)]);

    /// <summary>U4 — unsigned 32-bit integer (big-endian).</summary>
    public static SecsItem U4(uint value)
        => new(SecsFormat.U4, [
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)(value & 0xFF)]);

    /// <summary>
    /// I4 — signed 32-bit integer (big-endian).
    /// ✅ Fixed: encodes directly instead of using 'with {}' on U4.
    /// Cast int → uint preserves bit pattern (two's complement).
    /// </summary>
    public static SecsItem I4(int value)
    {
        uint u = (uint)value;
        return new SecsItem(SecsFormat.I4, [
            (byte)(u >> 24),
            (byte)(u >> 16),
            (byte)(u >> 8),
            (byte)(u & 0xFF)
        ]);
    }

    /// <summary>I1 — signed 8-bit integer.</summary>
    public static SecsItem I1(sbyte value)
        => new(SecsFormat.I1, [(byte)value]);

    /// <summary>I2 — signed 16-bit integer (big-endian).</summary>
    public static SecsItem I2(short value)
    {
        ushort u = (ushort)value;
        return new SecsItem(SecsFormat.I2, [
            (byte)(u >> 8),
            (byte)(u & 0xFF)]);
    }

    /// <summary>F4 — 32-bit float (big-endian).</summary>
    public static SecsItem F4(float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return new SecsItem(SecsFormat.F4, bytes);
    }

    /// <summary>F8 — 64-bit double (big-endian).</summary>
    public static SecsItem F8(double value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return new SecsItem(SecsFormat.F8, bytes);
    }

    /// <summary>Bo — boolean.</summary>
    public static SecsItem Bo(bool value)
        => new(SecsFormat.Boolean, [value ? (byte)1 : (byte)0]);

    // ── Value extractors ──────────────────────────────────────────

    public string  GetAscii() =>
        System.Text.Encoding.ASCII.GetString(RawData);

    public byte    GetU1()    => RawData[0];

    public ushort  GetU2()    =>
        (ushort)((RawData[0] << 8) | RawData[1]);

    public uint    GetU4()    =>
        (uint)((RawData[0] << 24) | (RawData[1] << 16)
             | (RawData[2] << 8)  |  RawData[3]);

    public int     GetI4()
    {
        // ✅ Read raw bytes as signed int — preserves two's complement
        uint u = (uint)((RawData[0] << 24) | (RawData[1] << 16)
                       | (RawData[2] << 8)  |  RawData[3]);
        return (int)u;
    }

    public short   GetI2()
    {
        ushort u = (ushort)((RawData[0] << 8) | RawData[1]);
        return (short)u;
    }

    public sbyte   GetI1()    => (sbyte)RawData[0];

    public float   GetF4()
    {
        var bytes = RawData.ToArray();
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes);
    }

    public double  GetF8()
    {
        var bytes = RawData.ToArray();
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToDouble(bytes);
    }

    public bool    GetBool()  => RawData[0] != 0;

    // ── Serialization to SECS-II wire format ──────────────────────

    public byte[] Encode()
    {
        using var ms = new System.IO.MemoryStream();
        EncodeInto(ms);
        return ms.ToArray();
    }

    private void EncodeInto(System.IO.Stream stream)
    {
        if (Format == SecsFormat.List)
        {
            WriteHeader(stream, 0x01, Items.Count);
            foreach (var item in Items)
                item.EncodeInto(stream);
        }
        else
        {
            // Format byte bits[7:2] = format code, bits[1:0] = length byte count
            byte formatCode = (byte)((byte)Format | 0x01);
            WriteHeader(stream, formatCode, RawData.Length);
            stream.Write(RawData, 0, RawData.Length);
        }
    }

    private static void WriteHeader(
        System.IO.Stream stream, byte formatByte, int length)
    {
        if (length <= 0xFF)
        {
            stream.WriteByte((byte)(formatByte | 0x01));
            stream.WriteByte((byte)length);
        }
        else if (length <= 0xFFFF)
        {
            stream.WriteByte((byte)(formatByte | 0x02));
            stream.WriteByte((byte)(length >> 8));
            stream.WriteByte((byte)(length & 0xFF));
        }
        else
        {
            stream.WriteByte((byte)(formatByte | 0x03));
            stream.WriteByte((byte)(length >> 16));
            stream.WriteByte((byte)(length >> 8));
            stream.WriteByte((byte)(length & 0xFF));
        }
    }

    // ── Deserialization from wire bytes ───────────────────────────

    public static SecsItem Decode(byte[] data)
    {
        int pos = 0;
        return DecodeFrom(data, ref pos);
    }

    private static SecsItem DecodeFrom(byte[] data, ref int pos)
    {
        byte fb         = data[pos++];
        byte formatCode = (byte)(fb & 0xFC);
        int  lenBytes   = fb & 0x03;
        int  length     = 0;

        for (int i = 0; i < lenBytes; i++)
            length = (length << 8) | data[pos++];

        // List type
        if (formatCode == 0x00)
        {
            var items = new List<SecsItem>();
            for (int i = 0; i < length; i++)
                items.Add(DecodeFrom(data, ref pos));
            return L(items);
        }

        // Scalar type
        var raw = new byte[length];
        Array.Copy(data, pos, raw, 0, length);
        pos += length;

        // ✅ Map format code back to SecsFormat enum
        var format = MapFormatCode(formatCode);
        return new SecsItem(format, raw);
    }

    private static SecsFormat MapFormatCode(byte code) => code switch
    {
        0x08 => SecsFormat.Binary,
        0x09 => SecsFormat.Boolean,
        0x10 => SecsFormat.Ascii,
        0x61 => SecsFormat.I1,
        0x62 => SecsFormat.I2,
        0x64 => SecsFormat.I4,
        0x70 => SecsFormat.F8,
        0x74 => SecsFormat.F4,
        0xA4 => SecsFormat.U1,
        0xA8 => SecsFormat.U2,
        0xB0 => SecsFormat.U4,
        _    => SecsFormat.Binary   // unknown — treat as raw bytes
    };

    // ── Debug string ──────────────────────────────────────────────

    public override string ToString() => Format switch
    {
        SecsFormat.List    => $"L[{Items.Count}]({string.Join(", ", Items)})",
        SecsFormat.Ascii   => $"A'{GetAscii()}'",
        SecsFormat.U4      => $"U4({GetU4()})",
        SecsFormat.U2      => $"U2({GetU2()})",
        SecsFormat.U1      => $"U1({GetU1()})",
        SecsFormat.I4      => $"I4({GetI4()})",
        SecsFormat.I2      => $"I2({GetI2()})",
        SecsFormat.I1      => $"I1({GetI1()})",
        SecsFormat.Boolean => $"Bo({GetBool()})",
        SecsFormat.Binary  => $"B[{RawData.Length}]",
        SecsFormat.F4      => $"F4({GetF4()})",
        SecsFormat.F8      => $"F8({GetF8()})",
        _                  => $"{Format}[{RawData.Length}]"
    };
}