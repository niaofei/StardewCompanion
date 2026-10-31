using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using StardewCompanion.Protocol;

namespace StardewCompanion.Mod;

internal sealed class BridgeClient : IDisposable
{
    private readonly Uri _endpoint;
    private readonly string _token;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentQueue<BridgeEnvelope> _incoming = new();
    private readonly ConcurrentQueue<BridgeEnvelope> _outgoing = new();
    private readonly SemaphoreSlim _available = new(0);
    private long _lastReceived;
    public bool Connected { get; private set; }
    public bool Healthy => Connected && Environment.TickCount64 - Interlocked.Read(ref _lastReceived) < 3500;

    public BridgeClient(string endpoint, string token)
    {
        _endpoint = new Uri(endpoint);
        if (!_endpoint.IsLoopback || _endpoint.Scheme != "ws")
            throw new ArgumentException("Bridge must be a loopback ws:// endpoint.");
        _token = token;
        _ = Task.Run(RunAsync);
    }

    public bool TryRead(out BridgeEnvelope envelope) => _incoming.TryDequeue(out envelope!);

    public void Send(string type, string session, object payload)
    {
        if (!Connected) return;
        _outgoing.Enqueue(new BridgeEnvelope { Type = type, MessageId = Guid.NewGuid().ToString("N"),
            SessionId = session, Payload = JsonSerializer.Serialize(payload) });
        _available.Release();
    }

    private async Task RunAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            try
            {
                socket.Options.SetRequestHeader("Authorization", "Bearer " + _token);
                await socket.ConnectAsync(_endpoint, connection.Token);
                Interlocked.Exchange(ref _lastReceived, Environment.TickCount64);
                Connected = true;
                var receive = ReceiveAsync(socket, connection.Token);
                var send = SendAsync(socket, connection.Token);
                await Task.WhenAny(receive, send);
                connection.Cancel();
                socket.Abort();
                try { await Task.WhenAll(receive, send); } catch (OperationCanceledException) { }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or System.IO.IOException) { }
            finally
            {
                Connected = false;
                while (_outgoing.TryDequeue(out _)) { }
                while (_incoming.TryDequeue(out _)) { }
            }
            try { await Task.Delay(1000, _shutdown.Token); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellation)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > 65536)
                    throw new IOException("Invalid or oversized bridge message.");
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            var envelope = JsonSerializer.Deserialize<BridgeEnvelope>(message.ToArray());
            if (envelope?.ProtocolVersion != "0.1") throw new IOException("Protocol mismatch.");
            Interlocked.Exchange(ref _lastReceived, Environment.TickCount64);
            if (envelope.Type != "heartbeat") _incoming.Enqueue(envelope);
        }
    }

    private async Task SendAsync(ClientWebSocket socket, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            await _available.WaitAsync(cancellation);
            while (_outgoing.TryDequeue(out var envelope))
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation);
            }
        }
    }

    public void Dispose() { _shutdown.Cancel(); }
}
