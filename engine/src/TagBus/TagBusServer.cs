using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace FactoryForge.TagBus;

/// <summary>
/// Engine-side tag bus. Speaks the protocol in docs/tag-bus.md, so the
/// unchanged Python sidecar and all its drivers connect to this exactly as they
/// do to harness/engine_stub.py.
///
/// Only one sidecar may connect at a time: two drivers writing the same output
/// tag is a bug, not a feature.
/// </summary>
public partial class TagBusServer : Node
{
    public const int ProtocolVersion = 0;
    public const string EngineId = "factoryforge-engine/0.1.0";

    [Export] public int Port { get; set; } = 7411;
    [Export] public int TickMs { get; set; } = 10;

    private TcpServer _listener = new();
    private WebSocketPeer? _client;
    private readonly Dictionary<string, object> _lastSent = new();

    public TagTable Tags { get; set; } = new();
    public string SceneName { get; set; } = "untitled";
    public int Epoch { get; private set; }
    public long TickCount { get; private set; }
    public bool HasClient => _client is not null &&
                             _client.GetReadyState() == WebSocketPeer.State.Open;

    public override void _Ready()
    {
        var err = _listener.Listen((ushort)Port, "127.0.0.1");
        if (err != Error.Ok)
            GD.PushError($"tag bus could not listen on port {Port}: {err}");
        else
            GD.Print($"tag bus listening on ws://127.0.0.1:{Port}/tagbus");
    }

    public override void _Process(double delta)
    {
        AcceptPending();
        PumpClient();
    }

    private void AcceptPending()
    {
        if (!_listener.IsConnectionAvailable()) return;
        var stream = _listener.TakeConnection();
        if (stream is null) return;

        if (HasClient)
        {
            // Refuse rather than silently multiplexing.
            stream.DisconnectFromHost();
            GD.Print("tag bus: refused a second sidecar");
            return;
        }

        var peer = new WebSocketPeer();
        if (peer.AcceptStream(stream) != Error.Ok)
        {
            GD.PushWarning("tag bus: websocket handshake failed");
            return;
        }
        _client = peer;
    }

    private void PumpClient()
    {
        if (_client is null) return;
        _client.Poll();

        switch (_client.GetReadyState())
        {
            case WebSocketPeer.State.Open:
                break;
            case WebSocketPeer.State.Closed:
                GD.Print("tag bus: sidecar disconnected");
                _client = null;
                return;
            default:
                return; // still connecting or closing
        }

        if (!_greeted)
        {
            _greeted = true;
            Send(Hello());
            SendDescribe();
        }

        while (_client.GetAvailablePacketCount() > 0)
        {
            var text = _client.GetPacket().GetStringFromUtf8();
            try
            {
                var msg = JsonNode.Parse(text)?.AsObject();
                if (msg is not null) Handle(msg);
            }
            catch (Exception e)
            {
                GD.PushWarning($"tag bus: bad message: {e.Message}");
            }
        }
    }

    private bool _greeted;

    // --- outgoing ---

    private static JsonObject Hello() => new()
    {
        ["t"] = "hello",
        ["protocol"] = ProtocolVersion,
        ["engine"] = EngineId,
    };

    /// <summary>Publish the current tag set and bump the epoch.</summary>
    public void SendDescribe()
    {
        Epoch++;
        _lastSent.Clear();
        foreach (var tag in Tags) _lastSent[tag.Id] = Tags.Visible(tag.Id);

        Send(new JsonObject
        {
            ["t"] = "describe",
            ["scene"] = SceneName,
            ["epoch"] = Epoch,
            ["tags"] = Tags.ToJson(),
        });
    }

    /// <summary>Send only the input tags whose values changed. Nothing is sent
    /// when nothing changed — an idle scene produces zero traffic.</summary>
    public void SendUpdates()
    {
        if (!HasClient) return;
        JsonObject? changed = null;
        foreach (var tag in Tags.ByKind(TagKind.Input))
        {
            var current = Tags.Visible(tag.Id);
            if (_lastSent.TryGetValue(tag.Id, out var prev) && Equals(prev, current))
                continue;
            _lastSent[tag.Id] = current;
            changed ??= new JsonObject();
            changed[tag.Id] = JsonValue.Create(current);
        }
        if (changed is null) return;

        Send(new JsonObject
        {
            ["t"] = "update",
            ["tick"] = TickCount,
            ["values"] = changed,
        });
    }

    public void Status(string level, string code, string message) => Send(new JsonObject
    {
        ["t"] = "status",
        ["level"] = level,
        ["code"] = code,
        ["message"] = message,
    });

    private void Send(JsonObject msg)
    {
        if (_client is null || _client.GetReadyState() != WebSocketPeer.State.Open) return;
        _client.SendText(msg.ToJsonString());
    }

    // --- incoming ---

    private void Handle(JsonObject msg)
    {
        switch (msg["t"]?.GetValue<string>())
        {
            case "write":
                // A write in flight across a scene change would otherwise land
                // on whatever tag inherited that id.
                if (msg["epoch"]?.GetValue<int>() != Epoch) return;
                ApplyWrites(msg["values"]?.AsObject());
                break;

            case "force":
                if (msg["epoch"]?.GetValue<int>() != Epoch) return;
                if (msg["values"]?.AsObject() is { } forces)
                    foreach (var (id, node) in forces)
                        if (Tags.Contains(id) && node is not null)
                            Tags.Force(id, ToClr(node));
                if (msg["clear"]?.AsArray() is { } clears)
                    foreach (var node in clears)
                        if (node is not null && Tags.Contains(node.GetValue<string>()))
                            Tags.ClearForce(node.GetValue<string>());
                break;

            case "status":
                GD.Print($"sidecar: {msg["message"]}");
                break;
        }
    }

    private void ApplyWrites(JsonObject? values)
    {
        if (values is null) return;
        List<string>? unknown = null;
        foreach (var (id, node) in values)
        {
            var tag = Tags.Get(id);
            if (tag is null) { (unknown ??= new()).Add(id); continue; }
            if (tag.Kind != TagKind.Output)
            {
                Status("warn", "wrong_kind",
                    $"{id} is a simulator-owned input; use force to override it");
                continue;
            }
            if (node is not null) Tags.Set(id, ToClr(node));
        }
        if (unknown is not null)
            Status("warn", "unknown_tags", $"ignored unknown tags: {string.Join(", ", unknown)}");
    }

    private static object ToClr(JsonNode node)
    {
        var v = node.AsValue();
        if (v.TryGetValue<bool>(out var b)) return b;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return d;
        return v.ToString();
    }

    public void CountTick() => TickCount++;
}
