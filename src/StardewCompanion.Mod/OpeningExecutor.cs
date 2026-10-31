using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewCompanion.Protocol;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace StardewCompanion.Mod;

// Native opening actions are initially restricted to a separately opted-in, newly created solo test save.
internal sealed class OpeningExecutor
{
    internal static readonly string[] Skills={"collect_starter_seeds","clear_area","till_area","plant_area","craft_chest","place_chest"};
    private readonly string _skill;
    private readonly JsonElement _args;
    private readonly Action<GridPoint> _interact;
    private readonly Navigation _nav=new();
    private readonly HashSet<GridPoint> _done=new();
    private readonly Dictionary<string,int> _initial=Inventory();
    private GridPoint? _target;
    private long _wait;
    private long _pickupDeadline;
    private int _hits;
    private float _beforeHealth;
    private int _seedBefore;
    private int _chestBefore;
    private int _woodBefore;
    private CraftingPage? _crafting;
    private bool _crafted;
    private bool _disposed;
    private readonly HashSet<Debris> _oldDebris=Game1.currentLocation.debris.ToHashSet();
    internal int Completed {get;private set;}
    internal string Phase {get;private set;}="starting";
    internal string Reason {get;private set;}="";
    internal Dictionary<string,int> CollectedItems {get;}=new();
    internal object[] AffectedTiles=>_done.Select(p=>(object)new{x=p.X,y=p.Y}).ToArray();
    private static (string Save,int X,int Y)? _plot;
    internal OpeningExecutor(string skill,string arguments,Action<GridPoint> interact)
    {
        _skill=skill;using var doc=JsonDocument.Parse(arguments);_args=doc.RootElement.Clone();_interact=interact;
        if(skill.EndsWith("_area"))
        {
            if(!WorldView.Supported(Game1.getLocationFromName(Text("location")))||Num("width") is <1 or >32||Num("height") is <1 or >32||Num("x")<0||Num("y")<0)throw new JsonException("Opening work must stay in a bounded Farm region");
            if(Num("maxTargets",1024) is <1 or >1024)throw new JsonException("Invalid target limit");
        }

    }
    private int Num(string key,int fallback=0)=>_args.TryGetProperty(key,out var value)?value.GetInt32():fallback;
    private string Text(string key)=>_args.GetProperty(key).GetString()!;
    private string SeedId=>_args.TryGetProperty("seedId",out var id)?id.GetString()!:"(O)472";
    private string Map=>_args.TryGetProperty("location",out var location)?location.GetString()!:"Farm";
    private bool Trees=>_args.TryGetProperty("includeTrees",out var v)&&v.GetBoolean();
    private bool RemoveStumps=>!_args.TryGetProperty("removeStumps",out var v)||v.GetBoolean();
    private string End(string reason){Reason=reason;Capture();return Completed>0?"partial":"failed";}
    private static Dictionary<string,int> Inventory()=>Game1.player.Items.Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
    private static int Count(string id)=>Game1.player.Items.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack);
    internal void Capture()
    {
        if(!StardewModdingAPI.Context.IsWorldReady)return;
        foreach(var (id,count) in Inventory())
        {var gain=Math.Max(0,count-_initial.GetValueOrDefault(id));if(gain>CollectedItems.GetValueOrDefault(id))CollectedItems[id]=gain;}
    }
    internal static bool Natural(StardewValley.Object obj)=>obj is not Chest && obj.Name is "Stone" or "Twig" or "Weeds";
    private static HoeDirt? Soil(GridPoint p)=>Game1.currentLocation.terrainFeatures.TryGetValue(new(p.X,p.Y),out var f)?f as HoeDirt:null;
    private bool Obstacle(GridPoint p)
    {
        var l=Game1.currentLocation;var tile=new Vector2(p.X,p.Y);
        var type=_args.TryGetProperty("targetType",out var filter)?filter.GetString():"all";
        if(type is not ("all" or "debris" or "twig" or "stone" or "weeds" or "grass" or "tree" or "stump"))throw new JsonException("Unknown clear targetType");
        if(type is "tree" or "stump")return l.terrainFeatures.TryGetValue(tile,out var tf)&&tf is Tree t&&!t.tapped.Value&&(type=="stump"?t.stump.Value:t.growthStage.Value>=5&&(!t.stump.Value||RemoveStumps));
        if(type is not ("all" or "debris")){
            if(type=="grass")return l.terrainFeatures.TryGetValue(tile,out var terrain)&&terrain is Grass;
            return l.objects.TryGetValue(tile,out var resource)&&resource.Name==(type=="twig"?"Twig":type=="stone"?"Stone":"Weeds");
        }
        return (l.objects.TryGetValue(tile,out var obj)&&Natural(obj)) ||
            (l.terrainFeatures.TryGetValue(tile,out var f)&&(f is Grass||(Trees||type=="all")&&type!="debris"&&f is Tree tree&&!tree.tapped.Value&&(!tree.stump.Value||RemoveStumps)));
    }
    private static bool Tilled(GridPoint p)=>Soil(p)!=null;
    private static bool CanTill(GridPoint p)
    {
        var l=Game1.currentLocation;var tile=new Vector2(p.X,p.Y);
        return l.doesTileHaveProperty(p.X,p.Y,"Diggable","Back")!=null&&!l.isWaterTile(p.X,p.Y)
            &&!l.objects.ContainsKey(tile)&&!l.terrainFeatures.ContainsKey(tile)&&Navigation.Passable(p);
    }
    private IEnumerable<GridPoint> Targets()
    {
        for(var x=Num("x");x<Num("x")+Num("width");x++)for(var y=Num("y");y<Num("y")+Num("height");y++)
        {
            var p=new GridPoint(x,y);if(_done.Contains(p))continue;
            if(_skill=="clear_area"&&Obstacle(p))yield return p;
            if(_skill=="till_area"&&CanTill(p))yield return p;
            if(_skill=="plant_area"&&Soil(p) is {crop:null} soil&&soil.canPlantThisSeedHere(ItemRegistry.GetDataOrErrorItem(SeedId).ItemId))yield return p;
        }
    }
    internal string Tick(AiInputSimulator input)
    {
        if(!SoloTestMode.Enabled||_disposed)return End("solo_test_identity_required");
        Capture();
        if(_skill=="craft_chest")return Craft();
        // Opening the native gift already grants the seeds, then shows an acknowledgement.
        // Finish that dialogue through its normal click path before applying the menu guard.
        if(_skill=="collect_starter_seeds"&&Count("(O)472")==15&&Game1.activeClickableMenu is DialogueBox {isQuestion:false} acknowledgement)
        {
            acknowledgement.receiveLeftClick(acknowledgement.x+64,acknowledgement.y+64);
            Phase="acknowledge_starter_gift";return "running";
        }
        if(Game1.activeClickableMenu!=null)return End("unexpected_menu:"+Game1.activeClickableMenu.GetType().Name);
        if(Game1.eventUp||!Game1.player.CanMove||Game1.player.UsingTool){_nav.Suspend();Phase="waiting_for_native_game";return "running";}
        if(_skill=="collect_starter_seeds")return Starter(input);
        if(Game1.currentLocation.NameOrUniqueName!=Map)return End("opening_requires_farm");
        if(_skill=="place_chest")return Place(input);
        var now=Environment.TickCount64;
        if(_target is {} target&&_wait!=0)
        {
            if(now<_wait)return "running";
            if(_skill=="clear_area"&&Game1.currentLocation.terrainFeatures.TryGetValue(new(target.X,target.Y),out var fallingFeature)&&fallingFeature is Tree fallingTree&&fallingTree.falling.Value)
            {Phase="waiting_for_native_tree_fall";return "running";}
            if(_skill=="clear_area"&&Obstacle(target))
            {
                if(++_hits>40)return End("opening_target_did_not_clear");
                var health=TreeHealth(target);
                if(_hits>3&&health==_beforeHealth&&health>0)return End("opening_tool_not_effective");
                _beforeHealth=health;_wait=0;
            }
            else
            {
                var verified=_skill=="clear_area"?!Obstacle(target):_skill=="till_area"?Tilled(target):Soil(target)?.crop?.netSeedIndex.Value==ItemRegistry.GetDataOrErrorItem(SeedId).ItemId&&Count(SeedId)==_seedBefore-1;
                if(!verified)return End("opening_action_not_verified");
                if(_skill=="clear_area")
                {
                    var pickup=CollectDrops(input,target);if(pickup!="done")return pickup;
                }
                Completed++;_done.Add(target);_target=null;_wait=0;_hits=0;_nav.Reset();
            }
        }
        if(Completed>=Num("maxTargets",1024)){Reason="opening_target_limit_reached";return "completed";}
        if(_target==null)
        {
            var candidates=Targets().OrderBy(p=>p.Distance(Navigation.Current)).ToArray();
            if(candidates.Length==0){
                var cells=Enumerable.Range(Num("x"),Num("width")).SelectMany(x=>Enumerable.Range(Num("y"),Num("height")).Select(y=>new GridPoint(x,y))).ToArray();
                if(_skill=="till_area"&&cells.Any(p=>Soil(p)==null)){
                    Reason="tilling_precondition_blocked:区域包含障碍或不可耕格，先清障或缩小范围";return Completed>0?"partial":"failed";
                }
                if(_skill=="plant_area"){
                    if(cells.Any(p=>Soil(p)==null)){Reason="requires_tilled_empty_soil:先clear_area清障，再till_area锄地；plant_area不锄地";return Completed>0?"partial":"failed";}
                }
                Reason=Completed==0?"already_satisfied_no_action":"opening_observed_targets_finished";return "completed";
            }
            _target=candidates.FirstOrDefault(p=>_nav.BestAdjacent(p)!=null);
            if(!_target.HasValue||!candidates.Contains(_target.Value)||_nav.BestAdjacent(_target.Value)==null)return End("opening_target_unreachable");
            _beforeHealth=TreeHealth(_target.Value);_pickupDeadline=0;
        }
        var tile=_target.Value;var adjacent=_nav.BestAdjacent(tile);if(adjacent==null)return End("opening_target_unreachable");
        var move=_nav.Move(Map,adjacent.Value,input,_interact);Phase="opening_approach";
        if(move is "blocked" or "unsupported")return End(_nav.Reason);
        if(move!="reached")return "running";
        Face(tile);
        if(_skill=="plant_area")
        {
            var seeds=Game1.player.Items.FirstOrDefault(i=>i?.QualifiedItemId==SeedId) as StardewValley.Object;
            if(seeds==null)return End("starter_seeds_empty");
            Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(seeds);_seedBefore=Count(SeedId);
            if(!Utility.tryToPlaceItem(Game1.currentLocation,seeds,tile.X*64,tile.Y*64))return End("native_seed_placement_failed");
        }
        else
        {
            Tool? tool=_skill=="till_area"?Game1.player.Items.OfType<Hoe>().FirstOrDefault():ClearTool(tile);
            if(tool==null)return End("opening_tool_missing");
            Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(tool);input.Tool=true;
        }
        _wait=now+700;Phase="opening_verify";return "running";
    }
    private static float TreeHealth(GridPoint p)=>Game1.currentLocation.terrainFeatures.TryGetValue(new(p.X,p.Y),out var f)&&f is Tree t?t.health.Value:0;
    private Tool? ClearTool(GridPoint p)
    {
        if(Game1.currentLocation.objects.TryGetValue(new(p.X,p.Y),out var obj)&&obj.Name=="Stone")return Game1.player.Items.OfType<Pickaxe>().FirstOrDefault();
        if(Game1.currentLocation.terrainFeatures.TryGetValue(new(p.X,p.Y),out var f)&&f is Grass)return Game1.player.Items.OfType<MeleeWeapon>().FirstOrDefault(t=>t.isScythe());
        return Game1.player.Items.OfType<Axe>().FirstOrDefault();
    }
    private string CollectDrops(AiInputSimulator input,GridPoint target)
    {
        var drops=Game1.currentLocation.debris.Where(d=>!_oldDebris.Contains(d)&&(d.item!=null||!string.IsNullOrEmpty(d.itemId.Value))
            &&d.Chunks.Any(c=>Vector2.DistanceSquared(c.position.Value,new Vector2(target.X*64,target.Y*64))<320*320)).ToArray();
        if(drops.Length==0)return "done";
        if(_pickupDeadline==0){_pickupDeadline=Environment.TickCount64+20000;_nav.Reset();}
        if(Environment.TickCount64>_pickupDeadline)return End("opening_drop_pickup_timeout");
        var chunk=drops.SelectMany(d=>d.Chunks).OrderBy(c=>Vector2.DistanceSquared(c.position.Value,Game1.player.Position)).FirstOrDefault();
        if(chunk==null)return "done";
        var drop=new GridPoint((int)(chunk.position.X+32)/64,(int)(chunk.position.Y+32)/64);
        // Standing on a neighboring tile can still be outside the native magnetic radius.
        // Prefer centering on the actual item tile, falling back only for blocked tiles.
        GridPoint? goal=Navigation.Passable(drop)?drop:_nav.BestAdjacent(drop);
        if(goal==null)return End("opening_drop_unreachable");
        var state=_nav.Move(Map,goal.Value,input,_interact);if(state is "blocked" or "unsupported")return End(_nav.Reason);
        Phase="opening_collect_native_drops";return "running";
    }
    private string Starter(AiInputSimulator input)
    {
        if(Count("(O)472")>0){Completed=Count("(O)472");Reason="native_starter_seeds_received";return "completed";}
        if(!ReferenceEquals(Game1.currentLocation,Utility.getHomeOfFarmer(Game1.player)))return End("starter_requires_own_home");
        var gift=Game1.currentLocation.objects.Pairs.FirstOrDefault(p=>p.Value is Chest c&&c.giftbox.Value&&c.giftboxIsStarterGift.Value);
        if(gift.Value==null)return End("starter_gift_missing");
        var tile=new GridPoint((int)gift.Key.X,(int)gift.Key.Y);var goal=_nav.BestAdjacent(tile);if(goal==null)return End("starter_gift_unreachable");
        var state=_nav.Move(Game1.currentLocation.NameOrUniqueName,goal.Value,input,_interact);
        if(state is "blocked" or "unsupported")return End(_nav.Reason);
        if(state=="reached")_interact(tile);Phase="open_native_starter_gift";return "running";
    }
    private string Craft()
    {
        if(Game1.player.UsingTool||!Game1.player.CanMove||Game1.eventUp)return "running";
        if(_crafting==null)
        {
            if(Game1.activeClickableMenu!=null)return End("craft_menu_already_open");
            if(!Game1.player.craftingRecipes.ContainsKey("Chest"))return End("chest_recipe_unknown");
            if(Count("(O)388")<50)return End("chest_wood_missing:required=50;available="+Count("(O)388")+";missing="+(50-Count("(O)388")));
            if(!Game1.player.Items.Any(i=>i==null||i.QualifiedItemId=="(BC)130"&&i.Stack<i.maximumStackSize()))return End("crafted_item_inventory_full:checked_before_crafting;wood_not_spent");
            _woodBefore=Count("(O)388");_chestBefore=Count("(BC)130");
            _crafting=new CraftingPage(100,100,800,600,standaloneMenu:true);Game1.activeClickableMenu=_crafting;
        }
        if(!ReferenceEquals(Game1.activeClickableMenu,_crafting))return End("craft_menu_changed");
        if(!_crafted)
        {
            var recipe=_crafting.pagesOfCraftingRecipes.SelectMany((page,index)=>page.Select(pair=>(pair,index))).FirstOrDefault(p=>p.pair.Value.name=="Chest");
            if(recipe.pair.Key==null)return End("native_chest_recipe_not_visible");
            _crafting.currentCraftingPage=recipe.index;var click=recipe.pair.Key.bounds.Center;
            _crafting.receiveRightClick(click.X,click.Y);_crafted=true;
        }
        if(_crafting.heldItem!=null)
        {
            var output=_crafting.heldItem;
            var stack=Game1.player.Items.FirstOrDefault(i=>i!=null&&i.canStackWith(output)&&i.Stack< i.maximumStackSize());
            if(stack!=null){var moved=Math.Min(output.Stack,stack.maximumStackSize()-stack.Stack);stack.Stack+=moved;output.Stack-=moved;if(output.Stack<=0)_crafting.heldItem=null;}
            if(_crafting.heldItem!=null&&Game1.player.Items.IndexOf(null) is var empty&&empty>=0){Game1.player.Items[empty]=_crafting.heldItem;_crafting.heldItem=null;}
        }
        if(_crafting.heldItem!=null)return End("crafted_item_not_received");
        _crafting.exitThisMenu();
        if(Count("(O)388")!=_woodBefore-50||Count("(BC)130")!=_chestBefore+1)return End("native_chest_craft_not_verified");
        Capture();Completed=1;Reason="native_chest_crafted";return "completed";
    }
    private string Place(AiInputSimulator input)
    {
        var tile=new GridPoint(Num("x"),Num("y"));var goal=_nav.BestAdjacent(tile);if(goal==null)return End("chest_placement_unreachable");
        var state=_nav.Move(Map,goal.Value,input,_interact);if(state is "blocked" or "unsupported")return End(_nav.Reason);if(state!="reached")return "running";
        if(Game1.currentLocation.objects.ContainsKey(new(tile.X,tile.Y)))return End("chest_placement_occupied");
        var chest=Game1.player.Items.FirstOrDefault(i=>i?.QualifiedItemId=="(BC)130") as StardewValley.Object;if(chest==null)return End("crafted_chest_missing");
        var before=Count("(BC)130");Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(chest);
        if(!Utility.tryToPlaceItem(Game1.currentLocation,chest,tile.X*64,tile.Y*64))return End("native_chest_placement_failed");
        if(Count("(BC)130")!=before-1||!Game1.currentLocation.objects.TryGetValue(new(tile.X,tile.Y),out var placed)||placed is not Chest)return End("chest_placement_not_verified");
        Completed=1;Reason="native_chest_placed";return "completed";
    }
    private static void Face(GridPoint target)
    {var p=Navigation.Current;Game1.player.faceDirection(target.X>p.X?1:target.X<p.X?3:target.Y>p.Y?2:0);}
    internal bool Cleanup()
    {
        _disposed=true;if(_crafting!=null&&ReferenceEquals(Game1.activeClickableMenu,_crafting))
        {
            if(_crafting.heldItem is Item held)
            {
                if(Game1.player.addItemToInventoryBool(held))_crafting.heldItem=null;
                else {Game1.createItemDebris(held,Game1.player.Position,Game1.player.FacingDirection,Game1.currentLocation);_crafting.heldItem=null;}
            }
            _crafting.exitThisMenu();
        }return true;
    }
    internal static object InspectPlan()
    {
        if(!SoloTestMode.Enabled||Game1.currentLocation is not Farm farm)throw new InvalidOperationException("Opening inspection requires the solo test Farm");
        var save=Game1.uniqueIDForThisGame.ToString();var entry=farm.GetMainFarmHouseEntry();var nav=new Navigation();
        if(_plot?.Save!=save)
        {
            var choices=new List<(int Cost,int X,int Y)>();
            for(var x=entry.X-10;x<=entry.X+5;x++)for(var y=entry.Y+2;y<=entry.Y+12;y++)
            {
                var valid=true;var cost=0;
                for(var xx=x;xx<x+5&&valid;xx++)for(var yy=y;yy<y+3;yy++)
                {
                    var tile=new Vector2(xx,yy);
                    if(farm.doesTileHaveProperty(xx,yy,"Diggable","Back")==null||farm.isWaterTile(xx,yy)||farm.buildings.Any(b=>b.occupiesTile(tile))||farm.resourceClumps.Any(c=>c.getBoundingBox().Contains(xx*64+32,yy*64+32))){valid=false;break;}
                    if(farm.terrainFeatures.TryGetValue(tile,out var feature)&&feature is not Grass){valid=false;break;}
                    if(farm.objects.TryGetValue(tile,out var obj)){if(!Natural(obj)){valid=false;break;}cost+=4;}
                    if(feature is Grass)cost++;
                }
                if(valid)choices.Add((cost+Math.Abs(x-entry.X)+Math.Abs(y-entry.Y),x,y));
            }
            var chosen=choices.OrderBy(c=>c.Cost).FirstOrDefault(c=>nav.BestAdjacent(new(c.X,c.Y))!=null);
            if(chosen==default)throw new InvalidOperationException("No reachable starter plot found");_plot=(save,chosen.X,chosen.Y);
        }
        var plot=_plot.Value;
        var chestSpot=Enumerable.Range(plot.X-2,9).SelectMany(x=>Enumerable.Range(plot.Y+3,3).Select(y=>new GridPoint(x,y)))
            .FirstOrDefault(p=>Navigation.Passable(p)&&!farm.objects.ContainsKey(new(p.X,p.Y))&&!farm.terrainFeatures.ContainsKey(new(p.X,p.Y))&&nav.BestAdjacent(p)!=null);
        var wood=farm.objects.Pairs.Where(p=>p.Value.Name=="Twig").Select(p=>new{kind="twig",x=(int)p.Key.X,y=(int)p.Key.Y})
            .Concat(farm.terrainFeatures.Pairs.Where(p=>p.Value is Tree t&&!t.tapped.Value&&t.growthStage.Value>=5).Select(p=>new{kind="tree",x=(int)p.Key.X,y=(int)p.Key.Y}))
            .OrderBy(p=>Math.Abs(p.x-Navigation.Current.X)+Math.Abs(p.y-Navigation.Current.Y)).Take(50)
            .Where(p=>nav.BestAdjacent(new(p.x,p.y))!=null).Take(12).ToArray();
        return new{plot=new{location="Farm",x=plot.X,y=plot.Y,width=5,height=3},chestSpot=new{x=chestSpot.X,y=chestSpot.Y},woodTargets=wood,
            rain=farm.IsRainingHere(),money=Game1.player.Money,wood=Count("(O)388"),seeds=Count("(O)472"),entry=new{x=entry.X,y=entry.Y}};
    }
}
