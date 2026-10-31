using System.Text.Json;
using System.Text.Json.Nodes;

namespace StardewCompanion.Core;

public sealed partial class CompanionRuntime
{
    private object ReadContextHistory(JsonObject arguments)
    {
        var source=arguments["source"]?.GetValue<string>()??"";
        var id=arguments["id"]?.GetValue<string>()??"";
        var offset=arguments["offset"]?.GetValue<int>()??0;
        var limit=arguments["limit"]?.GetValue<int>()??4;
        if(offset<0||limit is <1 or >8)throw new InvalidOperationException("历史查询offset必须>=0，limit必须为1到8");
        if(source=="diagnostics")return _memory.ReadDiagnosticArchive(_scope,id,offset,limit);
        if(source=="plans"){
            var plans=Plans.OrderByDescending(p=>p.UpdatedAt).Where(p=>id==""||p.Id==id).ToArray();
            if(id=="")return new{source,knowledge="historical_requires_recheck",total=plans.Length,offset,
                entries=plans.Skip(offset).Take(limit).Select(p=>new{p.Id,p.Goal,p.State,p.Pending,p.UpdatedAt})};
            var plan=plans.SingleOrDefault()??throw new InvalidOperationException("plan_not_found_in_current_save");
            var evidence=plan.Evidence;
            var results=DecisionContextCompression.Get(evidence,"results") as JsonArray??new();
            return new{source,knowledge="historical_requires_recheck",plan=DecisionContextCompression.Plan(JsonSerializer.SerializeToNode(plan)),plan.UpdatedAt,
                totalResults=results.Count,offset,
                requestedSteps=plan.Batch is JsonArray batch?new JsonArray(batch.Skip(offset).Take(limit).Select(v=>v?.DeepClone()).ToArray()):null,
                status=DecisionContextCompression.Text(evidence,"status"),reason=DecisionContextCompression.Text(evidence,"reason"),
                results=results.Skip(offset).Take(limit).Select(r=>DecisionContextCompression.Get(r,"receipt") is {} receipt
                    ?(JsonNode)DecisionContextCompression.Receipt(receipt):r?.DeepClone()),
                unexecuted=DecisionContextCompression.Get(evidence,"unexecuted")};
        }
        if(source=="memory"){
            var history=_memory.ReadHistory(_scope,id,id==""?offset:0,limit);var memories=history.Entries;
            if(id=="")return new{source,total=history.Total,offset,entries=memories.Select(m=>new{
                m.Id,m.Kind,m.State,m.Source,m.Day,preview=m.Text[..Math.Min(160,m.Text.Length)],previewOnly=m.Text.Length>160})};
            JsonNode? MemoryText(MemoryEntry memory){
                var text=memory.Kind=="agreement"?JsonValue.Create(memory.Text):DecisionContextCompression.Decode(JsonValue.Create(memory.Text));
                if(memory.Kind=="project"&&text is JsonObject project){
                    foreach(var field in new[]{"VerifiedResults","UnverifiedResults"}){
                        if(project[field] is not JsonArray list)continue;
                        project[field+"Count"]=list.Count;
                        project[field]=new JsonArray(list.Skip(offset).Take(limit).Select(DecisionContextCompression.Decode).ToArray());
                    }
                }
                return text;
            }
            return new{source,total=history.Total,offset,entries=memories.Select(m=>new{
                m.Id,m.Kind,m.State,m.Source,m.Day,text=MemoryText(m)})};
        }
        if(source=="receipts"){
            var stored=Plans.SelectMany(p=>(DecisionContextCompression.Get(p.Evidence,"results") as JsonArray??new())
                .Select(r=>DecisionContextCompression.Get(r,"receipt")));
            var recent=_recentResults.Select(r=>JsonSerializer.SerializeToNode(r));
            var receipt=stored.Concat(recent).LastOrDefault(r=>DecisionContextCompression.Text(r,"taskId")==id)
                ??throw new InvalidOperationException("receipt_not_found_in_current_save");
            var detail=DecisionContextCompression.Decode(DecisionContextCompression.Get(receipt,"details"));
            var affected=DecisionContextCompression.Get(detail,"affectedTiles") as JsonArray??new();
            var drops=DecisionContextCompression.Get(detail,"remainingDrops") as JsonArray??new();
            return new{source,knowledge="historical_requires_recheck",receipt=DecisionContextCompression.Receipt(receipt),offset,
                affectedTileCount=affected.Count,affectedTiles=affected.Skip(offset).Take(limit),
                remainingDropCount=drops.Count,remainingDrops=drops.Skip(offset).Take(limit)};
        }
        throw new InvalidOperationException("历史source仅支持plans、memory、receipts");
    }
}
