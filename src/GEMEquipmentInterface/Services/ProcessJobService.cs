// Services/ProcessJobService.cs
using GemEquipmentInterface.Core;
using GemEquipmentInterface.Equipment;
using GemEquipmentInterface.Transport;
using Serilog;

namespace GemEquipmentInterface.Services;

/// <summary>
/// S16 — Process Job Management
///
/// Process Jobs represent units of work on the equipment.
/// Host creates jobs, equipment executes them in order.
///
/// Key messages:
///   S16F1/F2  — Process Job Create / Acknowledge
///   S16F3/F4  — Process Job Cancel / Acknowledge
///   S16F5/F6  — Process Job Pause / Acknowledge
///   S16F7/F8  — Process Job Resume / Acknowledge
///   S16F11/F12 — Process Job List / Reply
///   S16F15/F16 — Process Job Status / Reply
/// </summary>
public class ProcessJobService
{
    private readonly HsmsConnection _conn;
    private readonly EquipmentModel _equipment;

    public ProcessJobService(HsmsConnection conn, EquipmentModel equipment)
    {
        _conn      = conn;
        _equipment = equipment;
    }

    // ── S16F1 → S16F2: Create Process Job ────────────────────────
    /// <summary>
    /// Host creates a process job.
    ///
    /// S16F1 body:
    ///   L[4]
    ///     A     JOBID      (job identifier)
    ///     A     RECIPEID   (recipe to run)
    ///     L[N]             (list of carrier IDs)
    ///       A   CarrierID
    ///     L[N]  PRJobCreateSpec parameters
    ///
    /// S16F2 body:
    ///   L[2]
    ///     A     JOBID
    ///     B[1]  PRJOBCREATESTATUS (0=OK, 1=already exists, 2=error)
    /// </summary>
    public void HandleCreateProcessJob(SecsMessage request)
    {
        if (request.Body is null || request.Body.Items.Count < 3)
        {
            SendCreateAck(request, "INVALID", 2);
            return;
        }

        string jobId    = request.Body.Items[0].GetAscii();
        string recipeId = request.Body.Items[1].GetAscii();
        var carriers    = request.Body.Items[2].Items
            .Select(i => i.GetAscii()).ToList();

        Log.Information("S16F1 Create ProcessJob: {JobId} recipe={Recipe} carriers={Carriers}",
            jobId, recipeId, string.Join(",", carriers));

        if (_equipment.ProcessJobs.ContainsKey(jobId))
        {
            SendCreateAck(request, jobId, 1);   // Already exists
            return;
        }

        _equipment.CreateProcessJob(jobId, recipeId, carriers);
        SendCreateAck(request, jobId, 0);        // Success
    }

    private void SendCreateAck(SecsMessage request, string jobId, byte status)
    {
        _conn.Send(new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S16,
            Function    = 2,    // S16F2
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.L(
                SecsItem.A(jobId),
                SecsItem.B(status)
            )
        });
        Log.Information("S16F2 Create ProcessJob Ack — JobId={JobId} Status={Status}",
            jobId, status);
    }

    // ── S16F3 → S16F4: Cancel Process Job ────────────────────────
    public void HandleCancelProcessJob(SecsMessage request)
    {
        string jobId = request.Body?.Items[0].GetAscii() ?? "";
        Log.Information("S16F3 Cancel ProcessJob: {JobId}", jobId);

        bool ok = _equipment.CancelProcessJob(jobId);

        _conn.Send(new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S16,
            Function    = 4,    // S16F4
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.L(
                SecsItem.A(jobId),
                SecsItem.B(ok ? (byte)0 : (byte)2)
            )
        });
    }

    // ── S16F11 → S16F12: List Process Jobs ────────────────────────
    public void HandleListProcessJobs(SecsMessage request)
    {
        Log.Information("S16F11 List ProcessJobs — {Count} jobs",
            _equipment.ProcessJobs.Count);

        var jobItems = _equipment.ProcessJobs.Values.Select(job =>
            SecsItem.L(
                SecsItem.A(job.JobId),
                SecsItem.A(job.RecipeId),
                SecsItem.A(job.State),
                SecsItem.A(job.CreatedAt.ToString("yyyyMMddHHmmss"))
            )
        ).ToList();

        _conn.Send(new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S16,
            Function    = 12,   // S16F12
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.L(jobItems)
        });
    }

    // ── S16F15 → S16F16: Process Job Status ──────────────────────
    public void HandleProcessJobStatus(SecsMessage request)
    {
        string jobId = request.Body?.Items.FirstOrDefault()?.GetAscii() ?? "";

        if (!_equipment.ProcessJobs.TryGetValue(jobId, out var job))
        {
            _conn.Send(new SecsMessage
            {
                DeviceId    = _conn.DeviceId,
                Stream      = GemConstants.S16,
                Function    = 16,
                SystemBytes = request.SystemBytes,
                Body        = SecsItem.L(SecsItem.A(jobId), SecsItem.A("NOT_FOUND"))
            });
            return;
        }

        _conn.Send(new SecsMessage
        {
            DeviceId    = _conn.DeviceId,
            Stream      = GemConstants.S16,
            Function    = 16,   // S16F16
            SystemBytes = request.SystemBytes,
            Body        = SecsItem.L(
                SecsItem.A(job.JobId),
                SecsItem.A(job.RecipeId),
                SecsItem.A(job.State),
                SecsItem.L(job.Carriers.Select(c => SecsItem.A(c)).ToList()),
                SecsItem.A(job.CreatedAt.ToString("yyyyMMddHHmmss"))
            )
        });
    }
}