using System.Text.Json;
using System.Text.Json.Nodes;

namespace StardewCompanion.Core;

public sealed record WorkPlanRecord(string Id,int Revision,string Goal,string Basis,string[] Assumptions,string[] SuccessCriteria,
    string[] Pending,string State,string UpdatedAt,JsonNode? Batch,JsonNode? Evidence);

public sealed partial class CompanionRuntime
{
    public string Mode=>_scope==""?"autonomous":_memory.Get(_scope,"operating_mode")??"autonomous";
    public WorkPlanRecord[] Plans=>_scope==""?Array.Empty<WorkPlanRecord>():JsonSerializer.Deserialize<WorkPlanRecord[]>(_memory.Get(_scope,"work_plans")??"[]")??Array.Empty<WorkPlanRecord>();
    public async Task SetMode(string mode)
    {
        if(mode is not ("autonomous" or "plan_only"))throw new InvalidOperationException("mode必须是autonomous或plan_only");
        if(_scope=="")throw new InvalidOperationException("先连接游戏角色");
        if(mode=="plan_only")await Pause();
        _memory.Set(_scope,"operating_mode",mode);
        Trace("mode_changed",new{mode});
        if(mode=="autonomous")await Resume();else {_planningPaused=false;WakeDecision();}
        Changed?.Invoke();
    }
    private WorkPlanRecord SaveWorkPlan(JsonObject args,JsonNode? batch=null,bool replaceObjective=true)
    {
        var id=args["planId"]?.GetValue<string>()??throw new InvalidOperationException("缺少planId");
        if(id.Length is <1 or >100)throw new InvalidOperationException("planId长度不正确");
        var records=Plans;var old=records.FirstOrDefault(r=>r.Id==id);
        string[] Strings(string key,string[] fallback){
            if(!args.ContainsKey(key))return fallback;
            if(args[key] is not JsonArray values||values.Any(v=>v?.GetValueKind()!=JsonValueKind.String))
                throw new InvalidOperationException(key+"必须是JSON字符串数组");
            return values.Select(v=>v!.GetValue<string>()).ToArray();
        }
        var record=new WorkPlanRecord(id,(old?.Revision??0)+1,args["goal"]?.GetValue<string>()??old?.Goal??"执行明确批次",
            args["basis"]?.GetValue<string>()??old?.Basis??"",Strings("assumptions",old?.Assumptions??Array.Empty<string>()),
            Strings("successCriteria",old?.SuccessCriteria??Array.Empty<string>()),Strings("pending",old?.Pending??Array.Empty<string>()),
            "planned",DateTimeOffset.UtcNow.ToString("o"),batch?.DeepClone()??old?.Batch,old?.Evidence);
        _memory.Set(_scope,"work_plans",JsonSerializer.Serialize(records.Where(r=>r.Id!=id).Append(record).ToArray()));
        UpdateWorkingPlan(args,replaceObjective);
        Trace("planning_saved",record);
        return record;
    }
    private void RecordPlanEvidence(string scope,string id,object evidence,string state)
    {
        var plans=JsonSerializer.Deserialize<WorkPlanRecord[]>(_memory.Get(scope,"work_plans")??"[]")??Array.Empty<WorkPlanRecord>();
        _memory.Set(scope,"work_plans",JsonSerializer.Serialize(plans.Select(p=>p.Id==id?p with{
            State=state,UpdatedAt=DateTimeOffset.UtcNow.ToString("o"),Evidence=JsonSerializer.SerializeToNode(evidence)}:p).ToArray()));
    }
}
