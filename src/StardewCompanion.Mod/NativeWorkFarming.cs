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
using StardewValley.Buildings;

namespace StardewCompanion.Mod;

internal sealed partial class NativeWorkExecutor
{
    private int _recoveryStartMinutes=-1;
    private readonly HashSet<Guid> _pettedIds=new();
    private readonly HashSet<Guid> _wateredBowlIds=new();
    private HashSet<Debris>? _petGiftBaseline;
    private long _petGiftDeadline;
    private PetBowl? _wateringBowl;
    private string FarmingExtras(AiInputSimulator input)
    {
        if(_action=="water_coverage"){
            var watered=Game1.currentLocation.terrainFeatures.Pairs.Where(p=>p.Value is HoeDirt d&&WorldView.NeedsWater(d,Game1.currentLocation))
                .Select(p=>new GridPoint((int)p.Key.X,(int)p.Key.Y)).ToArray();
            if(watered.Length==0)return Finish("covered_crops_already_watered");
            return End("water_now_requires_explicit_plot_region");
        }
        if(_action=="forage_extras"){
            if(Game1.currentLocation.NameOrUniqueName!=Map){var move=_nav.Move(Map,new(Num("x"),Num("y")),input,_interact);return move is "blocked" or "unsupported"?End("forage_stop_unreachable"):"running";}
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
        if(_action=="animal_door"||_action=="livestock"&&Text("operation")=="door"){
            var building=Game1.getFarm().buildings.FirstOrDefault(b=>b.GetIndoors()?.NameOrUniqueName==Map||b.id.Value.ToString()==Text("buildingId"));
            if(building==null)return End("animal_building_not_found");if(Text("door")=="unchanged"||building.animalDoorOpen.Value==(Text("door")=="open"))return Finish("door_already_requested_state");
            var point=new GridPoint(building.tileX.Value+building.animalDoor.X,building.tileY.Value+building.animalDoor.Y);var move=Near("Farm",point,input);
            if(move is "blocked" or "unsupported")return End("animal_door_unreachable");if(move!="reached")return "running";_interact(point);
            if(building.animalDoorOpen.Value!=(Text("door")=="open"))return End("animal_door_change_not_verified");Completed=1;return Finish("animal_door_verified");
        }
        if(Game1.currentLocation.NameOrUniqueName!=Map){var move=_nav.Move(Map,new(3,5),input,_interact);return move is "blocked" or "unsupported"?End("animal_location_unreachable"):"running";}
        var l=Game1.currentLocation;
        if(_action=="feed"){
            if(l is not AnimalHouse house)return End("animal_house_required");
            var before=house.objects.Values.Count(o=>o.QualifiedItemId=="(O)178");house.feedAllAnimals();
            Completed=Math.Max(0,house.objects.Values.Count(o=>o.QualifiedItemId=="(O)178")-before);
            var emptyTrough=false;for(var x=0;x<house.Map.Layers[0].LayerWidth;x++)for(var y=0;y<house.Map.Layers[0].LayerHeight;y++)if(house.doesTileHaveProperty(x,y,"Trough","Back")!=null&&!house.objects.ContainsKey(new(x,y)))emptyTrough=true;
            return emptyTrough?End("silo_hay_insufficient_for_trough"):Finish("native_feeding_verified");
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
                var collected=CollectNativeMenu(menu);if(collected=="completed"){_done.Add(point);return "running";}return collected;
            }
            if(near=="reached")_interact(point);return "running";
        }
        if(_action=="pets"){
            _petGiftBaseline??=l.debris.ToHashSet();
            var ids=_args.GetProperty("petIds").EnumerateArray().Select(v=>v.GetString()).ToHashSet();
            var pets=l.characters.OfType<Pet>().Where(p=>ids.Count==0||ids.Contains(p.petId.Value.ToString())).ToArray();
            if(ids.Count>0&&ids.Any(id=>!pets.Any(p=>p.petId.Value.ToString()==id)))return End("pet_id_not_observed_here");
            var pet=Flag("pet")?pets.FirstOrDefault(p=>!_pettedIds.Contains(p.petId.Value)):null;
            if(pet!=null){
                if(pet.lastPetDay.TryGetValue(Game1.player.UniqueMultiplayerID,out var day)&&day==Game1.Date.TotalDays){_pettedIds.Add(pet.petId.Value);return "running";}
                var point=new GridPoint(pet.TilePoint.X,pet.TilePoint.Y);var near=Near(Map,point,input);if(near is "blocked" or "unsupported")return End("pet_unreachable");if(near!="reached")return "running";
                Game1.player.CurrentToolIndex=EmptyHand();pet.checkAction(Game1.player,l);
                if(!pet.lastPetDay.TryGetValue(Game1.player.UniqueMultiplayerID,out day)||day!=Game1.Date.TotalDays)return End("petting_not_verified");
                _pettedIds.Add(pet.petId.Value);Completed++;_petGiftDeadline=Environment.TickCount64+15000;return "running";
            }
            if(Flag("collectGifts")){
                var gift=l.debris.FirstOrDefault(d=>!_petGiftBaseline.Contains(d)&&d.Chunks.Count>0&&(d.item!=null||!string.IsNullOrEmpty(d.itemId.Value)));
                if(gift!=null){
                    if(Environment.TickCount64>=_petGiftDeadline)return End("pet_gift_pickup_timeout_or_inventory_full");
                    var chunk=gift.Chunks.First();var point=new GridPoint((int)((chunk.position.X+32)/64),(int)((chunk.position.Y+32)/64));var goal=Navigation.Passable(point)?point:_nav.BestAdjacent(point);
                    if(goal==null)return End("pet_gift_unreachable");var moved=_nav.Move(Map,goal.Value,input,_interact);return moved is "blocked" or "unsupported"?End("pet_gift_unreachable"):"running";
                }
            }
            if(Flag("waterBowls")){
                if(_wateringBowl!=null){
                    if(_wateringBowl.watered.Value){_wateredBowlIds.Add(_wateringBowl.id.Value);_wateringBowl=null;_wait=0;Completed++;}
                    else if(Environment.TickCount64<_wait)return "running";
                    else return End("pet_bowl_watering_not_verified");
                }
                var bowls=l.buildings.OfType<PetBowl>().Where(b=>ids.Count==0||ids.Contains(b.petId.Value.ToString())).ToArray();
                if(bowls.Length==0)return End("pet_bowl_not_observed_here");
                var bowl=bowls.FirstOrDefault(b=>!b.watered.Value&&!_wateredBowlIds.Contains(b.id.Value));
                if(bowl!=null){
                    GridPoint? tile=null;
                    for(var x=bowl.tileX.Value;x<bowl.tileX.Value+bowl.tilesWide.Value;x++)for(var y=bowl.tileY.Value;y<bowl.tileY.Value+bowl.tilesHigh.Value;y++){
                        string property=null!;if(bowl.doesTileHaveProperty(x,y,"PetBowl","Buildings",ref property))tile=new(x,y);
                    }
                    if(tile==null)return End("pet_bowl_water_tile_missing");var near=Near(Map,tile.Value,input);if(near is "blocked" or "unsupported")return End("pet_bowl_unreachable");if(near!="reached")return "running";
                    if(_wait!=0){if(Environment.TickCount64<_wait)return "running";return End("pet_bowl_watering_not_verified");}
                    var can=Game1.player.Items.OfType<WateringCan>().FirstOrDefault();if(can==null||can.WaterLeft<=0)return End("watering_can_missing_or_empty");
                    Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(can);Face(tile.Value);input.Tool=true;_wateringBowl=bowl;_wait=Environment.TickCount64+1500;return "running";
                }
                if(_wait!=0){Completed++;_wait=0;}
            }
            return Finish("requested_pet_care_verified");
        }
        return End("animal_action_not_supported");
    }
}
