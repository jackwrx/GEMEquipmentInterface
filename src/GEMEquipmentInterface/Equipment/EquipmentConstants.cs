// Equipment/EquipmentConstants.cs
namespace GemEquipmentInterface.Equipment;

/// <summary>
/// GEM Equipment Constants — these are the IDs your equipment exposes.
/// In a real system these come from your equipment's GEM configuration.
///
/// SVID  = Status Variable ID  (real-time equipment readings)
/// ECID  = Equipment Constant ID (configurable parameters)
/// ALID  = Alarm ID
/// CEID  = Collection Event ID (triggers event reports)
/// RCMD  = Remote Command
/// </summary>
public static class EquipmentConstants
{
    // ── Status Variables (SVIDs) ──────────────────────────────────
    // These are read-only real-time equipment values
    public const uint SVID_CLOCK            = 1;    // Current time
    public const uint SVID_CONTROL_STATE    = 2;    // Online/Offline/Local/Remote
    public const uint SVID_PROCESS_STATE    = 3;    // Idle/Processing/Paused/Error
    public const uint SVID_RECIPE_ID        = 4;    // Active recipe name
    public const uint SVID_LOT_ID          = 5;    // Current lot ID
    public const uint SVID_WAFER_COUNT     = 6;    // Wafers processed count
    public const uint SVID_CHAMBER_TEMP    = 7;    // Chamber temperature (°C)
    public const uint SVID_CHAMBER_PRESSURE= 8;    // Chamber pressure (Torr)
    public const uint SVID_UPTIME_SECONDS  = 9;    // Equipment uptime

    // ── Equipment Constants (ECIDs) ───────────────────────────────
    // These are configurable by the Host
    public const uint ECID_MAX_BATCH_SIZE  = 101;  // Max wafers per batch
    public const uint ECID_TEMP_SETPOINT   = 102;  // Temperature setpoint (°C)
    public const uint ECID_PRESSURE_SETPOINT = 103; // Pressure setpoint
    public const uint ECID_RECIPE_PATH     = 104;  // Recipe file directory

    // ── Alarm IDs (ALIDs) ─────────────────────────────────────────
    // These trigger S5F1 alarm reports
    public const uint ALID_TEMP_HIGH       = 1001; // Over-temperature alarm
    public const uint ALID_TEMP_LOW        = 1002; // Under-temperature alarm
    public const uint ALID_PRESSURE_HIGH   = 1003; // Over-pressure alarm
    public const uint ALID_DOOR_OPEN       = 1004; // Door open during processing
    public const uint ALID_RECIPE_ERROR    = 1005; // Recipe execution error
    public const uint ALID_COMM_FAULT      = 1006; // Communication fault

    // ── Collection Event IDs (CEIDs) ──────────────────────────────
    // These trigger S6F11 event reports
    public const uint CEID_EQUIPMENT_ONLINE = 2001; // Equipment came online
    public const uint CEID_LOT_STARTED      = 2002; // Lot processing started
    public const uint CEID_LOT_COMPLETED    = 2003; // Lot processing completed
    public const uint CEID_WAFER_STARTED    = 2004; // Individual wafer started
    public const uint CEID_WAFER_COMPLETED  = 2005; // Individual wafer completed
    public const uint CEID_ALARM_SET        = 2006; // Alarm condition set
    public const uint CEID_ALARM_CLEARED    = 2007; // Alarm condition cleared
    public const uint CEID_PROCESS_STARTED  = 2008; // Process step started
    public const uint CEID_PROCESS_ENDED    = 2009; // Process step ended

    // ── Remote Commands (RCMDs) ───────────────────────────────────
    // These are sent by Host via S2F41
    public const string RCMD_START          = "START";
    public const string RCMD_STOP           = "STOP";
    public const string RCMD_PAUSE          = "PAUSE";
    public const string RCMD_RESUME         = "RESUME";
    public const string RCMD_ABORT          = "ABORT";
    public const string RCMD_HOME           = "HOME";
    public const string RCMD_RESET          = "RESET";
    public const string RCMD_CLEAR_ALARM    = "CLEAR_ALARM";

    // ── GEM Control States ────────────────────────────────────────
    public const int STATE_OFFLINE_EQ_OFFLINE  = 1;
    public const int STATE_OFFLINE_ATTEMPT_ONLINE = 2;
    public const int STATE_ONLINE_LOCAL        = 3;
    public const int STATE_ONLINE_REMOTE       = 4;

    // ── Process States ────────────────────────────────────────────
    public const int PROC_STATE_IDLE           = 0;
    public const int PROC_STATE_SETUP          = 1;
    public const int PROC_STATE_READY          = 2;
    public const int PROC_STATE_EXECUTING      = 3;
    public const int PROC_STATE_PAUSE          = 4;
    public const int PROC_STATE_COMPLETE       = 5;
    public const int PROC_STATE_ERROR          = 9;
}