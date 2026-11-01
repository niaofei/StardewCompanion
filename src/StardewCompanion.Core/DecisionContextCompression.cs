using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StardewCompanion.Core;

public sealed record CompressedDecisionContext(JsonObject Context,int OriginalCharacters,int Characters,int BudgetCharacters,bool OverBudget);

// Model-facing projections only. Original plans, receipts, player text and logs
// remain intact in storage. No model call is used to produce these summaries.
public static class DecisionContextCompression
{
    public const int DefaultBudgetCharacters=32000;
    public static readonly JsonSerializerOptions ReadableJson=new(){Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    public static string Serialize(object? value)=>JsonSerializer.Serialize(value,ReadableJson);
    public static JsonNode? Get(JsonNode? node,string name)=>node is JsonObject obj
        ?obj.FirstOrDefault(p=>p.Key.Equals(name,StringComparison.OrdinalIgnoreCase)).Value:null;
    public static string Text(JsonNode? node,string name)=>Get(node,name)?.ToString()??"";
    public static JsonNode? Decode(JsonNode? value)
    {
        if(value is JsonValue v&&v.TryGetValue<string>(out var text)&&text.TrimStart() is var trimmed&&
            (trimmed.StartsWith('{')||trimmed.StartsWith('[')))
        {try{return JsonNode.Parse(text);}catch(JsonException){}}
        return value?.DeepClone();
    }
    private static JsonArray Array(JsonNode? value)=>value as JsonArray??new();
    private static JsonObject Pick(JsonNode? source,params string[] fields)
    {
        var result=new JsonObject();
        foreach(var field in fields)if(Get(source,field) is {} value)result[field]=value.DeepClone();
        return result;
    }
    public static JsonObject Plan(JsonNode? source,bool includeSteps=false)
    {
        var result=Pick(source,"id","revision","goal","state","basis","assumptions","successCriteria","pending");
        if(Text(source,"state")!="planned")result["pending"]=new JsonArray();
        if(includeSteps&&Get(source,"batch") is {} batch)result["steps"]=batch.DeepClone();
        // A completed batch is evidence for its steps, never proof that its goal
        // or model-written pending items are complete.
        result["pendingMeaning"]=Text(source,"state")=="planned"?"active_plan_items_require_recheck":"previous_plan_pending_retired_until_replanned";
        return result;
    }
    private static JsonNode? Evidence(JsonNode? source)
    {
        if(source is JsonObject obj){
            var result=new JsonObject();
            foreach(var pair in obj){
                if(pair.Key is "affectedTiles" or "remainingDrops" or "chunks"){
                    if(pair.Value is JsonArray list)result[pair.Key]=new JsonObject{
                        ["count"]=list.Count,["sample"]=new JsonArray(list.Take(4).Select(v=>v?.DeepClone()).ToArray()),["sampleOnly"]=list.Count>4};
                    else result[pair.Key]=pair.Value?.DeepClone();
                }else result[pair.Key]=Evidence(pair.Key.Equals("details",StringComparison.OrdinalIgnoreCase)?Decode(pair.Value):pair.Value);
            }
            return result;
        }
        if(source is JsonArray array)return new JsonArray(array.Select(Evidence).ToArray());
        return source?.DeepClone();
    }
    public static JsonObject Receipt(JsonNode? source)
    {
        var result=Pick(source,"taskId","skill","status","reason","completed","observationRevision");
        if(Decode(Get(source,"details")) is JsonObject detail){
            foreach(var name in new[]{"requestedTarget","playerStart","playerEnd","location","collectedItems","actualCost","completedUnit","businessDetails"})
                if(Get(detail,name) is {} value)result[name]=Evidence(value);
            if(result["requestedTarget"] is JsonObject target){target.Remove("_bindings");target.Remove("allowedIds");}
            foreach(var name in new[]{"affectedTiles","remainingDrops"})
                if(Get(detail,name) is JsonArray array)result[name]=new JsonObject{["count"]=array.Count,["sample"]=new JsonArray(array.Take(4).Select(v=>v?.DeepClone()).ToArray()),["sampleOnly"]=array.Count>4};
        }
        return result;
    }
    public static JsonNode? ToolResult(string name,object? result)
    {
        var node=JsonSerializer.SerializeToNode(result);
        if(name=="save_plan")return Plan(node);
        if(Get(node,"taskId")!=null){
            if(name is "observe" or "inspect_target" or "inspect_targets" or "inspect_route" or "inspect_area" or "survey_area" or "read_mailbox"){
                var query=Pick(node,"taskId","status","reason","observationRevision");query["data"]=CompactQuery(Decode(Get(node,"details")));return query;
            }
            return Receipt(node);
        }
        return node;
    }
    private static void Table(JsonObject obj,string field)
    {
        if(Get(obj,field) is not JsonArray array||array.Count<8||array.Any(v=>v is not JsonObject))return;
        var columns=array.OfType<JsonObject>().SelectMany(o=>o.Select(p=>p.Key)).Distinct().ToArray();
        var missing=new JsonArray();
        var rows=new JsonArray(array.OfType<JsonObject>().Select((o,row)=>(JsonNode)new JsonArray(columns.Select((c,column)=>{
            if(o.TryGetPropertyValue(c,out var v))return v?.DeepClone();
            missing.Add(new JsonArray(row,column));return null;
        }).ToArray())).ToArray());
        var table=new JsonObject{["columns"]=new JsonArray(columns.Select(c=>(JsonNode?)JsonValue.Create(c)).ToArray()),["rows"]=rows};
        if(missing.Count>0)table["missing"]=missing;
        obj[field]=table;
    }
    private static JsonNode? CompactQuery(JsonNode? data)
    {
        if(data is JsonObject obj){
            foreach(var key in obj.Select(p=>p.Key).ToArray()){
                obj[key]=CompactQuery(obj[key]?.DeepClone());Table(obj,key);
            }
        }else if(data is JsonArray array){
            var holder=new JsonObject{["items"]=new JsonArray(array.Select(v=>CompactQuery(v?.DeepClone())).ToArray())};
            Table(holder,"items");return holder["items"]?.DeepClone();
        }
        return data;
    }
    private static JsonNode? Observation(JsonNode? source)
    {
        var result=source?.DeepClone();
        if(Get(result,"world") is JsonObject world){
            world.Remove("gameEvents");
            foreach(var field in new[]{"soil","inventory","forage","looseDrops","naturalObstacles","chairs","npcs"})Table(world,field);
            if(Get(world,"decisionFacts") is JsonObject facts)foreach(var field in new[]{"trees","resources"})Table(facts,field);
        }
        return result;
    }
    public static CompressedDecisionContext Build(JsonObject source,int budgetCharacters=DefaultBudgetCharacters)
    {
        var original=source.ToJsonString().Length;
        var result=Pick(source,"instruction","operatingMode","paused","pendingQuestion","chests","seasonPlan","seasonMaintenance","actionFeedback","gameEvents");
        result["playerInstructions"]=new JsonArray(Array(Get(source,"playerInstructions")).Select(v=>v?.GetValue<string>()??"")
            .Distinct(StringComparer.Ordinal).Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray());
        result["observation"]=Observation(Get(source,"observation"));
        result["capabilities"]=new JsonArray(Array(Get(source,"capabilities")).Select(c=>(JsonNode?)Pick(c,"name","state","reason","validated")).ToArray());
        var plans=Array(Get(source,"workPlans")).OfType<JsonObject>().OrderByDescending(p=>Text(p,"updatedAt")).ToArray();
        var latest=plans.FirstOrDefault();
        var goals=System.Array.Empty<JsonObject>();
        var completed=System.Array.Empty<JsonObject>();
        result["planning"]=new JsonObject{
            ["current"]=latest==null?null:Plan(latest,Text(latest,"state")=="planned"),
            ["otherOpenGoals"]=new JsonArray(goals.Take(8).Select(p=>{
                var goal=Pick(p,"id","goal","state");goal["lastFailure"]=Text(Get(p,"evidence"),"reason");return (JsonNode)goal;
            }).ToArray()),
            ["recentCompletedBatches"]=new JsonArray(completed.Take(3).Select(p=>(JsonNode)Pick(p,"id","goal","state")).ToArray()),
            ["confirmedCompletedBatches"]=new JsonArray(completed.Select(p=>(JsonNode)Pick(p,"id","goal","updatedAt")).DistinctBy(p=>Text(p,"goal")).Take(32).ToArray()),
            ["archivedPlanCount"]=plans.Length,["olderOpenGoals"]=Math.Max(0,goals.Length-8),
            ["historyTool"]="read_context_history(source=plans,id=planId or empty,offset,limit)",
            ["completionMeaning"]="batch_completed仅表示本批执行结束；跨日待办和模型提出的目标仍需新观察核验"};
        // Only planned records remain active; stopping/revising a plan retires its old pending list.
        var pending=new List<JsonObject>();
        if(Text(latest,"state")=="planned")foreach(var item in Array(Get(latest,"pending")))pending.Add(new JsonObject{["planId"]=Text(latest,"id"),["text"]=item?.ToString()??"",["state"]="planned"});
        foreach(var plan in goals)foreach(var item in Array(Get(plan,"pending")))pending.Add(new JsonObject{["planId"]=Text(plan,"id"),["text"]=item?.ToString()??"",["state"]="planned"});
        pending=pending.Where(p=>Text(p,"text")!="").DistinctBy(p=>Text(p,"text")).ToList();
        result["pending"]=new JsonArray(pending.Take(24).Select(p=>(JsonNode)p).ToArray());
        result["dailyPlan"]=new JsonObject{["next"]=new JsonArray(pending.Take(24).Select(p=>(JsonNode?)JsonValue.Create(Text(p,"text"))).ToArray()),["done"]=new JsonArray()};
        if(result["planning"]!["current"] is JsonObject currentPlan)currentPlan.Remove("pending");
        var last=Get(source,"lastPlan");
        var receipts=Array(Get(last,"results")).Select(r=>Get(r,"receipt"))
            .Concat(Array(Get(source,"recentTaskResults"))).Where(r=>r!=null&&Text(r,"taskId")!=""&&Text(r,"status")=="completed")
            .GroupBy(r=>Text(r,"taskId")).Select(g=>g.Last()).TakeLast(6).Select(Receipt).ToArray();
        result["feedback"]=new JsonObject{["receipts"]=new JsonArray(receipts.Select(r=>(JsonNode)r).ToArray())};
        result["dailyPlan"]=new JsonObject{
            ["goal"]=latest!=null&&Text(latest,"state")=="planned"?JsonValue.Create(Text(latest,"goal")):null,
            ["next"]=new JsonArray(pending.Take(24).Select(p=>(JsonNode?)JsonValue.Create(Text(p,"text"))).ToArray()),
            ["done"]=new JsonArray(receipts.Select(r=>(JsonNode)r.DeepClone()).ToArray())};
        result.Remove("planning");result.Remove("pending");result.Remove("feedback");
        var day=Text(Get(source,"observation"),"gameDay");
        result["queryFacts"]=new JsonArray(Array(Get(source,"queryFacts")).Where(q=>Text(q,"skill")!="observe"&&Text(q,"observedDay")==day).TakeLast(3).Select(q=>{
            var fact=Pick(q,"skill","observedDay","location");fact["result"]=ToolResult(Text(q,"skill"),Get(q,"result"));return (JsonNode)fact;
        }).ToArray());
        result["greedyCandidates"]=new JsonArray(Array(Get(source,"greedyCandidates")).Take(6).Select(v=>v?.DeepClone()).ToArray());
        result["projects"]=new JsonArray(Array(Get(source,"projects")).Select(p=>{
            var project=Pick(p,"id","goal","nextStep","status","pending","lastCheckedSession");
            // ProjectNote uses Remaining rather than Pending in some versions.
            foreach(var pair in (p as JsonObject??new()).Where(p=>!p.Key.Equals("VerifiedResults",StringComparison.OrdinalIgnoreCase)&&!p.Key.Equals("UnverifiedResults",StringComparison.OrdinalIgnoreCase)))
                if(Get(project,pair.Key)==null)project[pair.Key]=pair.Value?.DeepClone();
            project["verifiedEvidenceCount"]=Array(Get(p,"verifiedResults")).Count;
            project["recentVerifiedResults"]=new JsonArray(Array(Get(p,"verifiedResults")).TakeLast(3).Select(Decode).ToArray());
            project["unverifiedEvidenceCount"]=Array(Get(p,"unverifiedResults")).Count;return (JsonNode)project;
        }).ToArray());
        var projectedProjects=Array(Get(source,"projects")).Select(p=>Text(p,"id")).Where(id=>id!="").ToHashSet(StringComparer.Ordinal);
        var projectedChests=Array(Get(source,"chests")).Select(p=>Text(p,"id")).Where(id=>id!="").ToHashSet(StringComparer.Ordinal);
        result["memories"]=new JsonArray(Array(Get(source,"memories")).Where(m=>{
            var kind=Text(m,"kind");var id=Text(Decode(Get(m,"text")),"id");
            return !(kind=="project"&&projectedProjects.Contains(id))&&!(kind=="place"&&projectedChests.Contains(id));
        }).Select(m=>{
            var memory=Pick(m,"id","kind","state","source","day");memory["text"]=Text(m,"kind")=="agreement"?Get(m,"text")?.DeepClone():Decode(Get(m,"text"));return (JsonNode)memory;
        }).ToArray());
        result["contextPolicy"]=new JsonObject{["historyStoredLocally"]=true,["tableMeaning"]="columns+rows为无损表格；missing中的[行,列]从0开始，表示原观察未提供字段，与实际null不同",["omitted"]=new JsonArray(),["softBudgetCharacters"]=budgetCharacters,["overBudget"]=false};
        var omitted=(JsonArray)result["contextPolicy"]!["omitted"]!;
        // These are optional historical aids. Never trim player requirements,
        // current observations/dialogue, the current plan, or its latest receipt.
        foreach(var field in new[]{"capabilities","greedyCandidates","queryFacts","memories"}){
            var owner=result;
            if(owner[field] is not JsonArray items)continue;
            while(result.ToJsonString(ReadableJson).Length>budgetCharacters&&items.Count>0){
                var index=field=="memories"?items.Select((m,i)=>(m,i)).LastOrDefault(p=>Text(p.m,"kind") is not ("agreement" or "project")).i:items.Count-1;
                if(field=="memories"&&Text(items[index],"kind") is "agreement" or "project")break;
                omitted.Add(new JsonObject{["section"]=field,["id"]=Text(items[index],"id")});items.RemoveAt(index);
            }
        }
        var chars=result.ToJsonString(ReadableJson).Length;
        result["contextPolicy"]!["overBudget"]=chars>budgetCharacters;
        return new(result,original,result.ToJsonString(ReadableJson).Length,budgetCharacters,chars>budgetCharacters);
    }
}
