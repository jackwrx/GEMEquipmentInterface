// Equipment/EquipmentModel.cs
using GemEquipmentInterface.Equipment;
using Serilog;

namespace GemEquipmentInterface.Equipment;

/// <summary>
/// Simulated equipment state.
/// In production this would connect to real hardware PLCs,
/// sensors, and actuators via OPC-UA, serial, Ethernet I/O etc.
/// </summary>
public class EquipmentModel
{
    // ── GEM Control State ─────────────────────────────────────────
    public int    ControlState  { get; private set; }
        = EquipmentConstants.STATE_OFFLINE_EQ_OFFLINE;

    // ── Process State ─────────────────────────────────────────────
    public int    ProcessState  { get; private set; }
        = EquipmentConstants.PROC_STATE_IDLE;

    // ── Equipment Status Variables ────────────────────────────────
    public string CurrentRecipeId  { get; private set; } = "";
    public string CurrentLotId     { get; private set; } = "";
    public int    WaferCount       { get; private set; } = 0;
    public double ChamberTemp      { get; private set; } = 25.0;
    public double ChamberPressure  { get; private set; } = 760.0;
    public DateTime StartTime      { get; } = DateTime.UtcNow;

    // ── Equipment Constants (configurable) ────────────────────────
    public int    MaxBatchSize      { get; private set; } = 25;
    public double TempSetpoint      { get; private set; } = 200.0;
    public double PressureSetpoint  { get; private set; } = 0.001;
    public string RecipePath        { get; private set; } = "/recipes/";

    // ── Active Alarms ─────────────────────────────────────────────
    private readonly Dictionary<uint, AlarmInfo> _activeAlarms = [];
    public IReadOnlyDictionary<uint, AlarmInfo> ActiveAlarms => _activeAlarms;

    // ── Active Process Jobs ───────────────────────────────────────
    private readonly Dictionary<string, ProcessJob> _processJobs = [];
    public IReadOnlyDictionary<string, ProcessJob> ProcessJobs => _processJobs;

    // ── Events ────────────────────────────────────────────────────
    public event EventHandler<uint>?       CollectionEventTriggered;
    public event EventHandler<AlarmInfo>?  AlarmRaised;
    public event EventHandler<AlarmInfo>?  AlarmCleared;

    // ── Status Variable Map ───────────────────────────────────────
    public object? GetStatusVariable(uint svid) => svid switch
    {
        EquipmentConstants.SVID_CLOCK            => DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
        EquipmentConstants.SVID_CONTROL_STATE    => ControlState,
        EquipmentConstants.SVID_PROCESS_STATE    => ProcessState,
        EquipmentConstants.SVID_RECIPE_ID        => CurrentRecipeId,
        EquipmentConstants.SVID_LOT_ID           => CurrentLotId,
        EquipmentConstants.SVID_WAFER_COUNT      => WaferCount,
        EquipmentConstants.SVID_CHAMBER_TEMP     => (int)(ChamberTemp * 10),
        EquipmentConstants.SVID_CHAMBER_PRESSURE => (int)(ChamberPressure * 1000),
        EquipmentConstants.SVID_UPTIME_SECONDS   => (uint)(DateTime.UtcNow - StartTime).TotalSeconds,
        _                                         => null
    };

    // ── Equipment Constant Map ────────────────────────────────────
    public object? GetEquipmentConstant(uint ecid) => ecid switch
    {
        EquipmentConstants.ECID_MAX_BATCH_SIZE    => MaxBatchSize,
        EquipmentConstants.ECID_TEMP_SETPOINT     => (int)TempSetpoint,
        EquipmentConstants.ECID_PRESSURE_SETPOINT => (int)(PressureSetpoint * 1000),
        EquipmentConstants.ECID_RECIPE_PATH       => RecipePath,
        _                                          => null
    };

    // ── GEM Control State Transitions ─────────────────────────────
    public bool GoOnline()
    {
        if (ControlState == EquipmentConstants.STATE_OFFLINE_EQ_OFFLINE ||
            ControlState == EquipmentConstants.STATE_OFFLINE_ATTEMPT_ONLINE)
        {
            ControlState = EquipmentConstants.STATE_ONLINE_REMOTE;
            Log.Information("Equipment → ONLINE REMOTE");
            TriggerEvent(EquipmentConstants.CEID_EQUIPMENT_ONLINE);
            return true;
        }
        return false;
    }

    public bool GoOffline()
    {
        ControlState = EquipmentConstants.STATE_OFFLINE_EQ_OFFLINE;
        Log.Information("Equipment → OFFLINE");
        return true;
    }

    public bool GoLocal()
    {
        if (ControlState == EquipmentConstants.STATE_ONLINE_REMOTE)
        {
            ControlState = EquipmentConstants.STATE_ONLINE_LOCAL;
            Log.Information("Equipment → ONLINE LOCAL");
            return true;
        }
        return false;
    }

    // ── Process Control ───────────────────────────────────────────
    public bool StartProcess(string recipeId, string lotId)
    {
        if (ProcessState != EquipmentConstants.PROC_STATE_IDLE &&
            ProcessState != EquipmentConstants.PROC_STATE_READY)
        {
            Log.Warning("Cannot start — process state is {State}", ProcessState);
            return false;
        }
        CurrentRecipeId = recipeId;
        CurrentLotId    = lotId;
        ProcessState    = EquipmentConstants.PROC_STATE_EXECUTING;
        ChamberTemp     = TempSetpoint;       // simulate ramp-up
        ChamberPressure = PressureSetpoint;
        TriggerEvent(EquipmentConstants.CEID_LOT_STARTED);
        Log.Information("Process STARTED recipe={Recipe} lot={Lot}", recipeId, lotId);
        return true;
    }

    public bool PauseProcess()
    {
        if (ProcessState != EquipmentConstants.PROC_STATE_EXECUTING) return false;
        ProcessState = EquipmentConstants.PROC_STATE_PAUSE;
        TriggerEvent(EquipmentConstants.CEID_PROCESS_ENDED);
        Log.Information("Process PAUSED");
        return true;
    }

    public bool ResumeProcess()
    {
        if (ProcessState != EquipmentConstants.PROC_STATE_PAUSE) return false;
        ProcessState = EquipmentConstants.PROC_STATE_EXECUTING;
        TriggerEvent(EquipmentConstants.CEID_PROCESS_STARTED);
        Log.Information("Process RESUMED");
        return true;
    }

    public bool StopProcess()
    {
        ProcessState = EquipmentConstants.PROC_STATE_COMPLETE;
        TriggerEvent(EquipmentConstants.CEID_LOT_COMPLETED);
        Log.Information("Process STOPPED lot={Lot} wafers={Count}", CurrentLotId, WaferCount);
        ProcessState    = EquipmentConstants.PROC_STATE_IDLE;
        CurrentLotId    = "";
        CurrentRecipeId = "";
        return true;
    }

    public bool AbortProcess()
    {
        ProcessState = EquipmentConstants.PROC_STATE_ERROR;
        Log.Warning("Process ABORTED");
        RaiseAlarm(EquipmentConstants.ALID_RECIPE_ERROR, "Process aborted by host");
        ProcessState = EquipmentConstants.PROC_STATE_IDLE;
        return true;
    }

    public bool HomeEquipment()
    {
        ProcessState = EquipmentConstants.PROC_STATE_IDLE;
        ChamberTemp     = 25.0;
        ChamberPressure = 760.0;
        WaferCount      = 0;
        Log.Information("Equipment HOMED");
        return true;
    }

    public bool ResetEquipment()
    {
        foreach (var alid in _activeAlarms.Keys.ToList())
            ClearAlarm(alid);
        ProcessState = EquipmentConstants.PROC_STATE_IDLE;
        Log.Information("Equipment RESET");
        return true;
    }

    // ── Alarm Management ──────────────────────────────────────────
    public void RaiseAlarm(uint alid, string description)
    {
        if (_activeAlarms.ContainsKey(alid)) return;

        var alarm = new AlarmInfo(alid, description, true, DateTime.UtcNow);
        _activeAlarms[alid] = alarm;
        AlarmRaised?.Invoke(this, alarm);
        TriggerEvent(EquipmentConstants.CEID_ALARM_SET);
        Log.Warning("ALARM SET ALID={Alid} {Desc}", alid, description);
    }

    public void ClearAlarm(uint alid)
    {
        if (!_activeAlarms.TryGetValue(alid, out var alarm)) return;
        _activeAlarms.Remove(alid);
        var cleared = alarm with { IsSet = false };
        AlarmCleared?.Invoke(this, cleared);
        TriggerEvent(EquipmentConstants.CEID_ALARM_CLEARED);
        Log.Information("ALARM CLEARED ALID={Alid}", alid);
    }

    // ── Process Job Operations ────────────────────────────────────
    public ProcessJob CreateProcessJob(string jobId, string recipeId, List<string> carriers)
    {
        var job = new ProcessJob(jobId, recipeId, carriers, "QUEUED", DateTime.UtcNow);
        _processJobs[jobId] = job;
        Log.Information("ProcessJob created: {JobId}", jobId);
        return job;
    }

    public bool CancelProcessJob(string jobId)
    {
        if (!_processJobs.TryGetValue(jobId, out var job)) return false;
        _processJobs[jobId] = job with { State = "CANCELLED" };
        Log.Information("ProcessJob cancelled: {JobId}", jobId);
        return true;
    }

    // ── Private helpers ───────────────────────────────────────────
    private void TriggerEvent(uint ceid)
        => CollectionEventTriggered?.Invoke(this, ceid);
}

public record AlarmInfo(uint AlarmId, string Description, bool IsSet, DateTime Timestamp);
public record ProcessJob(
    string JobId, string RecipeId, List<string> Carriers,
    string State, DateTime CreatedAt);