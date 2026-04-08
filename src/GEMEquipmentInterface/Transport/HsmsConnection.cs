// Transport/HsmsConnection.cs
using System.Net;
using System.Net.Sockets;
using GemEquipmentInterface.Core;
using Serilog;

namespace GemEquipmentInterface.Transport;

/// <summary>
/// SEMI E37 HSMS (High-Speed Message Services) TCP/IP connection.
///
/// HSMS is the transport layer for SECS-II messages over TCP/IP.
/// The EI runs as TCP server — Host connects to it.
///
/// Session states:
///   NOT_CONNECTED → CONNECTED → SELECTED → SELECTED (active)
///
/// Wire format per message:
///   [4 bytes: length N] [N bytes: header + body]
/// </summary>
public class HsmsConnection : IDisposable
{
    private TcpListener?       _listener;
    private TcpClient?         _client;
    private NetworkStream?     _stream;
    private CancellationTokenSource _cts = new();
    private readonly object    _sendLock = new();
    private uint               _systemBytesCounter = 1;

    public bool IsConnected  => _client?.Connected ?? false;
    public bool IsSelected   { get; private set; }
    public ushort DeviceId   { get; }

    // ── Events ────────────────────────────────────────────────────
    public event EventHandler<SecsMessage>? MessageReceived;
    public event EventHandler?              Connected;
    public event EventHandler?              Disconnected;
    public event EventHandler?              Selected;

    public HsmsConnection(ushort deviceId = 0)
    {
        DeviceId = deviceId;
    }

    // ── Start TCP server — wait for Host to connect ───────────────
    public async Task StartServerAsync(string host, int port)
    {
        _listener = new TcpListener(IPAddress.Parse(host), port);
        _listener.Start();
        Log.Information("HSMS listening on {Host}:{Port}", host, port);

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                _client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _client.NoDelay = true;   // disable Nagle — send immediately
                _stream = _client.GetStream();
                IsSelected = false;

                Log.Information("Host connected: {Endpoint}",
                    _client.Client.RemoteEndPoint);
                Connected?.Invoke(this, EventArgs.Empty);

                // Start receive loop for this connection
                await ReceiveLoopAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Error(ex, "HSMS connection error");
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // ── Receive loop — reads messages from TCP stream ─────────────
    private async Task ReceiveLoopAsync()
    {
        var lengthBuf = new byte[4];
        try
        {
            while (!_cts.Token.IsCancellationRequested && IsConnected)
            {
                // Read 4-byte length prefix
                await ReadExactAsync(lengthBuf, 4);
                int msgLen = (lengthBuf[0] << 24) | (lengthBuf[1] << 16)
                           | (lengthBuf[2] << 8)  |  lengthBuf[3];

                if (msgLen < 10 || msgLen > 1_048_576)
                {
                    Log.Warning("Invalid HSMS message length: {Len}", msgLen);
                    break;
                }

                // Read full message body
                var msgBuf = new byte[4 + msgLen];
                Array.Copy(lengthBuf, msgBuf, 4);
                await ReadExactAsync(msgBuf, msgLen, 4);

                var msg = SecsMessage.Decode(msgBuf);
                Log.Debug("RECV ← {Msg}", msg);

                // Handle HSMS control messages internally
                if (msg.SType != GemConstants.STYPE_DATA)
                    await HandleControlMessageAsync(msg);
                else
                    MessageReceived?.Invoke(this, msg);
            }
        }
        catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
        {
            Log.Warning(ex, "HSMS receive loop ended");
        }
        finally
        {
            IsSelected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── Handle HSMS session-layer control messages ─────────────────
    private async Task HandleControlMessageAsync(SecsMessage msg)
    {
        switch (msg.SType)
        {
            // Host wants to SELECT (activate the session)
            case GemConstants.STYPE_SELECT_REQ:
                Log.Information("HSMS SELECT.REQ received");
                await SendControlAsync(GemConstants.STYPE_SELECT_RSP, msg.SystemBytes);
                IsSelected = true;
                Selected?.Invoke(this, EventArgs.Empty);
                Log.Information("HSMS session SELECTED ✅");
                break;

            // Host wants to DESELECT (deactivate but keep TCP)
            case GemConstants.STYPE_DESELECT_REQ:
                Log.Information("HSMS DESELECT.REQ received");
                await SendControlAsync(GemConstants.STYPE_DESELECT_RSP, msg.SystemBytes);
                IsSelected = false;
                break;

            // Linktest — heartbeat to check connection is alive
            case GemConstants.STYPE_LINKTEST_REQ:
                Log.Debug("HSMS LINKTEST.REQ");
                await SendControlAsync(GemConstants.STYPE_LINKTEST_RSP, msg.SystemBytes);
                break;

            // Host wants to SEPARATE (close the TCP connection)
            case GemConstants.STYPE_SEPARATE_REQ:
                Log.Information("HSMS SEPARATE.REQ — closing connection");
                _client?.Close();
                break;

            default:
                Log.Warning("Unknown HSMS SType={SType}", msg.SType);
                break;
        }
    }

    // ── Send a SECS-II data message ───────────────────────────────
    public void Send(SecsMessage message)
    {
        if (!IsConnected || _stream is null)
            throw new InvalidOperationException("HSMS not connected.");

        var bytes = message.Encode();
        lock (_sendLock)
        {
            _stream.Write(bytes, 0, bytes.Length);
        }
        Log.Debug("SEND → {Msg}", message);
    }

    // ── Send and wait for reply ───────────────────────────────────
    public async Task<SecsMessage?> SendAndWaitAsync(
        SecsMessage message,
        TimeSpan?   timeout = null)
    {
        var tcs        = new TaskCompletionSource<SecsMessage?>();
        var replyFunc  = message.Function + 1;   // S1F1 → S1F2, S5F1 → S5F2 etc.
        var sysBytes   = message.SystemBytes;

        // One-shot handler to capture the matching reply
        void handler(object? s, SecsMessage reply)
        {
            if (reply.Stream      == message.Stream &&
                reply.Function    == replyFunc      &&
                reply.SystemBytes == sysBytes)
            {
                MessageReceived -= handler;
                tcs.TrySetResult(reply);
            }
        }

        MessageReceived += handler;
        Send(message);

        var delay = timeout ?? TimeSpan.FromSeconds(10);
        if (await Task.WhenAny(tcs.Task, Task.Delay(delay)) != tcs.Task)
        {
            MessageReceived -= handler;
            Log.Warning("Timeout waiting for reply to {Msg}", message.MessageId);
            return null;
        }

        return await tcs.Task;
    }

    // ── Send HSMS control message (no body) ───────────────────────
    private async Task SendControlAsync(byte sType, uint systemBytes)
    {
        // ✅ Use HsmsHeader factory for clean control message creation
        var header = HsmsHeader.ControlMessage(DeviceId, sType, systemBytes);
        var buf    = new byte[14];     // 4 (length) + 10 (header) + 0 (no body)

        // Length = 10 (header only, no body)
        buf[0] = 0; buf[1] = 0; buf[2] = 0; buf[3] = 10;

        // Header bytes at offset 4
        header.EncodeTo(buf, 4);

        if (_stream is not null)
            await _stream.WriteAsync(buf);

        Serilog.Log.Debug("SEND → {Header}", header);
    }

    // ── Unique system bytes (message ID) ─────────────────────────
    public uint NextSystemBytes()
        => Interlocked.Increment(ref _systemBytesCounter);

    // ── Read exact number of bytes ────────────────────────────────
    private async Task ReadExactAsync(byte[] buf, int count, int offset = 0)
    {
        int total = 0;
        while (total < count)
        {
            int n = await _stream!.ReadAsync(buf.AsMemory(offset + total, count - total));
            if (n == 0) throw new IOException("Connection closed by host.");
            total += n;
        }
    }
    public void Dispose()
    {
        _cts.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _listener?.Stop();
    }
}