using System.Text.Json.Nodes;
using System.Text.Json;
using StardewCompanion.Protocol;

namespace StardewCompanion.Core;

public sealed record PlanStep(string Skill, JsonObject Arguments);
public sealed record ShortPlan(string Id, PlanStep[] Steps, string EncounterPolicy = "interrupt")
{
    public static ShortPlan Parse(JsonObject args)
    {
        var id=args["planId"]?.GetValue<string>();
        if(string.IsNullOrWhiteSpace(id)||id.Length>100)throw new InvalidOperationException("planId必须是1到100字符的唯一标识");
        if(args["steps"] is not JsonArray steps||steps.Count is <1 or >8)throw new InvalidOperationException("计划必须包含1到8步");
        var parsed=steps.Select(s=>{
            var skill=s?["skill"]?.GetValue<string>()??"";
            if(!ToolCatalog.Actions.Contains(skill)&&BusinessCapabilities.Find(skill) is not {Query:false})
                throw new InvalidOperationException("计划包含未开放技能："+skill);
            if(s?["arguments"] is not JsonObject arguments)throw new InvalidOperationException("每步必须有明确arguments");
            ToolCatalog.ValidateArguments(skill,arguments);
            return new PlanStep(skill,(JsonObject)arguments.DeepClone());
        }).ToArray();
        if(parsed.Take(parsed.Length-1).Any(s=>s.Skill is "sleep" or "end_day"||s.Skill=="recover"&&s.Arguments["method"]?.GetValue<string>()=="sleep"))throw new InvalidOperationException("睡觉必须是计划最后一步，换日后重新决策");
        if(parsed.Take(parsed.Length-1).Any(s=>s.Skill=="follow_player"))throw new InvalidOperationException("持续跟随必须是计划最后一步，直到取消或玩家离开");
        var encounterPolicy=args["encounterPolicy"]?.GetValue<string>()??"interrupt";
        if(encounterPolicy is not ("fight" or "avoid" or "interrupt"))throw new InvalidOperationException("encounterPolicy只支持fight、avoid、interrupt");
        return new(id,parsed,encounterPolicy);
    }
}

// Owns no strategy: executes exactly the model's ordered steps and stops on the first unmet condition.
public sealed class ShortPlanExecutor(IGameBridge bridge)
{
    public async Task<TaskResult> Step(string skill,object arguments,string taskId,CancellationToken cancellation,string encounterPolicy="interrupt")
    {
        var done=new TaskCompletionSource<TaskResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Received(TaskResult r){if(r.TaskId==taskId&&r.Status is "completed" or "partial" or "failed" or "rejected" or "cancelled")done.TrySetResult(r);}
        bridge.Result+=Received;
        try
        {
            var first=await bridge.Dispatch(skill,arguments,cancellation:cancellation,taskId:taskId,encounterPolicy:encounterPolicy);
            Received(first);
            var timeout=skill is "fish_at" or "fight_enemies" or "mine_travel"?TimeSpan.FromSeconds(900):TimeSpan.FromSeconds(180);
            var result=skill=="follow_player"?await done.Task.WaitAsync(cancellation):await done.Task.WaitAsync(timeout,cancellation);
            var until=Environment.TickCount64+3000;
            while((!bridge.Fresh||bridge.Observation is not {WorldReady:true} state||state.Revision<=result.ObservationRevision)
                &&Environment.TickCount64<until)await Task.Delay(50,cancellation);
            if(!bridge.Fresh||bridge.Observation is not {WorldReady:true} fresh||fresh.Revision<=result.ObservationRevision)
                throw new InvalidOperationException("step_observation_timeout");
            return result;
        }
        finally{bridge.Result-=Received;}
    }
}
