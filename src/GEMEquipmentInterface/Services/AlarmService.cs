// Services/AlarmService.cs
using GemEquipmentInterface.Core;
using GemEquipmentInterface.Equipment;
using GemEquipmentInterface.Transport;
using Serilog;

namespace GemEquipmentInterface.Services;

/// <summary>
/// S5F1/S5F2 — Alarm Report Send / Acknowledge
/// S5F5/S5F6 — Enable/Disable Alarm Send / Acknowledge
///
/// GEM Alarm flow:
///   Equipment detects alarm condition
///       → Sends S5F1 to Host (with W-bit: reply expected)
///       → Host replies S5F2 (acknowledge)
///       → Equipment sends S6F11 Collection Event (CEID_ALARM_SET)
///
/// The equipment also maintains a list of alarms that can be
/// individually enabled/disabled by the Host via S5F5.
/// </summary>
public class AlarmService
{
    private readonly HsmsConnection _conn;
    private readonly EquipmentModel _equipment;

    // Alarm enable flags — Host can disable specific alarms
    private readonly HashSet<uint> _enabledAlarms = [];

    // Alarm definitions with text
    private static readonly Dictionary<uint, string> _alarmDefs = new()
    {
        [EquipmentConstants.ALID_TEMP_HIGH]    = "Chamber temperature above setpoint",
        [EquipmentConstants.ALID_TEMP_LOW]     = "Chamber temperature below setpoint",
        [EquipmentConstants.ALID_PRESSURE_HIGH]= "Chamber pressure above setpoint",
        [EquipmentConstants.ALID_DOOR_OPEN]    = "Chamber door opened during processing",
        [EquipmentConstants.ALID_RECIPE_ERROR] = "Recipe execution error",
        [EquipmentConstants.ALID_COMM_FAULT]   = "Communication fault detected",
    };

    public AlarmService(HsmsConnection conn, EquipmentModel equipment)
    {
        _conn      = conn;
        _equipment = equipment;

        // All alarms enabled by default
        foreach (var alid in _alarmDefs.Keys)
            _enabledAlarms.Add(alid);

        // Wire up equipment alarm events
        _equipment.AlarmRaised  += OnAlarmRaised;
        _equipment.AlarmCleared += OnAlarmCleared;
    }

    // ── Equipment alarm raised → send S5F1 to Host ────────────────
    private async void OnAlarmRaised(object? sender, AlarmInfo alarm)
    {
        if (!_enabledAlarms.Contains(alarm.AlarmId))
        {
            Log.Debug("Alarm {Alid} is disabled — not reporting", alarm.AlarmId);
            return;
        }

        await SendAlarmReportAsync(alarm.AlarmId, alarm.Description, isSet: true);
    }

    private async void OnAlarmCleared(object? sender, AlarmInfo alarm)
    {
        if (!_enabledAlarms.Contains(alarm.AlarmId)) return;
        await SendAlarmReportAsync(alarm.AlarmId, alarm.Description, isSet: false);
    }

    // ── S5F1: Alarm Report Send ───────────────────────────────────
    /// <summary>
    /// Equipment sends S5F1 to Host when alarm sets or clears.
    ///
    /// S5F1 body:
    ///   L[3]
    ///     B[1]  ALCD (alarm code: bit0=alarm set, bit1=display)
    ///     U4    ALID (alarm ID)
    ///     A     ALTX (alarm text)
    ///
    /// W-bit set — equipment expects S5F2 acknowledge.
    /// </summary>
    public async Task SendAlarmReportAsync(uint alid, string alarmText, bool isSet)
    {
        byte alcd = isSet ? (byte)0x81 : (byte)0x80;
        // Bit 7 = display alarm (1), Bit 0 = alarm set (1) or clear (0)

        var msg = new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S5,
            Function    = GemConstants.F1,
            ReplyBit    = true,   // W-bit — reply expected
            SystemBytes = _conn.NextSystemBytes(),
            Body        = SecsItem.L(
                SecsItem.B(alcd),
                SecsItem.U4(alid),
                SecsItem.A(alarmText)
            )
        };

        Log.Warning("S5F1 Alarm Report → ALID={Alid} Set={IsSet} Text={Text}",
            alid, isSet, alarmText);

        var reply = await _conn.SendAndWaitAsync(msg, TimeSpan.FromSeconds(5));

        if (reply is null)
            Log.Error("S5F2 Alarm Ack TIMEOUT for ALID={Alid}", alid);
        else
            Log.Information("S5F2 Alarm Ack received for ALID={Alid}", alid);
    }

    // ── S5F5 → S5F6: Enable/Disable Alarm ────────────────────────
    /// <summary>
    /// Host sends S5F5 to enable or disable specific alarms.
    ///
    /// S5F5 body:
    ///   L[2]
    ///     B[1]  ALED (0=disable, 1=enable)
    ///     L[N]  — list of ALIDs (U4)
    ///
    /// S5F6 body:
    ///   B[1]  ERRCODE (0=OK)
    /// </summary>
    public void HandleEnableDisableAlarm(SecsMessage request)
    {
        if (request.Body is null || request.Body.Items.Count < 2)
        {
            Log.Warning("S5F5 malformed — missing body");
            return;
        }

        bool enable = request.Body.Items[0].GetU1() == 1;
        var alidList = request.Body.Items[1].Items;

        foreach (var item in alidList)
        {
            uint alid = item.GetU4();
            if (enable) _enabledAlarms.Add(alid);
            else        _enabledAlarms.Remove(alid);
            Log.Information("Alarm {Alid} {Action}", alid, enable ? "ENABLED" : "DISABLED");
        }

        var reply = new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S5,
            Function    = GemConstants.F6,
            ReplyBit    = false,
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.B(0)   // ERRCODE = 0 (success)
        };

        _conn.Send(reply);
        Log.Information("S5F6 Enable/Disable Alarm Ack sent");
    }
}