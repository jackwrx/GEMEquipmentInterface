// Core/SecsMessage.cs
namespace GemEquipmentInterface.Core;

/// <summary>
/// A complete HSMS/SECS-II message.
///
/// Wire format (total = 4 + 10 + N bytes):
///   [4 bytes : message length]
///   [10 bytes: HSMS header   ]
///   [N bytes : SECS-II body  ]  ← optional
///
/// The 4-byte length prefix contains the byte count of
/// everything AFTER it (header + body), NOT including itself.
///
/// Example — S1F1 Are You There (no body):
///   00 00 00 0A          ← length = 10 (header only)
///   00 01                ← Session ID = 1
///   81                   ← Stream = 1, R-bit = 1
///   01                   ← Function = 1
///   00                   ← PType = 0
///   00                   ← SType = 0 (data message)
///   00 00 00 01          ← System Bytes = 1
/// </summary>
public class SecsMessage
{
    // ── Fields ────────────────────────────────────────────────────
    public ushort    DeviceId    { get; init; }
    public byte      Stream      { get; init; }
    public byte      Function    { get; init; }
    public bool      ReplyBit    { get; init; }
    public byte      PType       { get; init; }
    public byte      SType       { get; init; }
    public uint      SystemBytes { get; init; }
    public SecsItem? Body        { get; init; }

    // ── Derived properties ────────────────────────────────────────

    /// <summary>
    /// Build an HsmsHeader from this message's fields.
    /// Used for encoding and validation.
    /// ✅ Uppercase property — accessible from any method including ToString()
    /// </summary>
    public HsmsHeader Header => SType == GemConstants.STYPE_DATA
        ? HsmsHeader.DataMessage(DeviceId, Stream, Function, ReplyBit, SystemBytes)
        : HsmsHeader.ControlMessage(DeviceId, SType, SystemBytes);

    /// <summary>Friendly message name e.g. "S1F1", "S6F11".</summary>
    public string MessageId => $"S{Stream}F{Function}";

    /// <summary>True if this is an HSMS session control message.</summary>
    public bool IsControl   => SType != GemConstants.STYPE_DATA;

    // ══════════════════════════════════════════════════════════════
    // ENCODE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Serialize this message to HSMS wire bytes.
    ///
    /// Layout:
    ///   Bytes 0-3  : message length (big-endian uint32)
    ///   Bytes 4-13 : HSMS header (10 bytes)
    ///   Bytes 14+  : SECS-II body (variable, may be empty)
    /// </summary>
    public byte[] Encode()
    {
        var body   = Body?.Encode() ?? [];
        int total  = 10 + body.Length;      // header + body
        var buffer = new byte[4 + total];   // length prefix + header + body

        // ── Bytes 0-3: message length (big-endian) ────────────────
        buffer[0] = (byte)(total >> 24);
        buffer[1] = (byte)(total >> 16);
        buffer[2] = (byte)(total >> 8);
        buffer[3] = (byte)(total & 0xFF);

        // ── Bytes 4-13: HSMS header ───────────────────────────────
        Header.EncodeTo(buffer, offset: 4);

        // ── Bytes 14+: SECS-II body ───────────────────────────────
        if (body.Length > 0)
            Array.Copy(body, 0, buffer, 14, body.Length);

        return buffer;
    }

    // ══════════════════════════════════════════════════════════════
    // DECODE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Deserialize a full HSMS message from wire bytes.
    /// Buffer must include the 4-byte length prefix.
    /// </summary>
    public static SecsMessage Decode(byte[] buffer)
    {
        if (buffer.Length < 14)
            throw new InvalidDataException(
                $"Buffer too short: {buffer.Length} bytes. Minimum is 14 (4 length + 10 header).");

        // ── Decode the 10-byte header (skip 4-byte length prefix) ─
        var header = HsmsHeader.DecodeFrom(buffer, offset: 4);

        // ── Validate header fields ────────────────────────────────
        var errors = header.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException(
                $"Invalid HSMS header: {string.Join("; ", errors)}");

        // ── Decode optional SECS-II body ──────────────────────────
        SecsItem? body = null;
        if (buffer.Length > 14)
        {
            var bodyBytes = new byte[buffer.Length - 14];
            Array.Copy(buffer, 14, bodyBytes, 0, bodyBytes.Length);
            if (bodyBytes.Length > 0)
                body = SecsItem.Decode(bodyBytes);
        }

        return new SecsMessage
        {
            DeviceId    = header.SessionId,
            Stream      = header.Stream,
            Function    = header.Function,
            ReplyBit    = header.ReplyBit,
            PType       = header.PType,
            SType       = header.SType,
            SystemBytes = header.SystemBytes,
            Body        = body,
        };
    }

    // ══════════════════════════════════════════════════════════════
    // FACTORY METHODS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Create an HSMS session control message (no body).
    /// Used for Select, Deselect, Linktest, Separate.
    /// </summary>
    public static SecsMessage Control(
        ushort sessionId,
        byte   sType,
        uint   systemBytes) => new()
    {
        DeviceId    = sessionId,
        Stream      = 0,
        Function    = 0,
        ReplyBit    = false,
        PType       = 0,
        SType       = sType,
        SystemBytes = systemBytes,
    };

    /// <summary>
    /// Create a SECS-II data message reply.
    /// Echoes the SystemBytes from the request so the sender
    /// can match this reply to the original request.
    /// </summary>
    public static SecsMessage Reply(
        SecsMessage request,
        byte        function,
        SecsItem?   body = null) => new()
    {
        DeviceId    = request.DeviceId,
        Stream      = request.Stream,
        Function    = function,
        ReplyBit    = false,   // replies never set the R-bit
        PType       = 0,
        SType       = GemConstants.STYPE_DATA,
        SystemBytes = request.SystemBytes,  // echo back — required by SEMI E37
        Body        = body,
    };

    // ══════════════════════════════════════════════════════════════
    // DEBUG
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// ✅ Fixed: uses uppercase Header property, not lowercase 'header' local var.
    /// </summary>
    public override string ToString() =>
        $"{Header} Body={Body}";
}