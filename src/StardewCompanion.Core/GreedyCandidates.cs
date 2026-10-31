using System.Text.Json.Nodes;
using StardewCompanion.Protocol;

namespace StardewCompanion.Core;

public static class GreedyCandidates
{
    public static object[] Read(GameObservation s)
    {
        var world=JsonNode.Parse(s.WorldView);
        var candidates=new List<(int Priority,int Distance,object Value)>();
        foreach(var tile in (world?["soil"] as JsonArray??new JsonArray()).OfType<JsonObject>())
        {
            var x=tile["x"]!.GetValue<int>();var y=tile["y"]!.GetValue<int>();
            var distance=Math.Abs(x-s.Tile.X)+Math.Abs(y-s.Tile.Y);
            var dead=tile["dead"]?.GetValue<bool>()??false;
            if(!dead&&tile["needsWater"]?.GetValue<bool>()==true)
                candidates.Add((1,distance,new{skill="water_area",priority=1,location=s.Location,x,y,estimatedDistance=distance,estimatedStaminaPerTile=2,reason="保护已有作物；按相邻区域合并处理"}));
            if(!dead&&tile["ready"]?.GetValue<bool>()==true)
                candidates.Add((2,distance,new{skill="harvest_area",priority=2,location=s.Location,x,y,estimatedDistance=distance,estimatedStaminaPerTile=0,reason="当前成熟产出已确认"}));
        }
        foreach(var tile in (world?["forage"] as JsonArray??new JsonArray()).OfType<JsonObject>())
        {
            var x=tile["x"]!.GetValue<int>();var y=tile["y"]!.GetValue<int>();var distance=Math.Abs(x-s.Tile.X)+Math.Abs(y-s.Tile.Y);
            candidates.Add((3,distance,new{skill="forage_area",priority=3,location=s.Location,x,y,estimatedDistance=distance,estimatedStaminaPerTile=0,reason="当前地图实际可见采集物；可达性仍由执行器验证"}));
        }
        return candidates.OrderBy(c=>c.Priority).ThenBy(c=>c.Distance).Take(24).Select(c=>c.Value).ToArray();
    }
}
