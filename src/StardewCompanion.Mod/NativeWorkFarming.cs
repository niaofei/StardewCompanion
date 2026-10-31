using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewCompanion.Protocol;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace StardewCompanion.Mod;

internal sealed partial class NativeWorkExecutor
{
    private int _recoveryStartMinutes=-1;
    private string FarmingExtras(AiInputSimulator input)
    {
        if(_action=="water_coverage"){
            var watered=Game1.currentLocation.terrainFeatures.Pairs.Where(p=>p.Value is HoeDirt d&&WorldView.NeedsWater(d,Game1.currentLocation))
                .Select(p=>new GridPoint((int)p.Key.X,(int)p.Key.Y)).ToArray();
            if(watered.Length==0)return Finish("covered_crops_already_watered");
            return End("water_now_requires_explicit_plot_region");
        }
        if(_action=="forage_extras"){
            var cells=Enumerable.Range(Num("x"),Num("width")).SelectMany(x=>Enumerable.Range(Num("y"),Num("height")).Select(y=>new GridPoint(x,y)));
            foreach(var tile in cells.Where(t=>!_done.Contains(t))){
                var l=Game1.currentLocation;var point=new Vector2(tile.X,tile.Y);
                var dig=Flag("digSpots")&&l.objects.TryGetValue(point,out var obj)&&obj.Name is "Artifact Spot" or "Seed Spot";
                var shake=Flag("shake")&&l.terrainFeatures.TryGetValue(point,out var f)&&f is Tree or Bush;
                if(!dig&&!shake){_done.Add(tile);continue;}
                var near=Near(Map,tile,input);if(near is "blocked" or "unsupported"){_done.Add(tile);_results.Add(new{x=tile.X,y=tile.Y,reason="unreachable"});continue;}if(near!="reached")return "running";
                if(_wait==0){
                    if(dig){var hoe=Game1.player.Items.OfType<Hoe>().FirstOrDefault();if(hoe==null)return End("hoe_missing");Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(hoe);Face(tile);input.Tool=true;}
                    else _interact(tile);
                    _wait=Environment.TickCount64+1000;return "running";
                }
                if(Environment.TickCount64<_wait)return "running";_wait=0;_done.Add(tile);Completed++;return "running";
            }
            return Finish("visible_forage_extras_processed");
        }
        return End("farming_action_not_supported");
    }
    private string RecoverByLocation(AiInputSimulator input)
    {
        var minutes=Game1.timeOfDay/100*60+Game1.timeOfDay%100;
        if(_recoveryStartMinutes<0)_recoveryStartMinutes=minutes;
        if(minutes-_recoveryStartMinutes>=Num("maxGameMinutes"))return End("recovery_requested_wait_limit");
        if(Text("method")=="chair")
            return End("vanilla_solo_chair_sitting_does_not_restore_stamina");
        if(Text("method")=="multiplayer_bed"){
            if(!StardewModdingAPI.Context.IsMultiplayer)return End("bed_rest_requires_multiplayer");
            var home=Utility.getHomeOfFarmer(Game1.player);var bed=home.GetPlayerBedSpot();
            if(Game1.activeClickableMenu is DialogueBox {isQuestion:true} dialogue&&Game1.currentLocation.lastQuestionKey=="Sleep"){
                dialogue.selectedResponse=Array.FindIndex(dialogue.responses,r=>r.responseKey=="No");
                if(dialogue.selectedResponse>=0)dialogue.receiveLeftClick(0,0);return "running";
            }
            if(Game1.player.isInBed.Value){Phase="resting_in_multiplayer_bed";return "running";}
            var near=Near(home.NameOrUniqueName,new(bed.X,bed.Y),input,false);if(near is "blocked" or "unsupported")return End("bed_unreachable");if(near=="reached")input.Direction="right";return "running";
        }
        if(Text("method")=="spa"){
            if(Game1.player.swimming.Value){Phase="resting_in_native_spa_pool";return "running";}
            if(Game1.currentLocation.NameOrUniqueName!="BathHouse_Pool"){
                var move=_nav.Move("BathHouse_Pool",new(13,9),input,_interact);return move is "blocked" or "unsupported"?End("spa_locked_or_unreachable"):"running";
            }
            var entrance=FindTouchAction(a=>a.Contains("PoolEntrance",StringComparison.OrdinalIgnoreCase));
            if(entrance==null)return End("pool_entrance_not_observed");
            var state=Near("BathHouse_Pool",entrance.Value,input,false);if(state is "blocked" or "unsupported")return End("pool_entry_unreachable");return "running";
        }
        return End("unknown_recovery_method");
    }
    private GridPoint? FindTouchAction(Func<string,bool> predicate)
    {
        var l=Game1.currentLocation;
        for(var x=0;x<l.Map.Layers[0].LayerWidth;x++)for(var y=0;y<l.Map.Layers[0].LayerHeight;y++){
            var action=l.doesTileHaveProperty(x,y,"TouchAction","Back");
            if(action!=null&&predicate(action))return new(x,y);
        }return null;
    }
    private string Animals(AiInputSimulator input)
    {
        if(Game1.currentLocation.NameOrUniqueName!=Map){var move=_nav.Move(Map,new(3,5),input,_interact);return move is "blocked" or "unsupported"?End("animal_location_unreachable"):"running";}
        var l=Game1.currentLocation;
        if(_action=="feed"){
            if(l is not AnimalHouse house)return End("animal_house_required");
            var before=Count("(O)178");house.feedAllAnimals();
            Completed=Math.Max(0,before-Count("(O)178"));return Finish("native_feeding_processed");
        }
        if(_action=="animal_door"){
            var building=Game1.getFarm().buildings.FirstOrDefault(b=>b.GetIndoors()?.NameOrUniqueName==Map);
            if(building==null)return End("animal_building_not_found");
            if(building.animalDoorOpen.Value==(Text("door")=="open"))return Finish("door_already_requested_state");
            var point=new GridPoint(building.tileX.Value+building.animalDoor.X,building.tileY.Value+building.animalDoor.Y);
            var move=Near("Farm",point,input);if(move is "blocked" or "unsupported")return End("animal_door_unreachable");if(move!="reached")return "running";
            _interact(point);return "running";
        }
        if(_action=="pet_animal"||_action=="animal_tool"){
            var animal=l.animals.Values.FirstOrDefault(a=>a.myID.Value.ToString()==Text("animalId"));if(animal==null)return End("animal_not_observed");
            var point=new GridPoint(animal.TilePoint.X,animal.TilePoint.Y);var near=Near(Map,point,input);if(near is "blocked" or "unsupported")return End("animal_unreachable");if(near!="reached")return "running";
            if(_action=="pet_animal"){if(animal.wasPet.Value)return Finish("animal_already_petted");animal.pet(Game1.player);if(!animal.wasPet.Value)return End("animal_pet_not_verified");Completed=1;return Finish("animal_petted");}
            if(animal.currentProduce.Value==null)return Finish("animal_has_no_tool_harvest");
            if(_wait==0){
                var tool=Game1.player.Items.OfType<Tool>().FirstOrDefault(t=>t.GetType().Name==Text("tool"));
                if(tool==null)return End("animal_harvest_tool_missing");
                _before=Game1.player.Items.Where(i=>i!=null).Sum(i=>i.Stack);Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(tool);Face(point);input.Tool=true;_wait=Environment.TickCount64+1500;return "running";
            }
            if(Environment.TickCount64<_wait)return "running";
            if(animal.currentProduce.Value!=null)return End("animal_tool_harvest_not_verified");Completed=1;return Finish("animal_produce_collected");
        }
        if(_action=="collect_animal_ground"){
            var product=l.objects.Pairs.FirstOrDefault(p=>p.Value.Category is -5 or -6 or -18);
            if(product.Value==null)return Finish("ground_produce_collected");
            var point=new GridPoint((int)product.Key.X,(int)product.Key.Y);var near=Near(Map,point,input);if(near is "blocked" or "unsupported")return End("ground_produce_unreachable");if(near=="reached"){Game1.player.CurrentToolIndex=EmptyHand();_interact(point);Completed++;}return "running";
        }
        if(_action=="collect_grabbers"){
            var grabber=l.objects.Pairs.FirstOrDefault(p=>p.Value.QualifiedItemId=="(BC)165"&&!_done.Contains(new((int)p.Key.X,(int)p.Key.Y)));
            if(grabber.Value==null)return Finish("grabbers_processed");
            var point=new GridPoint((int)grabber.Key.X,(int)grabber.Key.Y);var near=Near(Map,point,input);if(near is "blocked" or "unsupported")return End("grabber_unreachable");
            if(Game1.activeClickableMenu is ItemGrabMenu menu){
                _ownedMenu=menu;var index=menu.ItemsToGrabMenu.actualInventory.ToList().FindIndex(i=>i!=null);
                if(index<0){menu.exitThisMenu();_done.Add(point);return "running";}
                var click=menu.ItemsToGrabMenu.inventory[index].bounds.Center;menu.receiveRightClick(click.X,click.Y);Completed++;return "running";
            }
            if(near=="reached")_interact(point);return "running";
        }
        if(_action=="pets"){
            var pet=l.characters.OfType<Pet>().FirstOrDefault(p=>!_done.Contains(new(p.TilePoint.X,p.TilePoint.Y)));
            if(pet==null)return Flag("waterBowls")?End("pet_water_bowls_require_explicit_target_support"):Finish("pets_processed");
            var point=new GridPoint(pet.TilePoint.X,pet.TilePoint.Y);var near=Near(Map,point,input);if(near is "blocked" or "unsupported"){_done.Add(point);return "running";}if(near!="reached")return "running";
            if(Flag("pet"))pet.checkAction(Game1.player,l);_done.Add(point);Completed++;return "running";
        }
        return End("animal_action_not_supported");
    }
}
