// Handlers/MessageDispatcher.cs
using GemEquipmentInterface.Core;
using GemEquipmentInterface.Services;
using Serilog;

namespace GemEquipmentInterface.Handlers;

/// <summary>
/// Routes incoming SECS-II messages to the correct service handler.
///
/// SECS-II messages are identified by Stream + Function:
///   S1F1  → ConnectionService.HandleAreYouThere
///   S1F3  → StatusService.HandleStatusRequest
///   S2F41 → RemoteControlService.HandleHostCommand
///   S5F5  → AlarmService.HandleEnableDisableAlarm
///   S6F15 → EventService.HandleEnableDisableEvents
///   S16F1 → ProcessJobService.HandleCreateProcessJob
///   etc.
/// </summary>
public class MessageDispatcher
{
    private readonly ConnectionService   _connectionSvc;
    private readonly StatusService       _statusSvc;
    private readonly AlarmService        _alarmSvc;
    private readonly EventService        _eventSvc;
    private readonly RemoteControlService _remoteSvc;
    private readonly ProcessJobService   _processJobSvc;

    public MessageDispatcher(
        ConnectionService    connectionSvc,
        StatusService        statusSvc,
        AlarmService         alarmSvc,
        EventService         eventSvc,
        RemoteControlService remoteSvc,
        ProcessJobService    processJobSvc)
    {
        _connectionSvc = connectionSvc;
        _statusSvc     = statusSvc;
        _alarmSvc      = alarmSvc;
        _eventSvc      = eventSvc;
        _remoteSvc     = remoteSvc;
        _processJobSvc = processJobSvc;
    }

    public void Dispatch(SecsMessage message)
    {
        Log.Debug("Dispatch: {Stream}F{Function}", message.Stream, message.Function);

        // Route by Stream + Function
        switch (message.Stream, message.Function)
        {
            // ── Stream 1: Equipment Status ────────────────────────
            case (1, 1):  _connectionSvc.HandleAreYouThere(message);    break;
            case (1, 13): _connectionSvc.HandleEstablishComm(message);   break;
            case (1, 15): _connectionSvc.HandleRequestOffline(message);  break;
            case (1, 17): _connectionSvc.HandleRequestOnline(message);   break;
            case (1, 3):  _statusSvc.HandleStatusRequest(message);       break;
            case (1, 11): _statusSvc.HandleStatusNamelist(message);      break;

            // ── Stream 2: Equipment Control ───────────────────────
            case (2, 41): _remoteSvc.HandleHostCommand(message);         break;

            // ── Stream 5: Alarms ──────────────────────────────────
            case (5, 5):  _alarmSvc.HandleEnableDisableAlarm(message);   break;

            // ── Stream 6: Data Collection ─────────────────────────
            case (6, 15): _eventSvc.HandleEnableDisableEvents(message);  break;

            // ── Stream 16: Process Jobs ───────────────────────────
            case (16, 1):  _processJobSvc.HandleCreateProcessJob(message); break;
            case (16, 3):  _processJobSvc.HandleCancelProcessJob(message); break;
            case (16, 11): _processJobSvc.HandleListProcessJobs(message);  break;
            case (16, 15): _processJobSvc.HandleProcessJobStatus(message); break;

            default:
                Log.Warning("Unhandled message: S{Stream}F{Function}",
                    message.Stream, message.Function);
                // Send S9F7 (Illegal Data) for unhandled messages
                break;
        }
    }
}