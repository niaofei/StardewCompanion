using System.Text.Json.Nodes;
using StardewCompanion.Protocol;

namespace StardewCompanion.Core;

public static class PermissionPolicy
{
    public static JsonObject Authorize(string skill,JsonObject args,IReadOnlyList<ChestBinding> chests,GameObservation state)
    {
        if(skill=="mine_travel")throw new InvalidOperationException("skill_disabled:mine_travel");
        ToolCatalog.ValidateArguments(skill,args);
        if(BusinessCapabilities.Find(skill) is {} business){
            if(!ControlModePolicy.CanControl(state))throw new InvalidOperationException("capability_not_available:"+skill);
            using var doc=System.Text.Json.JsonDocument.Parse(args.ToJsonString());ContractValidation.Validate(business,doc.RootElement);
            var bound=(JsonObject)args.DeepClone();bound["_bindings"]=System.Text.Json.JsonSerializer.SerializeToNode(chests);return bound;
        }
        if(!ToolCatalog.Actions.Contains(skill))throw new InvalidOperationException("未注册的技能");
        var copy=(JsonObject)args.DeepClone();
        if(skill is "deposit_items" or "withdraw_items")
        {
            var name=copy["chest"]?.GetValue<string>();var id=copy["itemId"]?.GetValue<string>();var count=copy["count"]?.GetValue<int>()??0;
            var chest=chests.SingleOrDefault(c=>c.Name==name)??throw new InvalidOperationException("工作箱尚未由玩家授权");
            if(id==null||!chest.AllowedIds.Contains(id)||chest.ProtectedIds.Contains(id)||id.StartsWith("(T)")||id.StartsWith("(W)")||id is "(O)434" or "(O)326"||count is <1 or >9999)throw new InvalidOperationException("物品不在允许清单或受到保护");
            copy.Remove("chest");copy["chestId"]=chest.Id;copy["location"]=chest.Location;copy["x"]=chest.X;copy["y"]=chest.Y;copy["allowedIds"]=new JsonArray(chest.AllowedIds.Except(chest.ProtectedIds).Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray());
        }
        if(skill.EndsWith("_area"))
        {
            foreach(var key in new[]{"x","y","width","height"})if(copy[key] is not JsonValue)throw new InvalidOperationException("区域参数缺失");
            if(copy["width"]!.GetValue<int>() is <1 or >120||copy["height"]!.GetValue<int>() is <1 or >120)throw new InvalidOperationException("区域尺寸超出限制");
        }
        return copy;
    }
}
