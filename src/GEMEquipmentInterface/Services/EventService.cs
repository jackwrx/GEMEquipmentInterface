// Services/EventService.cs
using GemEquipmentInterface.Core;
using GemEquipmentInterface.Equipment;
using GemEquipmentInterface.Transport;
using Serilog;

namespace GemEquipmentInterface.Services;

/// <summary>
/// S6F11/S6F12 — Event Report Send / Acknowledge
/// S6F15/S6F16 — Enable/Disable Event Request / Acknowledge
///
/// GEM Event Reporting flow:
///   Equipment has a Collection Event (CEID)
///       → Equipment sends S6F11 to Host (with W-bit)
///       → Host replies S6F12 (acknowledge)
///
/// Each CEID can be associated with Report IDs (RPTIDs).
/// Each RPTID contains a list of SVIDs to report when that event fires.
///
/// This is the primary mechanism for data collection in GEM.
/// </summary>
public class EventService
{
    private readonly HsmsConnection _conn;
    private readonly EquipmentModel _equipment;

    // Enabled CEIDs — Host can enable/disable specific events
    private readonly HashSet<uint> _enabledCeids = [];

    // Event definitions
    private static readonly Dictionary<uint, string> _ceidDefs = new()
    {
        [EquipmentConstants.CEID_EQUIPMENT_ONLINE] = "Equipment came online",
        [EquipmentConstants.CEID_LOT_STARTED]      = "Lot processing started",
        [EquipmentConstants.CEID_LOT_COMPLETED]    = "Lot processing completed",
        [EquipmentConstants.CEID_WAFER_STARTED]    = "Wafer processing started",
        [EquipmentConstants.CEID_WAFER_COMPLETED]  = "Wafer processing completed",
        [EquipmentConstants.CEID_ALARM_SET]        = "Alarm condition set",
        [EquipmentConstants.CEID_ALARM_CLEARED]    = "Alarm condition cleared",
        [EquipmentConstants.CEID_PROCESS_STARTED]  = "Process step started",
        [EquipmentConstants.CEID_PROCESS_ENDED]    = "Process step ended",
    };

    // CEID → SVIDs to include in each event report
    private static readonly Dictionary<uint, List<uint>> _eventSvids = new()
    {
        [EquipmentConstants.CEID_EQUIPMENT_ONLINE] = [
            EquipmentConstants.SVID_CLOCK,
            EquipmentConstants.SVID_CONTROL_STATE],

        [EquipmentConstants.CEID_LOT_STARTED]      = [
            EquipmentConstants.SVID_CLOCK,
            EquipmentConstants.SVID_LOT_ID,
            EquipmentConstants.SVID_RECIPE_ID],

        [EquipmentConstants.CEID_LOT_COMPLETED]    = [
            EquipmentConstants.SVID_CLOCK,
            EquipmentConstants.SVID_LOT_ID,
            EquipmentConstants.SVID_WAFER_COUNT],

        [EquipmentConstants.CEID_ALARM_SET]        = [
            EquipmentConstants.SVID_CLOCK,
            EquipmentConstants.SVID_PROCESS_STATE],
    };

    private uint _reportIdCounter = 1;

    public EventService(HsmsConnection conn, EquipmentModel equipment)
    {
        _conn      = conn;
        _equipment = equipment;

        // Enable all events by default
        foreach (var ceid in _ceidDefs.Keys)
            _enabledCeids.Add(ceid);

        // Wire up equipment collection events
        _equipment.CollectionEventTriggered += OnCollectionEvent;
    }

    // ── Equipment fires event → send S6F11 to Host ────────────────
    private async void OnCollectionEvent(object? sender, uint ceid)
    {
        if (!_enabledCeids.Contains(ceid))
        {
            Log.Debug("CEID {Ceid} disabled — not reporting", ceid);
            return;
        }
        await SendEventReportAsync(ceid);
    }

    // ── S6F11: Event Report Send ──────────────────────────────────
    /// <summary>
    /// Equipment sends S6F11 to Host when a collection event fires.
    ///
    /// S6F11 body:
    ///   L[3]
    ///     U4    DATAID  (report data ID — unique per send)
    ///     U4    CEID    (collection event ID)
    ///     L[N]          (list of reports)
    ///       L[2]
    ///         U4    RPTID   (report ID)
    ///         L[M]          (list of variables)
    ///           item        (status variable value)
    ///
    /// W-bit set — Host must reply S6F12.
    /// </summary>
    public async Task SendEventReportAsync(uint ceid)
    {
        Log.Information("S6F11 Event Report → CEID={Ceid} ({Name})",
            ceid, _ceidDefs.GetValueOrDefault(ceid, "Unknown"));

        // Build the variable values for this event
        var rptItems = new List<SecsItem>();
        if (_eventSvids.TryGetValue(ceid, out var svids))
        {
            var values = svids.Select(svid =>
                BuildSecsValue(_equipment.GetStatusVariable(svid))
            ).ToList();

            rptItems.Add(SecsItem.L(
                SecsItem.U4(_reportIdCounter++),
                SecsItem.L(values)
            ));
        }

        var msg = new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S6,
            Function    = 11,            // S6F11
            ReplyBit    = true,          // W-bit
            SystemBytes = _conn.NextSystemBytes(),
            Body        = SecsItem.L(
                SecsItem.U4(_reportIdCounter++),  // DATAID
                SecsItem.U4(ceid),                 // CEID
                SecsItem.L(rptItems)               // reports
            )
        };

        var reply = await _conn.SendAndWaitAsync(msg, TimeSpan.FromSeconds(10));

        if (reply is null)
            Log.Error("S6F12 Event Ack TIMEOUT for CEID={Ceid}", ceid);
        else
            Log.Information("S6F12 Event Ack received for CEID={Ceid}", ceid);
    }

    // ── S6F15 → S6F16: Enable/Disable Events ─────────────────────
    /// <summary>
    /// Host sends S6F15 to enable or disable specific collection events.
    ///
    /// S6F15 body:
    ///   L[2]
    ///     B[1]  CEED (0=disable, 1=enable, 2=enable all, 3=disable all)
    ///     L[N]  — list of CEIDs (U4)
    ///
    /// S6F16 body:
    ///   B[1]  ERRCODE (0=OK)
    /// </summary>
    public void HandleEnableDisableEvents(SecsMessage request)
    {
        if (request.Body is null) return;

        byte ceed    = request.Body.Items[0].GetU1();
        var ceidList = request.Body.Items.Count > 1
            ? request.Body.Items[1].Items
            : [];

        switch (ceed)
        {
            case 0:  // Disable specified
                foreach (var item in ceidList)
                    _enabledCeids.Remove(item.GetU4());
                break;
            case 1:  // Enable specified
                foreach (var item in ceidList)
                    _enabledCeids.Add(item.GetU4());
                break;
            case 2:  // Enable all
                foreach (var ceid in _ceidDefs.Keys)
                    _enabledCeids.Add(ceid);
                break;
            case 3:  // Disable all
                _enabledCeids.Clear();
                break;
        }

        Log.Information("S6F15 Enable/Disable Events — CEED={Ceed} affected={Count}",
            ceed, ceidList.Count);

        _conn.Send(new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S6,
            Function    = 16,   // S6F16
            ReplyBit    = false,
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.B(0)   // ERRCODE = 0
        });
    }

    private static SecsItem BuildSecsValue(object? value) => value switch
    {
        string  s => SecsItem.A(s),
        int     i => SecsItem.I4(i),
        uint    u => SecsItem.U4(u),
        null      => SecsItem.A(""),
        _         => SecsItem.A(value.ToString() ?? "")
    };
}