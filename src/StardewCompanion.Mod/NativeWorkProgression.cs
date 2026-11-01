using Microsoft.Xna.Framework;
using StardewCompanion.Protocol;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Tools;

namespace StardewCompanion.Mod;

internal sealed partial class NativeWorkExecutor
{
    private string DonateBundle(AiInputSimulator input)
    {
        if(Game1.activeClickableMenu is ItemGrabMenu rewards){var collected=CollectNativeMenu(rewards);return collected=="completed"?Finish("bundle_rewards_collected"):collected;}
        if(Game1.currentLocation is not CommunityCenter center){
            var move=_nav.Move("CommunityCenter",new(20,17),input,_interact);return move is "blocked" or "unsupported"?End("community_center_unreachable"):"running";
        }
        if(Game1.activeClickableMenu is not JunimoNoteMenu menu){
            var tile=FindAction(a=>a.StartsWith("JunimoNote")&&a.EndsWith(Num("area").ToString()));
            if(tile==null)return End("requested_bundle_scroll_not_observed");
            var near=Near("CommunityCenter",tile.Value,input);if(near is "blocked" or "unsupported")return End("bundle_scroll_unreachable");if(near=="reached")_interact(tile.Value);return "running";
        }
        _ownedMenu=menu;
        if(menu.scrambledText)return End("junimo_language_not_unlocked");
        if(!menu.specificBundlePage){
            var bundle=menu.bundles.FirstOrDefault(b=>b.bundleIndex==Num("bundleId"));
            if(bundle==null)return End("bundle_not_in_requested_area");
            Call(menu,"setUpBundleSpecificPage",bundle);return "running";
        }
        if(menu.currentPageBundle.bundleIndex!=Num("bundleId"))return End("unexpected_bundle");
        var slot=Num("slot");if(slot<0||slot>=menu.ingredientSlots.Count)return End("invalid_bundle_slot");
        var complete=center.bundles[Num("bundleId")];
        if(slot<complete.Length&&complete[slot]){
            if(Flag("claimReward")&&menu.presentButton!=null){var click=menu.presentButton.bounds.Center;menu.receiveLeftClick(click.X,click.Y);return "running";}
            return Finish("bundle_slot_completed");
        }
        var id=Text("itemId");var item=menu.heldItem??Item(id,Num("quality"));if(item==null)return End("bundle_item_or_quality_missing");
        if(item.modData.ContainsKey("StardewCompanion/Protected"))return End("bundle_item_protected");
        if(_index>=Num("count"))return End("bundle_partial_quantity_not_committed");
        if(menu.heldItem==null){
            var click=menu.inventory.inventory[Game1.player.Items.IndexOf(item)].bounds.Center;
            menu.receiveRightClick(click.X,click.Y);
        }
        if(menu.heldItem==null)return End("bundle_item_not_selected");
        var target=menu.ingredientSlots[slot].bounds.Center;var before=Count(id);menu.receiveLeftClick(target.X,target.Y);_index++;
        if(slot<complete.Length&&complete[slot]){Completed=_index;if(Flag("claimReward"))return "running";return Finish("bundle_donation_verified");}
        return "running";
    }
    private string Museum(AiInputSimulator input)
    {
        if(Game1.currentLocation is not LibraryMuseum museum){
            var move=_nav.Move("ArchaeologyHouse",new(3,8),input,_interact);return move is "blocked" or "unsupported"?End("museum_unreachable"):"running";
        }
        var items=_args.GetProperty("items").EnumerateArray().ToArray();
        if(_index>=items.Length){
            if(!Flag("claimRewards"))return Finish("specified_museum_items_donated");
            if(Game1.activeClickableMenu is ItemGrabMenu rewards){var collected=CollectNativeMenu(rewards);return collected=="completed"?Finish("museum_rewards_collected"):collected;}
            if(Game1.activeClickableMenu is MuseumMenu donation){donation.exitThisMenu();return "running";}
            if(museum.getRewardsForPlayer(Game1.player).Count==0)return Finish("museum_donations_verified_no_unclaimed_rewards");
            if(Game1.activeClickableMenu is DialogueBox rewardDialogue){if(_responses.Count==0)_responses.Enqueue("Collect");return Dialogue(rewardDialogue);}
            var rewardCounter=FindAction(a=>a.Contains("Gunther"));if(rewardCounter==null)return End("museum_counter_not_observed");
            var approach=Near(Map,rewardCounter.Value,input);if(approach is "blocked" or "unsupported")return End("museum_counter_unreachable");if(approach=="reached")_interact(rewardCounter.Value);return "running";
        }
        var entry=items[_index];var id=entry.GetProperty("itemId").GetString()!;var target=new Vector2(entry.GetProperty("x").GetInt32(),entry.GetProperty("y").GetInt32());
        if(museum.museumPieces.Values.Contains(ItemRegistry.GetDataOrErrorItem(id).ItemId)){_index++;return "running";}
        if(Game1.activeClickableMenu is not MuseumMenu menu){
            if(Game1.activeClickableMenu is DialogueBox dialogue){if(_responses.Count==0)_responses.Enqueue("Donate");return Dialogue(dialogue);}
            var counter=FindAction(a=>a.Contains("Museum")||a.Contains("Gunther"));if(counter==null)return End("museum_counter_not_observed");
            var near=Near(museum.NameOrUniqueName,counter.Value,input);if(near is "blocked" or "unsupported")return End("museum_counter_unreachable");if(near=="reached")_interact(counter.Value);return "running";
        }
        _ownedMenu=menu;
        var item=menu.heldItem??Item(id);if(item==null)return End("museum_donation_item_missing");
        if(item.modData.ContainsKey("StardewCompanion/Protected"))return End("museum_donation_item_protected");
        if(!museum.isItemSuitableForDonation(item)||!museum.isTileSuitableForMuseumPiece((int)target.X,(int)target.Y))return End("museum_item_or_display_tile_invalid");
        if(menu.heldItem==null){var slot=menu.inventory.inventory[Game1.player.Items.IndexOf(item)].bounds.Center;menu.receiveLeftClick(slot.X,slot.Y);return "running";}
        var click=ScreenTile((int)target.X,(int)target.Y);ClickWorldTile(menu,click);
        if(!museum.museumPieces.TryGetValue(target,out var donated)||donated!=item.ItemId)return End("museum_donation_not_verified");
        Completed++;_index++;return "running";
    }
    private string PublicAction(AiInputSimulator input)
    {
        if(_action=="complete_public_project")return End("public_project_requires_project_specific_budget_adapter");
        if(Game1.currentLocation.NameOrUniqueName!=Map){var move=_nav.Move(Map,new(3,8),input,_interact);return move is "blocked" or "unsupported"?End("public_action_route_unavailable"):"running";}
        if(Game1.activeClickableMenu is DialogueBox dialogue)return Dialogue(dialogue);
        if(_action=="attend_event"&&Text("activity")=="minigame")return End("event_minigame_not_adapted:"+Text("eventId"));
        if(_action=="attend_event"&&Text("activity")=="shop")return EventShop(input);
        if(_action=="attend_event"&&Game1.currentLocation.currentEvent is {} current){
            if(_wait!=0&&Text("activity")=="enter"){Completed=1;return Finish("event_entered");}
            if(Text("activity")=="leave"){
                var exit=FindAction(a=>a.Contains("Leave")||a.Contains("Exit"));if(exit==null)return End("event_exit_not_observed");
                var near=Near(Map,exit.Value,input);if(near=="reached")_interact(exit.Value);return "running";
            }
        }
        var key=_action=="attend_event"?Text("eventId"):Text("projectId");
        if(_wait!=0&&Game1.activeClickableMenu==null){
            var cost=_moneyBefore-Game1.player.Money;if(cost>Num("maxCost"))return End("native_public_action_cost_mismatch");
            _results.Add(new{state="submitted",projectId=key,actualCost=cost,unlockRequiresObservation=true});Completed=1;return Finish("public_action_submitted_verify_unlock_later");
        }
        var action=FindAction(a=>a.Contains(key,StringComparison.OrdinalIgnoreCase));if(action==null)return End("public_project_action_not_observed:"+key);
        var approach=Near(Map,action.Value,input);if(approach is "blocked" or "unsupported")return End("public_project_unreachable");if(approach!="reached")return "running";
        _moneyBefore=Game1.player.Money;_interact(action.Value);_wait=Environment.TickCount64;return "running";
    }
    private string EventShop(AiInputSimulator input)
    {
        if(Game1.activeClickableMenu is not ShopMenu menu)return End("event_shop_not_open");
        _ownedMenu=menu;var item=menu.forSale.OfType<Item>().FirstOrDefault(i=>i.QualifiedItemId==Text("itemId"));if(item==null)return End("event_shop_item_missing");
        var price=menu.itemPriceAndStock[item].Price;if(price*Num("count")>Num("maxCost"))return End("event_shop_budget_exceeded");
        if(Completed>=Num("count"))return Finish("event_purchase_completed");
        var n=menu.forSale.IndexOf(item);menu.currentItemIndex=Math.Min(n,Math.Max(0,menu.forSale.Count-4));var click=menu.forSaleButtons[n-menu.currentItemIndex].bounds.Center;
        menu.receiveRightClick(click.X,click.Y);
        if(menu.heldItem is Item held){var index=Game1.player.Items.IndexOf(null);if(index<0)return End("event_purchase_inventory_full");var slot=menu.inventory.inventory[index].bounds.Center;menu.receiveLeftClick(slot.X,slot.Y);Completed++;}
        return "running";
    }
    private string Transit(AiInputSimulator input)
    {
        if(Game1.currentLocation.NameOrUniqueName==Map){var destination=_nav.Move(Map,new(Num("x"),Num("y")),input,_interact);if(destination=="reached"){Completed=1;return Finish("travel_destination_observed");}return destination is "blocked" or "unsupported"?End("travel_destination_tile_unreachable"):"running";}
        var method=Text("method");
        if(method=="horse"){
            if(Game1.player.mount==null){var horse=Game1.currentLocation.characters.OfType<StardewValley.Characters.Horse>().FirstOrDefault();if(horse==null)return End("horse_not_observed");
                var near=Near(Game1.currentLocation.NameOrUniqueName,new(horse.TilePoint.X,horse.TilePoint.Y),input);if(near is "blocked" or "unsupported")return End("horse_unreachable");
                if(near=="reached")horse.checkAction(Game1.player,Game1.currentLocation);return "running";}
            return _nav.Move(Map,new(Num("x"),Num("y")),input,_interact) is "blocked" or "unsupported"?End("horse_route_unavailable"):"running";
        }
        if(method is "totem" or "return_scepter"){
            var item=Item(Text("itemId"));if(item==null||item.modData.ContainsKey("StardewCompanion/Protected"))return End("travel_item_missing_or_protected");
            Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(item);input.Tool=true;return "running";
        }
        if(Game1.activeClickableMenu is DialogueBox dialogue){
            if(_responses.Count==0)_responses.Enqueue(method=="minecart"?Map:"Yes");
            return Dialogue(dialogue);
        }
        var source=method=="bus"?"BusStop":method=="boat"?"BoatTunnel":Game1.currentLocation.NameOrUniqueName;
        if(Game1.currentLocation.NameOrUniqueName!=source)return _nav.Move(source,new(9,9),input,_interact) is "blocked" or "unsupported"?End("transport_source_unreachable"):"running";
        var action=FindAction(a=>method=="bus"?a.Contains("Bus"):method=="boat"?a.Contains("Boat"):a.Contains("Minecart",StringComparison.OrdinalIgnoreCase));
        if(action==null)return End("transport_not_observed_or_locked");
        var nearAction=Near(source,action.Value,input);if(nearAction is "blocked" or "unsupported")return End("transport_unreachable");
        if(nearAction=="reached"){
            if(method=="bus"&&(Num("maxCost")<500||Game1.player.Money<500)||method=="boat"&&(Num("maxCost")<1000||Game1.player.Money<1000))return End("transport_budget_exceeded");
            _interact(action.Value);
        }return "running";
    }
}
