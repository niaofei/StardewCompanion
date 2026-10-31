namespace StardewCompanion.Protocol;

public sealed class BridgeEnvelope
{
    public string ProtocolVersion { get; set; } = "0.1";
    public string Type { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public long SentAtUtcMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class TilePosition
{
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class GameObservation
{
    public long Revision { get; set; }
    public string SessionId { get; set; } = "";
    public string SaveId { get; set; } = "";
    public long PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public bool WorldReady { get; set; }
    public bool IsHost { get; set; }
    public bool Multiplayer { get; set; }
    public string ControlMode { get; set; } = "farmhand";
    public string TestRunId { get; set; } = "";
    public string Location { get; set; } = "";
    public TilePosition Tile { get; set; } = new TilePosition();
    public float Stamina { get; set; }
    public int Health { get; set; }
    public int TimeOfDay { get; set; }
    public int GameDay { get; set; }
    public string Menu { get; set; } = "";
    // JSON describing the visible popup/menu text and explicit response options.
    public string MenuContext { get; set; } = "{}";
    public bool Paused { get; set; } = true;
    public string ActiveTaskId { get; set; } = "";
    public string[] ConnectedPlayers { get; set; } = new string[0];
    public string DiagnosticDetails { get; set; } = "{}";
    public string WorldView { get; set; } = "{}";
    public CapabilityStatus[] Capabilities {get;set;}=new CapabilityStatus[0];
}

public sealed class TaskCommand
{
    public string TaskId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SaveId { get; set; } = "";
    public long PlayerId { get; set; }
    public long ObservationRevision { get; set; }
    public long ExpiresAtUtcMs { get; set; }
    public string Skill { get; set; } = "";
    public string Arguments { get; set; } = "{}";
    public string EncounterPolicy { get; set; } = "interrupt";
}

public sealed class TaskResult
{
    public string TaskId { get; set; } = "";
    public string Skill { get; set; } = "";
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Details { get; set; } = "";
    public int Completed { get; set; }
    public long ObservationRevision { get; set; }
}
