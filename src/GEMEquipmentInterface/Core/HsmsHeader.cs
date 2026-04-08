// Core/HsmsHeader.cs
namespace GemEquipmentInterface.Core;

/// <summary>
/// SEMI E37 HSMS 10-byte message header.
///
/// Every HSMS message — whether a SECS-II data message or
/// a session control message — starts with this fixed 10-byte header.
///
/// Wire layout (10 bytes total):
/// ┌─────────┬─────────┬─────────┬─────────┬─────────┬─────────┬─────────┬─────────┬─────────┬─────────┐
/// │ Byte 0  │ Byte 1  │ Byte 2  │ Byte 3  │ Byte 4  │ Byte 5  │ Byte 6  │ Byte 7  │ Byte 8  │ Byte 9  │
/// ├─────────┴─────────┼─────────┼─────────┼─────────┼─────────┼─────────┴─────────┴─────────┴─────────┤
/// │   Session ID      │ Stream  │Function │  PType  │  SType  │           System Bytes                 │
/// │   (Device ID)     │+R-bit   │         │         │         │         (unique message ID)             │
/// └───────────────────┴─────────┴─────────┴─────────┴─────────┴─────────────────────────────────────────┘
///
/// Field breakdown:
///
///   Session ID (2 bytes):
///     Identifies the device/equipment. Typically 0 or 1 for single equipment.
///     Must match between host and equipment after SELECT.
///
///   Stream (1 byte):
///     Bit 7 (R-bit): 1 = reply expected (W-bit in SECS-II terminology)
///     Bits 6-0: Stream number (1-63 for data, 0 for control)
///
///   Function (1 byte):
///     Function number within the stream.
///     Odd  = Primary message (request, host→equipment or equipment→host)
///     Even = Secondary message (reply)
///     0    = Used for HSMS control messages
///
///   PType (1 byte):
///     0x00 = SECS-II encoded data message
///     0x01-0xFF = Subsidiary standards (not commonly used)
///
///   SType (1 byte):
///     Distinguishes data messages from HSMS session control messages:
///     0x00 = Data message (SECS-II content in body)
///     0x01 = Select.req
///     0x02 = Select.rsp
///     0x03 = Deselect.req
///     0x04 = Deselect.rsp
///     0x05 = Linktest.req
///     0x06 = Linktest.rsp
///     0x07 = Reject.req
///     0x09 = Separate.req
///
///   System Bytes (4 bytes):
///     Unique ID for this message transaction.
///     The reply must echo the same system bytes as the request.
///     Equipment and host use separate ranges to avoid collisions.
///     Big-endian unsigned 32-bit integer.
/// </summary>
public class HsmsHeader
{
    // ── Properties ────────────────────────────────────────────────

    /// <summary>
    /// Session ID / Device ID (bytes 0-1, big-endian).
    /// Identifies which equipment this message is for.
    /// </summary>
    public ushort SessionId    { get; init; }

    /// <summary>
    /// SECS-II Stream number (byte 2, bits 6-0).
    /// 0 = HSMS control message, 1-63 = data streams.
    /// </summary>
    public byte   Stream       { get; init; }

    /// <summary>
    /// SECS-II Function number (byte 3).
    /// Odd = primary/request, Even = secondary/reply.
    /// </summary>
    public byte   Function     { get; init; }

    /// <summary>
    /// R-bit (Reply bit) from byte 2, bit 7.
    /// True = sender expects a reply message.
    /// Also called W-bit (Wait bit) in some SEMI documents.
    /// </summary>
    public bool   ReplyBit     { get; init; }

    /// <summary>
    /// Presentation Type (byte 4).
    /// 0x00 = SECS-II message encoding (standard).
    /// </summary>
    public byte   PType        { get; init; }

    /// <summary>
    /// Session Type (byte 5).
    /// 0x00 = data message, 0x01-0x09 = HSMS control messages.
    /// See GemConstants.STYPE_* for all values.
    /// </summary>
    public byte   SType        { get; init; }

    /// <summary>
    /// System Bytes (bytes 6-9, big-endian uint32).
    /// Unique transaction identifier.
    /// Reply must echo the same value as the original request.
    /// </summary>
    public uint   SystemBytes  { get; init; }

    // ── Derived helpers ───────────────────────────────────────────

    /// <summary>
    /// True if this is an HSMS session control message (SType != 0).
    /// False if this is a SECS-II data message (SType == 0).
    /// </summary>
    public bool IsControlMessage  => SType != GemConstants.STYPE_DATA;

    /// <summary>
    /// True if this is a SECS-II data message (SType == 0).
    /// </summary>
    public bool IsDataMessage     => SType == GemConstants.STYPE_DATA;

    /// <summary>
    /// Friendly message ID string e.g. "S1F2", "S6F11".
    /// Returns "Control" for HSMS session messages.
    /// </summary>
    public string MessageId =>
        IsDataMessage ? $"S{Stream}F{Function}" : $"Control(SType={SType})";

    // ══════════════════════════════════════════════════════════════
    // ENCODE — serialize header to 10 wire bytes
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Encode the header to exactly 10 bytes in HSMS wire format.
    /// Call this before prepending the 4-byte message length prefix.
    /// </summary>
    public byte[] Encode()
    {
        var buf = new byte[10];
        EncodeTo(buf, 0);
        return buf;
    }

    /// <summary>
    /// Encode header bytes into an existing buffer at <paramref name="offset"/>.
    /// Useful when building the complete message (length + header + body) at once.
    /// </summary>
    public void EncodeTo(byte[] buf, int offset)
    {
        // ── Bytes 0-1: Session ID (big-endian) ───────────────────
        buf[offset + 0] = (byte)(SessionId >> 8);
        buf[offset + 1] = (byte)(SessionId & 0xFF);

        // ── Byte 2: Stream + R-bit ────────────────────────────────
        // Bit 7 = R-bit (reply expected)
        // Bits 6-0 = stream number
        buf[offset + 2] = (byte)(Stream | (ReplyBit ? 0x80 : 0x00));

        // ── Byte 3: Function ──────────────────────────────────────
        buf[offset + 3] = Function;

        // ── Byte 4: PType ─────────────────────────────────────────
        buf[offset + 4] = PType;

        // ── Byte 5: SType ─────────────────────────────────────────
        buf[offset + 5] = SType;

        // ── Bytes 6-9: System Bytes (big-endian) ──────────────────
        buf[offset + 6] = (byte)(SystemBytes >> 24);
        buf[offset + 7] = (byte)(SystemBytes >> 16);
        buf[offset + 8] = (byte)(SystemBytes >> 8);
        buf[offset + 9] = (byte)(SystemBytes & 0xFF);
    }

    // ══════════════════════════════════════════════════════════════
    // DECODE — deserialize header from 10 wire bytes
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Decode an HSMS header from exactly 10 bytes.
    /// The buffer should NOT include the 4-byte length prefix.
    /// </summary>
    public static HsmsHeader Decode(byte[] buf)
        => DecodeFrom(buf, 0);

    /// <summary>
    /// Decode an HSMS header from a buffer starting at <paramref name="offset"/>.
    /// Use this when the buffer includes the 4-byte length prefix (offset = 4).
    /// </summary>
    public static HsmsHeader DecodeFrom(byte[] buf, int offset)
    {
        // ── Bytes 0-1: Session ID ─────────────────────────────────
        ushort sessionId = (ushort)((buf[offset + 0] << 8)
                                  |  buf[offset + 1]);

        // ── Byte 2: Stream + R-bit ────────────────────────────────
        byte   streamByte = buf[offset + 2];
        bool   replyBit   = (streamByte & 0x80) != 0;
        byte   stream     = (byte)(streamByte & 0x7F);   // mask off R-bit

        // ── Byte 3: Function ──────────────────────────────────────
        byte function = buf[offset + 3];

        // ── Byte 4: PType ─────────────────────────────────────────
        byte pType = buf[offset + 4];

        // ── Byte 5: SType ─────────────────────────────────────────
        byte sType = buf[offset + 5];

        // ── Bytes 6-9: System Bytes ───────────────────────────────
        uint systemBytes = (uint)((buf[offset + 6] << 24)
                                | (buf[offset + 7] << 16)
                                | (buf[offset + 8] << 8)
                                |  buf[offset + 9]);

        return new HsmsHeader
        {
            SessionId   = sessionId,
            Stream      = stream,
            Function    = function,
            ReplyBit    = replyBit,
            PType       = pType,
            SType       = sType,
            SystemBytes = systemBytes,
        };
    }

    // ══════════════════════════════════════════════════════════════
    // FACTORY METHODS — build common header types
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a header for a SECS-II data message (SType = 0).
    /// </summary>
    public static HsmsHeader DataMessage(
        ushort sessionId,
        byte   stream,
        byte   function,
        bool   replyBit,
        uint   systemBytes) => new()
    {
        SessionId   = sessionId,
        Stream      = stream,
        Function    = function,
        ReplyBit    = replyBit,
        PType       = 0,
        SType       = GemConstants.STYPE_DATA,
        SystemBytes = systemBytes,
    };

    /// <summary>
    /// Create a header for an HSMS session control message.
    /// Stream = 0, Function = 0, PType = 0 for all control messages.
    /// </summary>
    public static HsmsHeader ControlMessage(
        ushort sessionId,
        byte   sType,
        uint   systemBytes) => new()
    {
        SessionId   = sessionId,
        Stream      = 0,
        Function    = 0,
        ReplyBit    = false,
        PType       = 0,
        SType       = sType,
        SystemBytes = systemBytes,
    };

    /// <summary>
    /// Create a Select.req header — sent by Host to activate the session.
    /// </summary>
    public static HsmsHeader SelectReq(ushort sessionId, uint systemBytes)
        => ControlMessage(sessionId, GemConstants.STYPE_SELECT_REQ, systemBytes);

    /// <summary>
    /// Create a Select.rsp header — sent by Equipment in reply to Select.req.
    /// </summary>
    public static HsmsHeader SelectRsp(ushort sessionId, uint systemBytes)
        => ControlMessage(sessionId, GemConstants.STYPE_SELECT_RSP, systemBytes);

    /// <summary>
    /// Create a Linktest.req header — periodic heartbeat from Host.
    /// </summary>
    public static HsmsHeader LinktestReq(ushort sessionId, uint systemBytes)
        => ControlMessage(sessionId, GemConstants.STYPE_LINKTEST_REQ, systemBytes);

    /// <summary>
    /// Create a Linktest.rsp header — Equipment replies to heartbeat.
    /// </summary>
    public static HsmsHeader LinktestRsp(ushort sessionId, uint systemBytes)
        => ControlMessage(sessionId, GemConstants.STYPE_LINKTEST_RSP, systemBytes);

    /// <summary>
    /// Create a Separate.req header — Host closes the session cleanly.
    /// </summary>
    public static HsmsHeader SeparateReq(ushort sessionId, uint systemBytes)
        => ControlMessage(sessionId, GemConstants.STYPE_SEPARATE_REQ, systemBytes);

    // ══════════════════════════════════════════════════════════════
    // VALIDATION
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Validate the header fields are within SEMI E37 allowed ranges.
    /// Returns a list of validation errors (empty = valid).
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Stream > 63)
            errors.Add($"Stream {Stream} out of range (0-63).");

        if (IsDataMessage && Stream == 0 && Function != 0)
            errors.Add("Stream 0 data messages must have Function 0.");

        if (PType != 0)
            errors.Add($"PType {PType} is non-standard. Expected 0x00.");

        if (SType > 9 || SType == 8)
            errors.Add($"SType {SType} is not a valid HSMS session type.");

        if (IsControlMessage && (Stream != 0 || Function != 0))
            errors.Add("Control messages must have Stream=0 and Function=0.");

        return errors;
    }

    // ══════════════════════════════════════════════════════════════
    // DEBUG
    // ══════════════════════════════════════════════════════════════

    public override string ToString() =>
        IsControlMessage
            ? $"[CTRL SType={SType} SessionId={SessionId} SysBytes={SystemBytes:X8}]"
            : $"[S{Stream}F{Function}{(ReplyBit ? "W" : "")} " +
              $"SessionId={SessionId} SysBytes={SystemBytes:X8}]";
}