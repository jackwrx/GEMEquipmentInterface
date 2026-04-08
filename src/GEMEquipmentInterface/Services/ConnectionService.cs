// Services/ConnectionService.cs
using GemEquipmentInterface.Core;
using GemEquipmentInterface.Equipment;
using GemEquipmentInterface.Transport;
using Serilog;

namespace GemEquipmentInterface.Services;

/// <summary>
/// S1F1/S1F2 — Are You There / On Line Data
/// S1F13/S1F14 — Establish Communications
/// S1F15/S1F16 — Request OFF-LINE
/// S1F17/S1F18 — Request ON-LINE
///
/// This is the first thing the Host sends after HSMS SELECT.
/// Equipment responds with its identity and capabilities.
/// </summary>
public class ConnectionService
{
    private readonly HsmsConnection  _conn;
    private readonly EquipmentModel  _equipment;

    public ConnectionService(HsmsConnection conn, EquipmentModel equipment)
    {
        _conn      = conn;
        _equipment = equipment;
    }

    // ── S1F1 → S1F2: Are You There ───────────────────────────────
    /// <summary>
    /// Host sends S1F1 (Are You There).
    /// Equipment replies S1F2 with model name and software revision.
    ///
    /// S1F2 body:
    ///   L[2]
    ///     A "ModelName"
    ///     A "SoftwareRevision"
    /// </summary>
    public void HandleAreYouThere(SecsMessage request)
    {
        Log.Information("S1F1 Are You There received");

        var reply = new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S1,
            Function    = GemConstants.F2,
            ReplyBit    = false,
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.L(
                SecsItem.A("GEM-EQUIPMENT-001"),  // MDLN — model name
                SecsItem.A("1.0.0")               // SOFTREV — software revision
            )
        };

        _conn.Send(reply);
        Log.Information("S1F2 On Line Data sent");
    }

    // ── S1F13 → S1F14: Establish Communications ───────────────────
    /// <summary>
    /// Host sends S1F13 to establish GEM communications.
    ///
    /// S1F14 body:
    ///   L[2]
    ///     B[1] COMMACK (0=accepted)
    ///     L[2]
    ///       A "ModelName"
    ///       A "SoftwareRevision"
    /// </summary>
    public void HandleEstablishComm(SecsMessage request)
    {
        Log.Information("S1F13 Establish Communications received");

        _equipment.GoOnline();

        var reply = new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S1,
            Function    = GemConstants.F14,
            ReplyBit    = false,
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.L(
                SecsItem.B(GemConstants.COMMACK_ACCEPTED),
                SecsItem.L(
                    SecsItem.A("GEM-EQUIPMENT-001"),
                    SecsItem.A("1.0.0")
                )
            )
        };

        _conn.Send(reply);
        Log.Information("S1F14 Establish Communications Ack sent — COMMACK=ACCEPTED");
    }

    // ── S1F15 → S1F16: Request OFF-LINE ──────────────────────────
    /// <summary>
    /// Host requests equipment go offline.
    ///
    /// S1F16 body:
    ///   B[1] OFLACK (0=OK)
    /// </summary>
    public void HandleRequestOffline(SecsMessage request)
    {
        Log.Information("S1F15 Request OFF-LINE received");

        _equipment.GoOffline();

        var reply = new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S1,
            Function    = GemConstants.F16,
            ReplyBit    = false,
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.B(GemConstants.OFLACK_OK)
        };

        _conn.Send(reply);
        Log.Information("S1F16 OFF-LINE Ack sent");
    }

    // ── S1F17 → S1F18: Request ON-LINE ───────────────────────────
    /// <summary>
    /// Host requests equipment go online.
    ///
    /// S1F18 body:
    ///   B[1] ONLACK (0=OK, 1=refused, 2=already online)
    /// </summary>
    public void HandleRequestOnline(SecsMessage request)
    {
        Log.Information("S1F17 Request ON-LINE received");

        bool success = _equipment.GoOnline();
        byte onlack  = success
            ? GemConstants.ONLACK_OK
            : GemConstants.ONLACK_ALREADY_ONLINE;

        var reply = new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S1,
            Function    = GemConstants.F18,
            ReplyBit    = false,
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.B(onlack)
        };

        _conn.Send(reply);
        Log.Information("S1F18 ON-LINE Ack sent — ONLACK={Ack}", onlack);
    }
}