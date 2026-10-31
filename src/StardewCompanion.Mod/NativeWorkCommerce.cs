using Microsoft.Xna.Framework;
using StardewCompanion.Protocol;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;
using StardewValley.Tools;

namespace StardewCompanion.Mod;

internal sealed partial class NativeWorkExecutor
{
    private string ToolService(AiInputSimulator input)
    {
        if(Game1.currentLocation.NameOrUniqueName!="Blacksmith"){var move=_nav.Move("Blacksmith",new(3,13),input,_interact);return move is "blocked" or "unsupported"?End("blacksmith_unreachable"):"running";}
        if(_action=="collect_upgraded_tool"){
            if(Game1.player.toolBeingUpgraded.Value==null){
                if(Game1.player.Items.OfType<Tool>().Any(t=>t.QualifiedItemId==Text("toolId"))){Completed=1;return Finish("upgraded_tool_received");}
                return End("no_tool_upgrade_pending");
            }
            if(Game1.player.daysLeftForToolUpgrade.Value>0){_results.Add(new{state="upgrading",daysRemaining=Game1.player.daysLeftForToolUpgrade.Value});return Finish("tool_upgrade_pending");}
        }else if(Game1.player.toolBeingUpgraded.Value!=null)return End("another_tool_upgrade_pending");
        if(Game1.activeClickableMenu is ShopMenu shop){
            _ownedMenu=shop;
            var target=shop.forSale.OfType<Tool>().FirstOrDefault(t=>t.QualifiedItemId==Text("toolId")&&t.UpgradeLevel==Num("upgradeLevel"));
            if(target==null)return End("requested_tool_upgrade_not_offered");
            var info=shop.itemPriceAndStock[target];
            if(info.Price>Num("maxCost")||info.Price>Game1.player.Money)return End("upgrade_budget_exceeded");
            var n=shop.forSale.IndexOf(target);shop.currentItemIndex=Math.Min(n,Math.Max(0,shop.forSale.Count-4));
            var click=shop.forSaleButtons[n-shop.currentItemIndex].bounds.Center;shop.receiveLeftClick(click.X,click.Y);
            if(Game1.player.toolBeingUpgraded.Value==null)return End("tool_upgrade_order_not_verified");
            _results.Add(new{state="upgrading",tool=Text("toolId"),daysRemaining=Game1.player.daysLeftForToolUpgrade.Value});Completed=1;return Finish("tool_upgrade_order_placed");
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
    private Building? SelectedBuilding()=>Game1.getFarm().buildings.FirstOrDefault(b=>b.id.Value.ToString()==Text("buildingId"));
    private string Buildings(AiInputSimulator input)
    {
        if(Game1.activeClickableMenu is DialogueBox dialogue){if(_responses.Count==0)_responses.Enqueue("Construct");return Dialogue(dialogue);}
        if(Game1.activeClickableMenu is not CarpenterMenu menu){
            if(_wait!=0&&Environment.TickCount64-_wait>1500){
                var exists=Game1.getFarm().buildings.Any(b=>b.tileX.Value==Num("x")&&b.tileY.Value==Num("y"));
                if(exists){Completed=1;return Finish("construction_submitted_or_building_moved");}
            }
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
            var p=ScreenTile(building!.tileX.Value,building.tileY.Value);menu.receiveLeftClick(p.X,p.Y);_index=2;
            if(operation=="demolish")return Finish("demolition_requested");
            if(operation=="upgrade"){Completed=1;return Finish("building_upgrade_order_placed");}return "running";
        }
        var target=ScreenTile(Num("x"),Num("y"));menu.receiveLeftClick(target.X,target.Y);_wait=Environment.TickCount64;
        if(operation=="move"){
            if(building!.tileX.Value!=Num("x")||building.tileY.Value!=Num("y"))return End("building_move_rejected");
            Completed=1;return Finish("building_moved");
        }
        var added=Game1.getFarm().buildings.FirstOrDefault(b=>b.tileX.Value==Num("x")&&b.tileY.Value==Num("y")&&b.buildingType.Value==Text("buildingType"));
        if(added==null)return End("native_building_placement_rejected");
        _results.Add(new{buildingId=added.id.Value.ToString(),state=added.daysOfConstructionLeft.Value>0?"construction_pending":"completed",daysRemaining=added.daysOfConstructionLeft.Value});
        Completed=1;return Finish("building_order_placed");
    }
    private string Livestock(AiInputSimulator input)
    {
        var operation=Text("operation");
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
            var animal=Game1.currentLocation.animals.Values.FirstOrDefault(a=>a.myID.Value.ToString()==Text("animalId"));if(animal==null)return End("animal_not_observed");
            if(Game1.activeClickableMenu is not AnimalQueryMenu query){
                var near=Near(Map,new(animal.TilePoint.X,animal.TilePoint.Y),input);if(near is "blocked" or "unsupported")return End("animal_unreachable");if(near!="reached")return "running";
                animal.pet(Game1.player);return "running";
            }
            _ownedMenu=query;
            if(operation=="sell"){
                var button=query.confirmingSell?query.yesButton:query.sellButton;var click=button.bounds.Center;query.receiveLeftClick(click.X,click.Y);
                if(!Game1.currentLocation.animals.ContainsKey(animal.myID.Value)){Completed=1;return Finish("animal_sale_verified");}return "running";
            }
            var home=SelectedBuilding();if(home==null||!animal.CanLiveIn(home))return End("animal_home_incompatible");
            if(!query.movingAnimal){var click=query.moveHomeButton.bounds.Center;query.receiveLeftClick(click.X,click.Y);return "running";}
            var point=ScreenTile(home.tileX.Value,home.tileY.Value);query.receiveLeftClick(point.X,point.Y);
            if(animal.home!=home)return End("animal_rehome_not_verified");Completed=1;return Finish("animal_rehomed");
        }
        if(Game1.activeClickableMenu is PurchaseAnimalsMenu menu){
            _ownedMenu=menu;
            if(menu.priceOfAnimal>Num("maxCost"))return End("animal_purchase_budget_exceeded");
            if(!menu.onFarm){
                var button=menu.animalsToPurchase.FirstOrDefault(b=>b.name==Text("animalType"));if(button==null)return End("animal_type_not_offered");
                var click=button.bounds.Center;menu.receiveLeftClick(click.X,click.Y);return "running";
            }
            if(menu.namingAnimal){menu.textBox.Text=Text("name");menu.textBoxEnter(menu.textBox);Completed=1;return Finish("native_animal_purchase_submitted");}
            var building=SelectedBuilding();if(building==null)return End("animal_home_not_found");
            var point=ScreenTile(building.tileX.Value,building.tileY.Value);menu.receiveLeftClick(point.X,point.Y);return "running";
        }
        if(Game1.activeClickableMenu is DialogueBox dialogue){if(_responses.Count==0)_responses.Enqueue("Purchase");return Dialogue(dialogue);}
        if(Game1.currentLocation.NameOrUniqueName!="AnimalShop"){var move=_nav.Move("AnimalShop",new(13,13),input,_interact);return move is "blocked" or "unsupported"?End("animal_shop_unreachable"):"running";}
        var counter=FindAction(a=>a.Contains("AnimalShop")||a.Contains("Marnie"));
        if(counter==null)return End("animal_shop_counter_not_observed");
        var approach=Near("AnimalShop",counter.Value,input);if(approach is "blocked" or "unsupported")return End("animal_shop_counter_unreachable");if(approach=="reached")_interact(counter.Value);return "running";
    }
}
