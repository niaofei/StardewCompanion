using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using StardewCompanion.Protocol;

namespace StardewCompanion.Fixture;

public sealed partial class FixtureMod
{
    private string _lastLogicCommand="";
    private void LogicTick()
    {
        var input=Environment.GetEnvironmentVariable("SC_LOGIC_FIXTURE_COMMAND");
        var output=Environment.GetEnvironmentVariable("SC_LOGIC_FIXTURE_RESULT");
        if(string.IsNullOrEmpty(input)||string.IsNullOrEmpty(output)||!File.Exists(input)
            ||!StardewModdingAPI.Context.IsWorldReady||StardewModdingAPI.Context.IsMainPlayer
            ||Game1.player.farmName.Value!="SCM0"
            ||Game1.uniqueIDForThisGame.ToString()!=Environment.GetEnvironmentVariable("SC_M0_EXPECTED_SAVE"))return;
        try
        {
            using var json=JsonDocument.Parse(File.ReadAllText(input));
            var id=json.RootElement.GetProperty("id").GetString()!;
            if(id==_lastLogicCommand)return;
            var operation=json.RootElement.GetProperty("operation").GetString();
            if(operation=="prepare"&&Game1.currentLocation.NameOrUniqueName!="Farm")return;
            _lastLogicCommand=id;
            object details;
            if(operation=="prepare")
            {
                var l=Game1.currentLocation;
                // Reset only the disposable test farmer's sample inventory between independent runs.
                // Earlier attempts can leave several crop qualities filling every slot; preserve all tools.
                for(var slot=0;slot<Game1.player.Items.Count;slot++)if(Game1.player.Items[slot] is not Tool)Game1.player.Items[slot]=null;
                Game1.player.addItemToInventoryBool(ItemRegistry.Create("(O)388",5));
                Game1.player.addItemToInventoryBool(ItemRegistry.Create("(O)24",1));
                // Explicit disposable-SCM0 setup: open a small work patch around the cabin.
                // Preserve placed containers, crops, buildings and all non-natural objects.
                for(var x=50;x<=63;x++)for(var y=16;y<=28;y++)
                {
                    var tile=new Vector2(x,y);
                    if(l.objects.TryGetValue(tile,out var natural)&&natural is not Chest
                        &&natural.Name is "Stone" or "Weeds" or "Twig")l.objects.Remove(tile);
                    if(l.terrainFeatures.TryGetValue(tile,out var feature)&&feature is Tree or Grass)l.terrainFeatures.Remove(tile);
                }
                var free=new List<Vector2>();
                var start=new GridPoint(Game1.player.TilePoint.X,Game1.player.TilePoint.Y);
                bool Passable(GridPoint p)=>p.X>=0&&p.Y>=0&&p.X<l.Map.Layers[0].LayerWidth&&p.Y<l.Map.Layers[0].LayerHeight
                    &&!l.isCollidingPosition(new Rectangle(p.X*64+8,p.Y*64+16,48,32),Game1.viewport,true,0,false,Game1.player,true,skipCollisionEffects:true);
                var visited=new HashSet<GridPoint>{start};var queue=new Queue<GridPoint>();queue.Enqueue(start);
                while(queue.Count>0&&visited.Count<2000)
                {
                    var p=queue.Dequeue();foreach(var next in p.Neighbors())if(!visited.Contains(next)&&Passable(next)){visited.Add(next);queue.Enqueue(next);}
                }
                foreach(var point in visited.OrderBy(p=>p.Distance(start)))
                {
                    var p=new Vector2(point.X,point.Y);
                    if(point.Distance(start)<2||point.Neighbors().Count(Passable)<3||free.Any(other=>Vector2.DistanceSquared(other,p)<9))continue;
                    if(!l.objects.ContainsKey(p)&&!l.terrainFeatures.ContainsKey(p)&&!l.buildings.Any(b=>b.occupiesTile(p)))free.Add(p);
                    if(free.Count==6)break;
                }
                if(free.Count<6)throw new InvalidOperationException("No safe fixture space");
                var crop=new HoeDirt(0,l){crop=new Crop("472",(int)free[0].X,(int)free[0].Y,l)};crop.crop.growCompletely();
                var wheat=new HoeDirt(0,l){crop=new Crop("483",(int)free[1].X,(int)free[1].Y,l)};wheat.crop.growCompletely();
                if(wheat.crop.GetHarvestMethod()!=StardewValley.GameData.Crops.HarvestMethod.Scythe)throw new InvalidOperationException("Scythe fixture data mismatch");
                l.terrainFeatures.Add(free[0],crop);l.terrainFeatures.Add(free[1],wheat);
                var chest=new Chest(true,free[2]);chest.Items.Add(ItemRegistry.Create("(O)388",200));l.objects.Add(free[2],chest);
                var shipping=new Chest(true,free[3],"248");
                if(shipping.SpecialChestType!=Chest.SpecialChestTypes.MiniShippingBin)throw new InvalidOperationException("Shipping fixture data mismatch");
                l.objects.Add(free[3],shipping);
                var forage=(StardewValley.Object)ItemRegistry.Create("(O)16");forage.IsSpawnedObject=true;forage.TileLocation=free[4];l.objects.Add(free[4],forage);
                var dry=new HoeDirt(0,l){crop=new Crop("472",(int)free[5].X,(int)free[5].Y,l)};l.terrainFeatures.Add(free[5],dry);
                var can=Game1.player.Items.OfType<WateringCan>().First();can.WaterLeft=0;
                details=new{crop=Tile(free[0]),scytheCrop=Tile(free[1]),chest=Tile(free[2]),shipping=Tile(free[3]),forage=Tile(free[4]),dryCrop=Tile(free[5]),setup="SCM0 only: natural obstacles cleared in a small fixture patch; this is not an agent ability"};
            }
            else if(operation=="fill_inventory")
            {
                foreach(var quality in new[]{0,1,2,4})
                    if(!Game1.player.Items.Any(i=>i?.QualifiedItemId=="(O)24"&&i.Quality==quality))
                    {
                        var empty=Game1.player.Items.IndexOf(null);
                        if(empty<0)throw new InvalidOperationException("Not enough slots for all crop qualities");
                        Game1.player.Items[empty]=ItemRegistry.Create("(O)24",1,quality);
                    }
                for(var slot=0;slot<Game1.player.Items.Count;slot++)if(Game1.player.Items[slot]==null)Game1.player.Items[slot]=ItemRegistry.Create("(O)390",999);
                details=new{full=Game1.player.Items.All(i=>i!=null),parsnips=Game1.player.Items.Where(i=>i?.QualifiedItemId=="(O)24").Sum(i=>i.Stack)};
            }
            else if(operation=="clear_fixture_filler")
            {
                for(var slot=0;slot<Game1.player.Items.Count;slot++)if(Game1.player.Items[slot]?.QualifiedItemId=="(O)390"&&Game1.player.Items[slot].Stack==999)Game1.player.Items[slot]=null;
                details=new{cleared=true};
            }
            else throw new InvalidOperationException("Unsupported fixture command");
            File.WriteAllText(output,JsonSerializer.Serialize(new{id,status="completed",saveId=Game1.uniqueIDForThisGame.ToString(),details}));
        }
        catch(Exception ex)
        {File.WriteAllText(output,JsonSerializer.Serialize(new{id=_lastLogicCommand,status="failed",reason=ex.Message}));}
    }
    private static object Tile(Vector2 point)=>new{x=(int)point.X,y=(int)point.Y};
}
