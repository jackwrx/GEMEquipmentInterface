// Core/GemConstants.cs
namespace GemEquipmentInterface.Core;

/// <summary>
/// SEMI E5 SECS-II Stream and Function codes.
/// S = Stream (category), F = Function (specific message)
/// Odd F = Primary (request), Even F = Secondary (reply)
/// </summary>
public static class GemConstants
{
    // ── Stream 1: Equipment Status ────────────────────────────────
    public const byte S1 = 1;
    public const byte F1 = 1;   // Are You There (request)
    public const byte F2 = 2;   // On Line Data  (reply)
    public const byte F3 = 3;   // Selected Equipment Status Request
    public const byte F4 = 4;   // Selected Equipment Status Data
    public const byte F5 = 5;   // Report Event Enable
    public const byte F6 = 6;   // Report Event Disable
    public const byte F11 = 11; // Status Variable Namelist Request
    public const byte F12 = 12; // Status Variable Namelist Reply
    public const byte F13 = 13; // Establish Communications Request
    public const byte F14 = 14; // Establish Communications Acknowledge
    public const byte F15 = 15; // Request OFF-LINE
    public const byte F16 = 16; // OFF-LINE Acknowledge
    public const byte F17 = 17; // Request ON-LINE
    public const byte F18 = 18; // ON-LINE Acknowledge

    // ── Stream 2: Equipment Control ───────────────────────────────
    public const byte S2 = 2;
    public const byte F41 = 41; // Host Command Send
    public const byte F42 = 42; // Host Command Acknowledge

    // ── Stream 5: Exception Handling (Alarms) ─────────────────────
    public const byte S5 = 5;
    // F1 = Alarm Report Send (EI → Host)
    // F2 = Alarm Report Acknowledge (Host → EI)
    // F5 = Enable/Disable Alarm Send (Host → EI)
    // F6 = Enable/Disable Alarm Acknowledge (EI → Host)

    // ── Stream 6: Data Collection ─────────────────────────────────
    public const byte S6 = 6;
    // F11 = Event Report Send (EI → Host)
    // F12 = Event Report Acknowledge (Host → EI)
    // F15 = Enable/Disable Event Request (Host → EI)
    // F16 = Enable/Disable Event Acknowledge (EI → Host)

    // ── Stream 16: Process Job Management ─────────────────────────
    public const byte S16 = 16;
    // F1  = Process Job Create (Host → EI)
    // F2  = Process Job Create Acknowledge
    // F3  = Process Job Cancel
    // F4  = Process Job Cancel Acknowledge
    // F5  = Process Job Pause
    // F6  = Process Job Pause Acknowledge
    // F7  = Process Job Resume
    // F8  = Process Job Resume Acknowledge
    // F11 = Process Job List
    // F12 = Process Job List Reply
    // F15 = Process Job Status Request
    // F16 = Process Job Status Reply

    // ── HSMS SType (Session layer control) ───────────────────────
    public const byte STYPE_DATA          = 0;
    public const byte STYPE_SELECT_REQ    = 1;
    public const byte STYPE_SELECT_RSP    = 2;
    public const byte STYPE_DESELECT_REQ  = 3;
    public const byte STYPE_DESELECT_RSP  = 4;
    public const byte STYPE_LINKTEST_REQ  = 5;
    public const byte STYPE_LINKTEST_RSP  = 6;
    public const byte STYPE_REJECT_REQ    = 7;
    public const byte STYPE_SEPARATE_REQ  = 9;

    // ── GEM HCACK codes (S2F42 acknowledge) ──────────────────────
    public const byte HCACK_OK               = 0;
    public const byte HCACK_INVALID_COMMAND  = 1;
    public const byte HCACK_PARAM_ERROR      = 2;
    public const byte HCACK_EXEC_ERROR       = 3;
    public const byte HCACK_NOT_NOW          = 4;

    // ── GEM COMMACK codes (S1F14) ─────────────────────────────────
    public const byte COMMACK_ACCEPTED       = 0;
    public const byte COMMACK_DENIED         = 1;

    // ── GEM OFLACK (S1F16 offline acknowledge) ────────────────────
    public const byte OFLACK_OK              = 0;

    // ── GEM ONLACK (S1F18 online acknowledge) ─────────────────────
    public const byte ONLACK_OK              = 0;
    public const byte ONLACK_REFUSED         = 1;
    public const byte ONLACK_ALREADY_ONLINE  = 2;
}