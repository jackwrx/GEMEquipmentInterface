// Program.cs
using Serilog;
using GemEquipmentInterface;
using GemEquipmentInterface.Equipment;

// ── Serilog ───────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/gem-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

// ── Create GEM Equipment Interface ───────────────────────────────
// TCP server — Host connects to port 5000
var gem = new GemEquipment(
    host:     "0.0.0.0",
    port:     5000,
    deviceId: 1
);

Log.Information("╔════════════════════════════════════════╗");
Log.Information("║     SEMI GEM Equipment Interface       ║");
Log.Information("║     HSMS Server: 0.0.0.0:5000          ║");
Log.Information("╚════════════════════════════════════════╝");

// ── Simulate some equipment activity after 10 seconds ─────────────
_ = Task.Run(async () =>
{
    await Task.Delay(10_000);   // wait for host to connect

    Log.Information("--- Simulating equipment activity ---");

    // Simulate a temperature alarm
    gem.SimulateAlarm(
        EquipmentConstants.ALID_TEMP_HIGH,
        "Chamber temperature exceeded 250°C — setpoint is 200°C");

    await Task.Delay(3_000);

    // Simulate clearing the alarm
    gem.SimulateClearAlarm(EquipmentConstants.ALID_TEMP_HIGH);

    await Task.Delay(2_000);

    // Simulate starting a process
    gem.SimulateStartProcess("RECIPE_A", "LOT_20260401_001");
});

// ── Start HSMS server (blocks until cancelled) ────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await gem.StartAsync(cts.Token);

Log.Information("GEM Equipment Interface stopped.");