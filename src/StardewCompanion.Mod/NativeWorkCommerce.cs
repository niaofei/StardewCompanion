using Microsoft.Xna.Framework;
using StardewCompanion.Protocol;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;
using StardewValley.Tools;
using StardewValley.Internal;
using Microsoft.Xna.Framework.Input;

namespace StardewCompanion.Mod;

internal sealed partial class NativeWorkExecutor
{
    private string? _collectedUpgradeId;
    private HashSet<long>? _livestockBefore;
    private bool _livestockSubmitted;
    private HashSet<Guid>? _constructionBefore;
    private FarmAnimal? _managedAnimal;
    private IEnumerable<FarmAnimal> OwnedLivestock()=>Game1.getFarm().animals.Values.Concat(Game1.getFarm().buildings.SelectMany(b=>b.GetIndoors()?.animals.Values.AsEnumerable()??Enumerable.Empty<FarmAnimal>())).GroupBy(a=>a.myID.Value).Select(g=>g.First());
    private string ToolService(AiInputSimulator input)
    {
        if(Game1.currentLocation.NameOrUniqueName!="Blacksmith"){var move=_nav.Move("Blacksmith",new(3,13),input,_interact);return move is "blocked" or "unsupported"?End("blacksmith_unreachable"):"running";}
        if(_action=="collect_upgraded_tool"){
            if(Game1.player.toolBeingUpgraded.Value==null){
                if(_collectedUpgradeId!=null&&Game1.player.Items.OfType<Tool>().Any(t=>t.QualifiedItemId==_collectedUpgradeId)){Completed=1;return Finish("upgraded_tool_received");}
                return End("no_tool_upgrade_pending");
            }
            var pending=Game1.player.toolBeingUpgraded.Value;var requested=ItemRegistry.Create(Text("toolId")) as Tool;
            if(requested==null||pending.GetType()!=requested.GetType())return End("requested_tool_not_being_upgraded");
            if(Game1.player.daysLeftForToolUpgrade.Value>0){_results.Add(new{state="upgrading",daysRemaining=Game1.player.daysLeftForToolUpgrade.Value});return End("tool_upgrade_pending");}
            if(Game1.player.freeSpotsInInventory()==0)return End("upgrade_collection_inventory_full");
            _collectedUpgradeId=pending.QualifiedItemId;
        }else if(Game1.player.toolBeingUpgraded.Value!=null)return End("another_tool_upgrade_pending");
        var upgradeLevel=Num("upgradeLevel");
        var materialId=upgradeLevel switch{1=>"(O)334",2=>"(O)335",3=>"(O)336",4=>"(O)337",_=>""};
        if(_action=="upgrade_tool"&&materialId!=""&&Count(materialId)<5){_results.Add(new{requiredMaterial=materialId,requiredCount=5,availableCount=Count(materialId)});return End("tool_upgrade_materials_missing");}
        if(Game1.activeClickableMenu is ShopMenu shop){
            _ownedMenu=shop;
            if(_action!="upgrade_tool"||shop.ShopId!="ClintUpgrade")return End("unexpected_upgrade_shop");
            if(shop.safetyTimer>0)return "running";
            var target=shop.forSale.OfType<Tool>().FirstOrDefault(t=>t.UpgradeLevel==upgradeLevel&&(t.QualifiedItemId==Text("toolId")||ShopBuilder.GetToolUpgradeData(t.GetToolData(),Game1.player)?.RequireToolId==Text("toolId")));
            if(target==null)return End("requested_tool_upgrade_not_offered");
            var info=shop.itemPriceAndStock[target];
            if(info.Price>Num("maxCost")||info.Price>Game1.player.Money)return End("upgrade_budget_exceeded");
            var moneyBefore=Game1.player.Money;var materialBefore=materialId==""?0:Count(materialId);
            var n=shop.forSale.IndexOf(target);shop.currentItemIndex=Math.Min(n,Math.Max(0,shop.forSale.Count-4));
            var click=shop.forSaleButtons[n-shop.currentItemIndex].bounds.Center;shop.receiveLeftClick(click.X,click.Y);
            if(Game1.player.toolBeingUpgraded.Value==null)return End("tool_upgrade_order_not_verified");
            var materialConsumed=materialId==""?0:materialBefore-Count(materialId);var moneyConsumed=moneyBefore-Game1.player.Money;
            if(materialId!=""&&materialConsumed!=5)return End("tool_upgrade_material_consumption_not_verified");
            _results.Add(new{state="upgrading",tool=Text("toolId"),upgradeLevel,moneyConsumed,materialId,materialConsumed,daysRemaining=Game1.player.daysLeftForToolUpgrade.Value});Completed=1;return Finish("tool_upgrade_order_placed");
        }
        if(Game1.activeClickableMenu is DialogueBox dialogue){
            if(_action=="upgrade_tool"&&_responses.Count==0)_responses.Enqueue("Upgrade");return Dialogue(dialogue);
        }
        var tile=FindAction(a=>a.Contains("Blacksmith",StringComparison.OrdinalIgnoreCase));
        if(tile==null)return End("blacksmith_counter_not_observed");
        var near=Near("Blacksmith",tile.Value,input);if(near is "blocked" or "unsupported")return End("blacksmith_counter_unreachable");if(near=="reached")_interact(tile.Value);return "running";
    }
    private static Point ScreenTile(int x,int y)
    {
        var vector=Utility.ModifyCoordinatesForUIScale(new Vector2(x*64+32-Game1.viewport.X,y*64+32-Game1.viewport.Y));
        return new((int)vector.X,(int)vector.Y);
    }
    private static void ClickWorldTile(IClickableMenu menu,Point point)
    {
        var previous=Game1.oldMouseState;
        Game1.oldMouseState=new MouseState((int)(point.X*Game1.options.uiScale),(int)(point.Y*Game1.options.uiScale),0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
        try{menu.receiveLeftClick(point.X,point.Y);}finally{Game1.oldMouseState=previous;}
    }
    private Building? SelectedBuilding()=>Game1.getFarm().buildings.FirstOrDefault(b=>b.id.Value.ToString()==Text("buildingId"));
    private string Buildings(AiInputSimulator input)
    {
        if(_constructionBefore!=null){
            var added=Game1.getFarm().buildings.FirstOrDefault(b=>!_constructionBefore.Contains(b.id.Value)&&b.tileX.Value==Num("x")&&b.tileY.Value==Num("y")&&b.buildingType.Value==Text("buildingType"));
            if(added!=null){Completed=1;_results.Add(new{buildingId=added.id.Value.ToString(),daysRemaining=added.daysOfConstructionLeft.Value});return Finish("building_order_verified");}
            return Environment.TickCount64-_wait>5000?End("native_building_placement_not_verified"):"running";
        }
        if(_action=="modify_building"&&Text("operation")=="demolish"&&_index>=2){
            if(SelectedBuilding()==null){Completed=1;return Finish("building_demolition_verified");}
            if(Game1.activeClickableMenu is DialogueBox confirm){if(_responses.Count==0)_responses.Enqueue("Yes");return Dialogue(confirm);}
            return Environment.TickCount64-_wait>5000?End("building_demolition_not_verified"):"running";
        }
        if(Game1.activeClickableMenu is DialogueBox dialogue){if(_responses.Count==0)_responses.Enqueue("Construct");return Dialogue(dialogue);}
        if(Game1.activeClickableMenu is not CarpenterMenu menu){
            if(Game1.currentLocation.NameOrUniqueName!="ScienceHouse"){var move=_nav.Move("ScienceHouse",new(8,18),input,_interact);return move is "blocked" or "unsupported"?End("carpenter_route_unavailable"):"running";}
            var tile=FindAction(a=>a.Contains("Carpenter"));if(tile==null)return End("carpenter_counter_not_observed");
            var near=Near("ScienceHouse",tile.Value,input);if(near is "blocked" or "unsupported")return End("carpenter_counter_unreachable");if(near=="reached")_interact(tile.Value);return "running";
        }
        _ownedMenu=menu;
        if(_index==0){
            var op=_action=="order_building"?"build":Text("operation");
            if(op=="build"||op=="upgrade"){
                var id=_action=="order_building"?Text("buildingType"):Text("upgradeType");
                var blueprint=menu.Blueprints.FirstOrDefault(b=>b.Id==id);if(blueprint==null)return End("building_not_unlocked:"+id);
                if(blueprint.BuildCost>Num("maxCost")||blueprint.BuildCost>Game1.player.Money)return End("building_budget_exceeded");
                menu.SetNewActiveBlueprint(blueprint);
                if(!menu.DoesFarmerHaveEnoughResourcesToBuild())return End("building_materials_missing");
                var click=menu.okButton.bounds.Center;menu.receiveLeftClick(click.X,click.Y);
            }else{
                var button=op=="move"?menu.moveButton:menu.demolishButton;var click=button.bounds.Center;menu.receiveLeftClick(click.X,click.Y);
            }
            _index=1;return "running";
        }
        if(!menu.onFarm)return "running";
        var operation=_action=="order_building"?"build":Text("operation");
        var building=operation=="build"?null:SelectedBuilding();
        if(operation!="build"&&building==null)return End("building_identity_missing");
        if(_index==1&&operation!="build"){
            var p=ScreenTile(building!.tileX.Value,building.tileY.Value);ClickWorldTile(menu,p);_index=2;_wait=Environment.TickCount64;
            if(operation=="demolish")return "running";
            if(operation=="upgrade"){
                if(building.daysUntilUpgrade.Value<=0&&building.buildingType.Value!=Text("upgradeType"))return End("building_upgrade_not_verified");
                Completed=1;return Finish("building_upgrade_order_verified");
            }return "running";
        }
        var target=ScreenTile(Num("x"),Num("y"));if(operation=="build")_constructionBefore=Game1.getFarm().buildings.Select(b=>b.id.Value).ToHashSet();ClickWorldTile(menu,target);_wait=Environment.TickCount64;
        if(operation=="move"){
            if(building!.tileX.Value!=Num("x")||building.tileY.Value!=Num("y"))return End("building_move_rejected");
            Completed=1;return Finish("building_moved");
        }
        return "running";
    }
    private string Livestock(AiInputSimulator input)
    {
        var operation=Text("operation");
        if(_livestockSubmitted){var added=OwnedLivestock().FirstOrDefault(a=>!_livestockBefore!.Contains(a.myID.Value)&&a.Name==Text("name"));if(added==null)return End("animal_purchase_not_verified");Completed=1;_results.Add(new{animalId=added.myID.Value.ToString(),name=added.Name,homeId=added.home?.id.Value.ToString()});return Finish("animal_purchase_verified");}
        if(operation is "incubate" or "rehome" or "sell"&&Game1.currentLocation.NameOrUniqueName!=Map&&Game1.activeClickableMenu==null){var move=_nav.Move(Map,new(3,5),input,_interact);return move is "blocked" or "unsupported"?End("livestock_location_unreachable"):"running";}
        if(operation=="incubate"){
            var incubator=Game1.currentLocation.objects.Pairs.FirstOrDefault(p=>p.Value.Name.Contains("Incubator")&&p.Value.heldObject.Value==null);
            if(incubator.Value==null)return End("empty_incubator_not_observed");
            var point=new GridPoint((int)incubator.Key.X,(int)incubator.Key.Y);var near=Near(Map,point,input);if(near is "blocked" or "unsupported")return End("incubator_unreachable");if(near!="reached")return "running";
            var egg=Item(Text("itemId"));if(egg==null)return End("incubation_item_missing");
            Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(egg);_interact(point);
            if(incubator.Value.heldObject.Value==null)return End("native_incubation_rejected");
            Completed=1;return Finish("incubation_started");
        }
        if(operation=="door")return Animals(input);
        if(operation is "rehome" or "sell"){
            var animal=_managedAnimal??=Game1.currentLocation.animals.Values.FirstOrDefault(a=>a.myID.Value.ToString()==Text("animalId"));if(animal==null)return End("animal_not_observed");
            if(Game1.activeClickableMenu is not AnimalQueryMenu query){
                var near=Near(Map,new(animal.TilePoint.X,animal.TilePoint.Y),input);if(near is "blocked" or "unsupported")return End("animal_unreachable");if(near!="reached")return "running";
                animal.pet(Game1.player);return "running";
            }
            _ownedMenu=query;
            if(operation=="sell"){
                var button=query.confirmingSell?query.yesButton:query.sellButton;var click=button.bounds.Center;query.receiveLeftClick(click.X,click.Y);
                if(!OwnedLivestock().Any(a=>a.myID.Value==animal.myID.Value)){Completed=1;return Finish("animal_sale_verified");}return "running";
            }
            var home=SelectedBuilding();if(home==null||!animal.CanLiveIn(home))return End("animal_home_incompatible");
            if(!query.movingAnimal){var click=query.moveHomeButton.bounds.Center;query.receiveLeftClick(click.X,click.Y);return "running";}
            var point=ScreenTile(home.tileX.Value,home.tileY.Value);ClickWorldTile(query,point);
            if(animal.home!=home)return End("animal_rehome_not_verified");Completed=1;return Finish("animal_rehomed");
        }
        if(Game1.activeClickableMenu is PurchaseAnimalsMenu menu){
            _ownedMenu=menu;
            if(menu.priceOfAnimal>Num("maxCost")||menu.priceOfAnimal>Game1.player.Money)return End("animal_purchase_budget_exceeded");
            if(!menu.onFarm){
                var button=menu.animalsToPurchase.FirstOrDefault(b=>b.name==Text("animalType"));if(button==null)return End("animal_type_not_offered");
                var click=button.bounds.Center;menu.receiveLeftClick(click.X,click.Y);return "running";
            }
            if(menu.namingAnimal){_livestockBefore=OwnedLivestock().Select(a=>a.myID.Value).ToHashSet();menu.textBox.Text=Text("name");menu.textBoxEnter(menu.textBox);_livestockSubmitted=true;return "running";}
            var building=SelectedBuilding();if(building==null)return End("animal_home_not_found");
            var point=ScreenTile(building.tileX.Value,building.tileY.Value);ClickWorldTile(menu,point);return "running";
        }
        if(Game1.activeClickableMenu is DialogueBox dialogue){if(_responses.Count==0)_responses.Enqueue("Purchase");return Dialogue(dialogue);}
        if(Game1.currentLocation.NameOrUniqueName!="AnimalShop"){var move=_nav.Move("AnimalShop",new(13,13),input,_interact);return move is "blocked" or "unsupported"?End("animal_shop_unreachable"):"running";}
        var counter=FindAction(a=>a.Contains("AnimalShop")||a.Contains("Marnie"));
        if(counter==null)return End("animal_shop_counter_not_observed");
        var approach=Near("AnimalShop",counter.Value,input);if(approach is "blocked" or "unsupported")return End("animal_shop_counter_unreachable");if(approach=="reached")_interact(counter.Value);return "running";
    }
}
