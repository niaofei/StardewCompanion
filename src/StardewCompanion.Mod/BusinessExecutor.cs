using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewCompanion.Protocol;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Objects;

namespace StardewCompanion.Mod;

internal sealed record WorkUnit(string Skill,object Arguments,bool Optional=false);
internal sealed partial class BusinessExecutor
{
    private readonly string _skill;
    private readonly JsonElement _args;
    private readonly Action<GridPoint> _interact;
    private IEnumerator<WorkUnit>? _workflow;
    private WorkUnit? _unit;
    private RoutineExecutor? _child;
    private NativeWorkExecutor? _native;
    private string? _finishedStatus;
    private int _childCount;
    private readonly Dictionary<string,int> _initial=Inventory();
    internal readonly List<object> Stages=new();
    internal readonly List<object> Skipped=new();
    internal int Completed {get;private set;}
    internal string Reason {get;private set;}="";
    internal string Phase {get;private set;}="prepare";
    internal Dictionary<string,int> CollectedItems {get;}=new();
    internal object[] AffectedTiles=>_tiles.ToArray();
    private readonly List<object> _tiles=new();
    internal BusinessExecutor(string skill,string args,Action<GridPoint> interact)
    {_skill=skill;using var doc=JsonDocument.Parse(args);_args=doc.RootElement.Clone();_interact=interact;}
    private string Text(string k,string fallback="")=>_args.TryGetProperty(k,out var v)&&v.ValueKind!=JsonValueKind.Null?v.GetString()!:fallback;
    private int Num(string k,int fallback=0)=>_args.TryGetProperty(k,out var v)?v.GetInt32():fallback;
    private bool Flag(string k)=>_args.TryGetProperty(k,out var v)&&v.GetBoolean();
    private string Map=>Text("location",Game1.currentLocation.NameOrUniqueName);
    private Dictionary<string,object?> Region()=>new(){["location"]=Map,["x"]=Num("x"),["y"]=Num("y"),["width"]=Num("width"),["height"]=Num("height")};
    private IEnumerable<GridPoint> Cells()=>Enumerable.Range(Num("x"),Num("width")).SelectMany(x=>Enumerable.Range(Num("y"),Num("height")).Select(y=>new GridPoint(x,y)));
    internal static Dictionary<string,int> Inventory()=>Game1.player.Items.Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
    internal static int Count(string id)=>Game1.player.Items.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack);
    internal string Tick(AiInputSimulator input)
    {
        Capture();
        _workflow??=Build().GetEnumerator();
        if(_unit==null){
            if(!_workflow.MoveNext()){Reason=Skipped.Count>0?"business_completed_with_skips":"business_completed";return Skipped.Count>0?"partial":"completed";}
            _unit=_workflow.Current;_childCount=0;
            if(_unit.Skill.StartsWith("$"))_native=new(_unit.Skill[1..],JsonSerializer.Serialize(_unit.Arguments),_interact);
            else _child=new(_unit.Skill,JsonSerializer.Serialize(_unit.Arguments),_interact);
        }
        var status=_finishedStatus??(_native?.Tick(input)??_child!.Tick(input));
        Phase=_unit.Skill+":"+(_native?.Phase??_child!.Phase);
        if(status=="running")return status;
        if(_finishedStatus==null){
        _childCount=_native?.Completed??_child!.Completed;
        var recordedReason=_native?.Reason??_child!.Reason;
        Stages.Add(new{skill=_unit.Skill,status,reason=recordedReason,completed=_childCount,details=_native?.Details??_child?.BusinessDetails});
        if(_child!=null)_tiles.AddRange(_child.AffectedTiles);
        Completed+=_childCount;
        _finishedStatus=status;
        }
        var reason=_native?.Reason??_child!.Reason;
        var optional=_unit.Optional;
        if(_native?.Cleanup()==false||_child?.Cleanup()==false){Phase="native_menu_cleanup_pending";return "running";}
        _child=null;_native=null;var done=_unit;_unit=null;_finishedStatus=null;
        if(status!="completed"){
            if(optional){Skipped.Add(new{skill=done.Skill,reason});return "running";}
            Reason=reason;return Completed>0?"partial":"failed";
        }
        return "running";
    }
    internal void Capture(){foreach(var (id,count) in Inventory()){var gain=Math.Max(0,count-_initial.GetValueOrDefault(id));if(gain>CollectedItems.GetValueOrDefault(id))CollectedItems[id]=gain;}}
    internal bool Cleanup(){Capture();return (_child?.Cleanup()??true)&&(_native?.Cleanup()??true);}
    private WorkUnit Native(string action,object args,bool optional=false)=>new("$"+action,args,optional);
    private IEnumerable<WorkUnit> Build()
    {
        switch(_skill){
            case "cultivate_plot":case "harvest_and_replant":case "maintain_plot":
                foreach(var u in Cultivate())yield return u;break;
            case "gather_resource":
                foreach(var u in Gather())yield return u;break;
            case "travel":
                if(Text("method")=="walk")yield return new("go_to",new{location=Map,x=Num("x"),y=Num("y")});
                else yield return Native("travel",_args);break;
            case "forage_route":
                foreach(var stop in _args.GetProperty("stops").EnumerateArray()){
                    yield return new("forage_area",stop,true);
                    if(stop.GetProperty("shake").GetBoolean()||stop.GetProperty("digSpots").GetBoolean())yield return Native("forage_extras",stop,true);
                }break;
            case "craft_items":case "cook_recipes":case "craft_and_place":case "install_irrigation":
                foreach(var u in Produce())yield return u;break;
            case "plant_trees":
                foreach(var tile in _args.GetProperty("tiles").EnumerateArray())yield return Native("plant_tree",new{location=Map,itemId=Text("itemId"),x=tile.GetProperty("x").GetInt32(),y=tile.GetProperty("y").GetInt32()},true);
                break;
            case "collect_outputs":case "service_machines":
                foreach(var target in _args.GetProperty("targets").EnumerateArray())yield return Native("machine",new{location=Map,mode=Text("mode","collect"),target},true);
                break;
            case "organize_storage":case "prepare_loadout":
                foreach(var u in Storage())yield return u;break;
            case "recover":
                if(Text("method")=="sleep")yield return new("sleep",new{});
                else yield return Native("recover",_args);break;
            case "end_day":
                if(_args.GetProperty("storageRules").GetArrayLength()>0)yield return new("organize_storage",new{rules=_args.GetProperty("storageRules"),_bindings=_args.GetProperty("_bindings")});
                if(_args.GetProperty("shippingItems").GetArrayLength()>0)yield return new("sell_items",new{channel="shipping",location="Farm",shopId=(string?)null,items=_args.GetProperty("shippingItems")});
                if(Flag("sleep"))yield return new("sleep",new{});break;
            default:
                foreach(var u in Domain())yield return u;break;
        }
    }
    private IEnumerable<WorkUnit> Cultivate()
    {
        if(Game1.currentLocation.NameOrUniqueName!=Map)yield return new("go_to",new{location=Map,x=Num("x"),y=Num("y")});
        var region=Region();var partial=Flag("allowPartial");
        if(_skill=="harvest_and_replant"||Flag("harvest"))yield return new("harvest_area",region,true);
        if(Flag("removeDead"))yield return Native("remove_dead",region,true);
        var seed=Text("seedId",Text("replantSeedId"));var desired=Num("plantCount",Num("width")*Num("height"));var needed=0;
        if(seed!=""){
            var seedItem=ItemRegistry.Create(seed) as StardewValley.Object;
            if(seedItem==null||seedItem.Category!=StardewValley.Object.SeedsCategory||seedItem.IsWildTreeSapling()||seedItem.IsFruitTreeSapling())throw new InvalidOperationException("unsupported_crop_seed:"+seed);
            if(Game1.cropData.TryGetValue(ItemRegistry.GetDataOrErrorItem(seed).ItemId,out var cropData)&&!Game1.currentLocation.SeedsIgnoreSeasonsHere()&&!cropData.Seasons.Contains(Game1.GetSeasonForLocation(Game1.currentLocation)))throw new InvalidOperationException("seed_not_in_current_season:"+seed);
            var occupied=Cells().Count(p=>Game1.currentLocation.terrainFeatures.TryGetValue(new(p.X,p.Y),out var f)&&f is HoeDirt {crop:not null});
            needed=Math.Max(0,desired-occupied);
            if(Count(seed)<needed&&!partial)throw new InvalidOperationException($"seed_missing:required={needed};available={Count(seed)}");
        }
        var clear=Text("clearPolicy","none");
        if(clear!="none"){
            var a=Region();a["targetType"]=clear=="debris_and_wild_trees"?"all":"debris";a["removeStumps"]=true;
            yield return new("clear_area",a,partial);
        }
        if(seed!=""&&needed>0){
            yield return new("till_area",region,partial);
            if(Text("fertilizerId")!="")yield return Native("fertilize",new{region, itemId=Text("fertilizerId")},partial);
            var a=Region();a["seedId"]=seed;a["maxTargets"]=Math.Min(needed,Count(seed));yield return new("plant_area",a,partial);
        }
        if(Text("waterPolicy")=="water_now")yield return new("water_area",region,partial);
        else Stages.Add(new{skill="water_area",status="skipped_by_policy",reason="skip_today"});
    }
    private IEnumerable<WorkUnit> Gather()
    {
        if(Game1.currentLocation.NameOrUniqueName!=Map)yield return new("go_to",new{location=Map,x=Num("x"),y=Num("y")});
        var item=Text("itemId");var before=Count(item);var sources=_args.GetProperty("sources").EnumerateArray().Select(s=>s.GetString()!).ToHashSet();
        var attempted=new HashSet<GridPoint>();
        while(Count(item)-before<Num("count")){
            var l=Game1.currentLocation;
            var targets=Cells().Where(p=>!attempted.Contains(p)).Select(p=>{
                l.objects.TryGetValue(new(p.X,p.Y),out var obj);l.terrainFeatures.TryGetValue(new(p.X,p.Y),out var f);
                var type=obj?.Name=="Twig"&&sources.Contains("twigs")?"twig":obj?.Name=="Stone"&&sources.Contains("stones")?"stone":
                    obj?.Name=="Weeds"&&sources.Contains("weeds")?"weeds":f is Grass&&sources.Contains("grass")?"grass":
                    f is Tree tree&&!tree.tapped.Value&&tree.growthStage.Value>=5&&(tree.stump.Value?sources.Contains("small_stumps"):sources.Contains("wild_trees"))?(tree.stump.Value?"stump":"tree"):null;
                var yield=type=="tree"?12:1;
                return new{p,type,score=(p.Distance(Navigation.Current)+1)/(float)yield};
            }).Where(t=>t.type!=null).OrderBy(t=>t.score).ToArray();
            var target=targets.FirstOrDefault(t=>new Navigation().BestAdjacent(t.p)!=null);
            if(target==null)break;
            attempted.Add(target.p);
            yield return new("clear_area",new{location=Map,x=target.p.X,y=target.p.Y,width=1,height=1,targetType=target.type,
                removeStumps=Flag("removeStumps"),maxTargets=1},true);
        }
        var gained=Count(item)-before;
        if(gained<Num("count")){Skipped.Add(new{reason="resource_target_shortfall",itemId=item,requested=Num("count"),actual=gained});}
    }
    private IEnumerable<WorkUnit> Produce()
    {
        var recipeName=Text("recipe");var cooking=_skill=="cook_recipes";
        var craftCount=Num("count",_args.TryGetProperty("tiles",out var requestedTiles)?requestedTiles.GetArrayLength():_args.TryGetProperty("placements",out var requestedPlacements)?requestedPlacements.GetArrayLength():1);
        if(recipeName!=""){
            var known=cooking?Game1.player.cookingRecipes:Game1.player.craftingRecipes;
            if(!known.ContainsKey(recipeName))throw new InvalidOperationException("recipe_not_unlocked:"+recipeName);
            var recipe=new CraftingRecipe(recipeName,cooking);
            if(_skill is "craft_and_place" or "install_irrigation"&&recipe.GetItemData(useFirst:true).QualifiedItemId!=Text("itemId"))throw new InvalidOperationException("recipe_output_item_mismatch");
            if(_skill is "craft_and_place" or "install_irrigation")craftCount=(craftCount+recipe.numberProducedPerCraft-1)/recipe.numberProducedPerCraft;
            foreach(var unit in IngredientSources(recipe.recipeList,craftCount,cooking))yield return unit;
        }
        if(cooking){var home=Utility.getHomeOfFarmer(Game1.player);var entry=home.getEntryLocation();if(Game1.currentLocation!=home)yield return new("go_to",new{location=home.NameOrUniqueName,x=entry.X,y=entry.Y});}
        var sources=_args.TryGetProperty("sourceChests",out var chests)?chests:_args;
        yield return Native("prepare_recipe",new{recipe=recipeName,count=craftCount,sourceChests=sources,cooking,ingredientQuality=Num("ingredientQuality")});
        if(_skill is "craft_items" or "cook_recipes")yield return Native("craft",new{recipe=recipeName,count=craftCount,cooking,ingredientQuality=Num("ingredientQuality")});
        else{
            if(_skill=="install_irrigation"&&(ItemRegistry.Create(Text("itemId")) is not StardewValley.Object sprinkler||!sprinkler.IsSprinkler()))throw new InvalidOperationException("irrigation_item_must_be_sprinkler");
            var placements=_args.TryGetProperty("tiles",out var t)?t:_args.GetProperty("placements");
            if(recipeName!="")yield return Native("craft",new{recipe=recipeName,count=craftCount,cooking=false,ingredientQuality=0});
            foreach(var tile in placements.EnumerateArray())yield return Native("place",new{location=Map,itemId=Text("itemId"),target=tile},true);
            if(_skill=="install_irrigation"&&Text("waterPolicy")=="water_now"){
                var location=Game1.currentLocation;
                var covered=placements.EnumerateArray().SelectMany(p=>location.objects.TryGetValue(new(p.GetProperty("x").GetInt32(),p.GetProperty("y").GetInt32()),out var sprinkler)?sprinkler.GetSprinklerTiles():new List<Vector2>()).Distinct().ToArray();
                foreach(var tile in covered)yield return new("water_area",new{location=Map,x=(int)tile.X,y=(int)tile.Y,width=1,height=1,autoRefill=true});
            }
        }
    }
    private IEnumerable<WorkUnit> IngredientSources(IReadOnlyDictionary<string,int> requirements,int crafts,bool cooking)
    {
        if(!_args.TryGetProperty("sourceChests",out var sources))yield break;
        foreach(var source in sources.EnumerateArray()){
            if(requirements.All(r=>Game1.player.Items.Where(i=>CraftingRecipe.ItemMatchesForCrafting(i,r.Key)&&(!cooking||i.Quality==Num("ingredientQuality"))).Sum(i=>i.Stack)>=r.Value*crafts))yield break;
            var name=source.GetString();var binding=_args.GetProperty("_bindings").EnumerateArray().FirstOrDefault(b=>b.GetProperty("Name").GetString()==name);
            if(binding.ValueKind==JsonValueKind.Undefined)throw new InvalidOperationException("chest_not_authorized:"+name);
            yield return Native("approach",new{location=binding.GetProperty("Location").GetString(),x=binding.GetProperty("X").GetInt32(),y=binding.GetProperty("Y").GetInt32()});
            if(!Game1.currentLocation.objects.TryGetValue(new(binding.GetProperty("X").GetInt32(),binding.GetProperty("Y").GetInt32()),out var obj)||obj is not Chest chest||!obj.modData.TryGetValue(WorldView.ChestKey,out var id)||id!=binding.GetProperty("Id").GetString())throw new InvalidOperationException("chest_binding_invalid:"+name);
            var allowed=binding.GetProperty("AllowedIds").EnumerateArray().Select(v=>v.GetString()).Except(binding.GetProperty("ProtectedIds").EnumerateArray().Select(v=>v.GetString())).ToHashSet();
            foreach(var ingredient in requirements){
                var needed=ingredient.Value*crafts-Game1.player.Items.Where(i=>CraftingRecipe.ItemMatchesForCrafting(i,ingredient.Key)&&(!cooking||i.Quality==Num("ingredientQuality"))).Sum(i=>i.Stack);
                foreach(var item in chest.GetItemsForPlayer(Game1.player.UniqueMultiplayerID).Where(i=>i!=null&&allowed.Contains(i.QualifiedItemId)&&CraftingRecipe.ItemMatchesForCrafting(i,ingredient.Key)&&(!cooking||i.Quality==Num("ingredientQuality"))).ToArray()){
                    if(needed<=0)break;var amount=Math.Min(needed,item.Stack);
                    yield return Transfer("withdraw_items",new{chest=name,itemId=item.QualifiedItemId,count=amount,quality=item.Quality});needed-=amount;
                }
            }
        }
    }
    private IEnumerable<WorkUnit> Storage()
    {
        if(_skill=="prepare_loadout"){
            foreach(var entry in _args.GetProperty("deposit").EnumerateArray())yield return Transfer("deposit_items",entry);
            foreach(var entry in _args.GetProperty("withdraw").EnumerateArray())yield return Transfer("withdraw_items",entry);
        }else foreach(var rule in _args.GetProperty("rules").EnumerateArray()){
            var source=rule.GetProperty("source").GetString();var destination=rule.GetProperty("destination").GetString();
            var id=rule.GetProperty("itemId").GetString()!;var count=rule.GetProperty("count").GetInt32();var quality=rule.GetProperty("quality").GetInt32();
            if(source!="inventory")yield return Transfer("withdraw_items",new{chest=source,itemId=id,count,quality});
            count=Math.Min(count,Math.Max(0,Game1.player.Items.Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack)-rule.GetProperty("retain").GetInt32()));
            if(destination!="inventory"&&count>0)yield return Transfer("deposit_items",new{chest=destination,itemId=id,count,quality});
        }
    }
}
