// Services/StatusService.cs
using GemEquipmentInterface.Core;
using GemEquipmentInterface.Equipment;
using GemEquipmentInterface.Transport;
using Serilog;

namespace GemEquipmentInterface.Services;

/// <summary>
/// S1F3/S1F4 — Selected Equipment Status Request / Data
///
/// Host sends S1F3 with a list of SVIDs it wants.
/// Equipment replies S1F4 with the current values.
///
/// S1F3 body:
///   L[N] — list of SVIDs (U4)
///
/// S1F4 body:
///   L[N] — list of values matching the requested SVIDs
///
/// If SVID list is empty, return all status variables.
/// </summary>
public class StatusService
{
    private readonly HsmsConnection _conn;
    private readonly EquipmentModel _equipment;

    // SVID definitions — name + unit
    private static readonly Dictionary<uint, (string Name, string Unit)> _svidDefs = new()
    {
        [EquipmentConstants.SVID_CLOCK]             = ("Clock",           ""),
        [EquipmentConstants.SVID_CONTROL_STATE]     = ("ControlState",    ""),
        [EquipmentConstants.SVID_PROCESS_STATE]     = ("ProcessState",    ""),
        [EquipmentConstants.SVID_RECIPE_ID]         = ("RecipeId",        ""),
        [EquipmentConstants.SVID_LOT_ID]            = ("LotId",           ""),
        [EquipmentConstants.SVID_WAFER_COUNT]       = ("WaferCount",      "wafers"),
        [EquipmentConstants.SVID_CHAMBER_TEMP]      = ("ChamberTemp",     "°C x10"),
        [EquipmentConstants.SVID_CHAMBER_PRESSURE]  = ("ChamberPressure", "mTorr"),
        [EquipmentConstants.SVID_UPTIME_SECONDS]    = ("UptimeSeconds",   "sec"),
    };

    public StatusService(HsmsConnection conn, EquipmentModel equipment)
    {
        _conn      = conn;
        _equipment = equipment;
    }

    // ── S1F3 → S1F4 ───────────────────────────────────────────────
    public void HandleStatusRequest(SecsMessage request)
    {
        // Parse requested SVIDs from S1F3 body
        var requestedSvids = new List<uint>();

        if (request.Body is { Format: SecsFormat.List })
        {
            foreach (var item in request.Body.Items)
                requestedSvids.Add(item.GetU4());
        }

        // Empty list → return all SVIDs
        if (requestedSvids.Count == 0)
            requestedSvids = _svidDefs.Keys.ToList();

        Log.Information("S1F3 Status Request — SVIDs: {Svids}",
            string.Join(",", requestedSvids));

        // Build S1F4 response
        var valueItems = new List<SecsItem>();
        foreach (var svid in requestedSvids)
        {
            var value = _equipment.GetStatusVariable(svid);
            valueItems.Add(BuildSecsValue(value));
        }

        var reply = new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S1,
            Function    = GemConstants.F4,
            ReplyBit    = false,
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.L(valueItems)
        };

        _conn.Send(reply);
        Log.Information("S1F4 Status Data sent — {Count} values", valueItems.Count);
    }

    // ── S1F11 → S1F12: Status Variable Namelist ───────────────────
    /// <summary>
    /// Host requests the list of all SVIDs with names and units.
    /// Used during initial capability discovery.
    ///
    /// S1F12 body:
    ///   L[N]
    ///     L[3]
    ///       U4  SVID
    ///       A   SVNAME
    ///       A   UNITS
    /// </summary>
    public void HandleStatusNamelist(SecsMessage request)
    {
        Log.Information("S1F11 Status Variable Namelist Request");

        var entries = _svidDefs.Select(kv =>
            SecsItem.L(
                SecsItem.U4(kv.Key),
                SecsItem.A(kv.Value.Name),
                SecsItem.A(kv.Value.Unit)
            )
        ).ToList();

        var reply = new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S1,
            Function    = GemConstants.F12,
            ReplyBit    = false,
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.L(entries)
        };

        _conn.Send(reply);
        Log.Information("S1F12 Status Namelist sent — {Count} SVIDs", entries.Count);
    }

    // ── Helper: convert .NET value to SecsItem ────────────────────
    private static SecsItem BuildSecsValue(object? value) => value switch
    {
        string  s   => SecsItem.A(s),
        int     i   => SecsItem.I4(i),
        uint    u   => SecsItem.U4(u),
        bool    b   => SecsItem.Bo(b),
        null        => SecsItem.A(""),
        _           => SecsItem.A(value.ToString() ?? "")
    };
}