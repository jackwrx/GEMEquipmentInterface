// Services/RemoteControlService.cs
using GemEquipmentInterface.Core;
using GemEquipmentInterface.Equipment;
using GemEquipmentInterface.Transport;
using Serilog;

namespace GemEquipmentInterface.Services;

/// <summary>
/// S2F41/S2F42 — Host Command Send / Acknowledge
///
/// The Host uses S2F41 to send remote commands to the equipment.
/// Equipment executes the command and replies S2F42 with HCACK.
///
/// S2F41 body:
///   L[2]
///     A     RCMD  (remote command string)
///     L[N]        (optional parameters)
///       L[2]
///         A   CPNAME  (parameter name)
///         A   CPVAL   (parameter value)
///
/// S2F42 body:
///   L[2]
///     B[1]  HCACK  (0=OK, 1=invalid command, 2=param error, 3=exec error)
///     L[N]         (parameter errors if HCACK != 0)
/// </summary>
public class RemoteControlService
{
    private readonly HsmsConnection _conn;
    private readonly EquipmentModel _equipment;

    public RemoteControlService(HsmsConnection conn, EquipmentModel equipment)
    {
        _conn      = conn;
        _equipment = equipment;
    }

    // ── S2F41 → S2F42: Host Command Send ─────────────────────────
    public void HandleHostCommand(SecsMessage request)
    {
        if (request.Body is null || request.Body.Items.Count < 1)
        {
            SendAck(request, GemConstants.HCACK_PARAM_ERROR, []);
            return;
        }

        string rcmd   = request.Body.Items[0].GetAscii().Trim().ToUpper();
        var    cpList = request.Body.Items.Count > 1
            ? ParseParameters(request.Body.Items[1])
            : new Dictionary<string, string>();

        Log.Information("S2F41 Remote Command: RCMD={Rcmd} Params={Params}",
            rcmd, string.Join(",", cpList.Select(kv => $"{kv.Key}={kv.Value}")));

        // ── Dispatch to equipment handler ─────────────────────────
        var (hcack, errors) = ExecuteCommand(rcmd, cpList);

        // ── S2F42 reply ───────────────────────────────────────────
        SendAck(request, hcack, errors);
        Log.Information("S2F42 HCACK={Hcack} for RCMD={Rcmd}", hcack, rcmd);
    }

    private (byte hcack, List<string> errors) ExecuteCommand(
        string rcmd, Dictionary<string, string> cpList)
    {
        try
        {
            bool success = rcmd switch
            {
                EquipmentConstants.RCMD_START =>
                    _equipment.StartProcess(
                        cpList.GetValueOrDefault("RecipeId", "DEFAULT"),
                        cpList.GetValueOrDefault("LotId", "LOT001")),

                EquipmentConstants.RCMD_STOP =>
                    _equipment.StopProcess(),

                EquipmentConstants.RCMD_PAUSE =>
                    _equipment.PauseProcess(),

                EquipmentConstants.RCMD_RESUME =>
                    _equipment.ResumeProcess(),

                EquipmentConstants.RCMD_ABORT =>
                    _equipment.AbortProcess(),

                EquipmentConstants.RCMD_HOME =>
                    _equipment.HomeEquipment(),

                EquipmentConstants.RCMD_RESET =>
                    _equipment.ResetEquipment(),

                EquipmentConstants.RCMD_CLEAR_ALARM =>
                    ClearAlarm(cpList),

                _ => false
            };

            if (!success)
            {
                Log.Warning("RCMD {Rcmd} failed — invalid state", rcmd);
                return (GemConstants.HCACK_NOT_NOW, []);
            }

            return (GemConstants.HCACK_OK, []);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RCMD {Rcmd} execution error", rcmd);
            return (GemConstants.HCACK_EXEC_ERROR, [ex.Message]);
        }
    }

    private bool ClearAlarm(Dictionary<string, string> cpList)
    {
        if (cpList.TryGetValue("AlarmId", out var alidStr) &&
            uint.TryParse(alidStr, out uint alid))
        {
            _equipment.ClearAlarm(alid);
            return true;
        }
        // Clear all alarms
        foreach (var alid2 in _equipment.ActiveAlarms.Keys.ToList())
            _equipment.ClearAlarm(alid2);
        return true;
    }

    private void SendAck(SecsMessage request, byte hcack, List<string> errors)
    {
        var errorItems = errors.Select(e =>
            SecsItem.L(SecsItem.A("ERROR"), SecsItem.A(e))
        ).ToList();

        _conn.Send(new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S2,
            Function    = GemConstants.F42,
            ReplyBit    = false,
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.L(
                SecsItem.B(hcack),
                SecsItem.L(errorItems)
            )
        });
    }

    private static Dictionary<string, string> ParseParameters(SecsItem paramList)
    {
        var result = new Dictionary<string, string>();
        foreach (var item in paramList.Items)
        {
            if (item.Items.Count >= 2)
                result[item.Items[0].GetAscii()] = item.Items[1].GetAscii();
        }
        return result;
    }
}