using System.Text.Json;

namespace StardewCompanion.Core;

public sealed record DecisionEvent(long Sequence,DateTimeOffset At,string Scope,string Session,string DecisionId,string Kind,JsonElement Data);

public sealed class DecisionJournal(string path)
{
    private readonly object _gate=new();
    private readonly Queue<DecisionEvent> _events=new();
    private long _sequence=LastSequence(path);
    private static long LastSequence(string file)
    {
        if(!File.Exists(file))return 0;
        long last=0;
        foreach(var line in File.ReadLines(file)){
            try{using var document=JsonDocument.Parse(line);if(document.RootElement.TryGetProperty("Sequence",out var sequence))last=Math.Max(last,sequence.GetInt64());}
            catch(JsonException){} // A partially written final line is not a committed event.
        }
        return last;
    }
    public long Latest { get { lock(_gate)return _sequence; } }
    public DecisionEvent[] Read(long after=0,int limit=200)
    { lock(_gate)return _events.Where(e=>e.Sequence>after).Take(Math.Clamp(limit,1,1000)).ToArray(); }
    public void Write(string scope,string session,string decision,string kind,object data)
    {
        var payload=JsonSerializer.SerializeToElement(data);
        lock(_gate)
        {
            var entry=new DecisionEvent(++_sequence,DateTimeOffset.UtcNow,scope,session,decision,kind,payload);
            _events.Enqueue(entry);while(_events.Count>1000)_events.Dequeue();
            try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.AppendAllText(path,JsonSerializer.Serialize(entry)+Environment.NewLine); }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
