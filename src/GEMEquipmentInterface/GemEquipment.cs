// GemEquipment.cs
using GemEquipmentInterface.Equipment;
using GemEquipmentInterface.Handlers;
using GemEquipmentInterface.Services;
using GemEquipmentInterface.Transport;
using Serilog;

namespace GemEquipmentInterface;

/// <summary>
/// Top-level GEM Equipment Interface.
/// Wires together all components and starts the HSMS server.
///
/// Usage:
///   var gem = new GemEquipment("127.0.0.1", 5000);
///   await gem.StartAsync();
/// </summary>
public class GemEquipment
{
    private readonly string          _host;
    private readonly int             _port;
    private readonly HsmsConnection  _conn;
    private readonly EquipmentModel  _equipment;
    private readonly MessageDispatcher _dispatcher;

    // Expose equipment model for external hardware integration
    public EquipmentModel Equipment => _equipment;

    public GemEquipment(string host, int port, ushort deviceId = 0)
    {
        _host      = host;
        _port      = port;
        _equipment = new EquipmentModel();
        _conn      = new HsmsConnection(deviceId);

        // ── Wire up services ──────────────────────────────────────
        var connectionSvc = new ConnectionService(_conn, _equipment);
        var statusSvc     = new StatusService(_conn, _equipment);
        var alarmSvc      = new AlarmService(_conn, _equipment);
        var eventSvc      = new EventService(_conn, _equipment);
        var remoteSvc     = new RemoteControlService(_conn, _equipment);
        var processJobSvc = new ProcessJobService(_conn, _equipment);

        // ── Wire up dispatcher ────────────────────────────────────
        _dispatcher = new MessageDispatcher(
            connectionSvc, statusSvc, alarmSvc,
            eventSvc, remoteSvc, processJobSvc);

        // ── Route received messages to dispatcher ─────────────────
        _conn.MessageReceived += (_, msg) => _dispatcher.Dispatch(msg);

        // ── Log HSMS session events ───────────────────────────────
        _conn.Connected    += (_, _) => Log.Information("Host connected");
        _conn.Selected     += (_, _) => Log.Information("HSMS Selected — GEM session active ✅");
        _conn.Disconnected += (_, _) =>
        {
            Log.Warning("Host disconnected");
            _equipment.GoOffline();
        };
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        Log.Information("🏭 GEM Equipment Interface starting on {Host}:{Port}", _host, _port);
        Log.Information("Waiting for Host connection...");
        await _conn.StartServerAsync(_host, _port);
    }

    // ── Simulate hardware events (call from your hardware layer) ──

    public void SimulateAlarm(uint alid, string description)
        => _equipment.RaiseAlarm(alid, description);

    public void SimulateClearAlarm(uint alid)
        => _equipment.ClearAlarm(alid);

    public bool SimulateStartProcess(string recipeId, string lotId)
        => _equipment.StartProcess(recipeId, lotId);
}