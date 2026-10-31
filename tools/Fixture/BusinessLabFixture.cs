using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace StardewCompanion.Fixture;

public sealed partial class FixtureMod
{
    private string _labCommand="";
    private void BusinessLabTick()
    {
        if(Environment.GetEnvironmentVariable("SC_BUSINESS_LAB")!="1"||!Context.IsWorldReady||!Context.IsMainPlayer)return;
        var file=Environment.GetEnvironmentVariable("SC_BUSINESS_LAB_CONTROL");
        if(string.IsNullOrEmpty(file)||!File.Exists(file))return;
        using var json=JsonDocument.Parse(File.ReadAllText(file));var request=json.RootElement;
        var id=request.GetProperty("id").GetString()!;if(id==_labCommand)return;_labCommand=id;
        var scene=request.GetProperty("scene").GetString()!;
        try
        {
            Game1.exitActiveMenu();
            var p=Game1.player;p.MaxItems=36;while(p.Items.Count<36)p.Items.Add(null);
            for(var i=0;i<p.Items.Count;i++)if(p.Items[i] is not Tool)p.Items[i]=null;
            p.Stamina=p.MaxStamina;p.health=p.maxHealth;p.Money=100000;Game1.timeOfDay=900;
            var farm=Game1.getFarm();
            for(var x=50;x<=76;x++)for(var y=17;y<=40;y++){
                farm.objects.Remove(new(x,y));farm.terrainFeatures.Remove(new(x,y));
            }
            void Give(string item,int count)=>p.addItemToInventoryBool(ItemRegistry.Create(item,count));
            foreach(var item in new[]{("(O)472",30),("(O)368",30),("(O)388",500),("(O)390",300),("(O)382",100),("(O)24",30),("(O)168",10)})
                Give(item.Item1,item.Item2);
            foreach(var recipe in Game1.content.Load<Dictionary<string,string>>("Data/CraftingRecipes").Keys)p.craftingRecipes[recipe]=0;
            foreach(var recipe in Game1.content.Load<Dictionary<string,string>>("Data/CookingRecipes").Keys)p.cookingRecipes[recipe]=0;
            if(scene=="trees"){
                farm.terrainFeatures[new(67,20)]=new Tree("1",5);farm.terrainFeatures[new(70,20)]=new Tree("2",5);
                farm.terrainFeatures[new(73,20)]=new Tree("3",5);
            }
            if(scene=="production"){
                farm.objects[new(68,20)]=ItemRegistry.Create<StardewValley.Object>("(BC)12");
                farm.objects[new(69,20)]=ItemRegistry.Create<StardewValley.Object>("(BC)15");
                Give("(O)309",10);Give("(O)628",3);Give("(O)599",3);
                Give("(BC)599",3);
            }
            if(scene=="commerce"){Give("(O)334",20);Give("(O)335",20);}
            if(scene=="recovery"){p.Stamina=20;Give("(O)194",10);}
            Game1.warpFarmer("Farm",64,15,false);
            if(scene=="awareness"){
                p.MaxItems=12;while(p.Items.Count>12)p.Items.RemoveAt(p.Items.Count-1);
                for(var i=0;i<12;i++)if(p.Items[i] is not Tool)p.Items[i]=ItemRegistry.Create("(O)388",1);
                if(!p.mailbox.Contains("spring_2_1"))p.mailbox.Add("spring_2_1");
                var town=Game1.getLocationFromName("Town");var flower=ItemRegistry.Create<StardewValley.Object>("(O)18");
                flower.isSpawnedObject.Value=true;town.objects[new Vector2(51,31)]=flower;
                Game1.warpFarmer("Town",51,30,false);
            }
            if(scene=="awareness_passout"){
                Game1.timeOfDay=2600;
                p.startToPassOut(); // Exercise the real animation, native pass-out settlement and day change.
            }
            File.WriteAllText(file+".result",JsonSerializer.Serialize(new{id,scene,status="ready",fixtureOnly=true,runId=p.farmName.Value,
                warning="Prepared isolated lab conditions, not autonomous opening evidence"}));
            Monitor.Log("Business lab scene prepared: "+scene,LogLevel.Info);
        }
        catch(Exception ex){File.WriteAllText(file+".result",JsonSerializer.Serialize(new{id,scene,status="failed",reason=ex.Message}));}
    }
}
