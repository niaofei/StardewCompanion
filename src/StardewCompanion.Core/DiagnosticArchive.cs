namespace StardewCompanion.Core;

public sealed partial class MemoryStore
{
    private bool _archiveReady;
    private void EnsureArchive()
    {
        if(_archiveReady)return;
        using var command=Command("CREATE TABLE IF NOT EXISTS decision_archive(id INTEGER PRIMARY KEY,scope TEXT NOT NULL,session TEXT NOT NULL,kind TEXT NOT NULL,decision TEXT NOT NULL,payload TEXT NOT NULL,at TEXT NOT NULL); CREATE INDEX IF NOT EXISTS decision_archive_scope ON decision_archive(scope,id);");
        command.ExecuteNonQuery();_archiveReady=true;
    }
    public void ArchiveDiagnostic(string scope,string session,string kind,string decision,string payload)
    {
        lock(_gate){EnsureArchive();using var command=Command("INSERT INTO decision_archive(scope,session,kind,decision,payload,at) VALUES($s,$session,$kind,$decision,$payload,$at)",
            ("$s",scope),("$session",session),("$kind",kind),("$decision",decision),("$payload",payload),("$at",DateTimeOffset.UtcNow.ToString("o")));command.ExecuteNonQuery();}
    }
    public object ReadDiagnosticArchive(string scope,string id,int offset,int limit)
    {
        lock(_gate){EnsureArchive();
            using var command=Command("SELECT id,session,kind,decision,payload,at FROM decision_archive WHERE scope=$s AND ($id='' OR CAST(id AS TEXT)=$id) ORDER BY id DESC LIMIT $limit OFFSET $offset",("$s",scope),("$id",id),("$limit",limit),("$offset",offset));
            using var reader=command.ExecuteReader();var rows=new List<object>();
            while(reader.Read())rows.Add(new{id=reader.GetInt64(0),session=reader.GetString(1),kind=reader.GetString(2),decisionId=reader.GetString(3),
                payload=id==""?null:System.Text.Json.Nodes.JsonNode.Parse(reader.GetString(4)),at=reader.GetString(5)});
            return new{source="diagnostics",offset,entries=rows};
        }
    }
}
