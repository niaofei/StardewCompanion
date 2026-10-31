using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewCompanion.Protocol;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;

namespace StardewCompanion.Mod;

internal sealed partial class NativeWorkExecutor
{
    private string Visit(AiInputSimulator input)
    {
        if(Game1.activeClickableMenu is DialogueBox dialogue)return Dialogue(dialogue);
        if(Game1.currentLocation.NameOrUniqueName!=Map){var move=_nav.Move(Map,new(3,8),input,_interact);return move is "blocked" or "unsupported"?End("npc_location_unreachable"):"running";}
        var npc=Game1.currentLocation.characters.FirstOrDefault(n=>n.Name==Text("npc"));
        if(npc==null)return End("npc_not_observed_here:"+Text("npc"));
        var point=new GridPoint(npc.TilePoint.X,npc.TilePoint.Y);var near=Near(Map,point,input);
        if(near is "blocked" or "unsupported")return End("npc_unreachable");if(near!="reached")return "running";
        if(_index==0&&Flag("talk")){
            Game1.player.CurrentToolIndex=EmptyHand();npc.checkAction(Game1.player,Game1.currentLocation);_index=1;Completed++;return "running";
        }
        if(Text("giftId")!=""){
            if(_index<2){
                var gift=Item(Text("giftId"),Num("quality")) as StardewValley.Object;
                if(gift==null||gift.modData.ContainsKey("StardewCompanion/Protected"))return End("gift_missing_or_protected");
                _before=Count(gift.QualifiedItemId);Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(gift);
                if(!npc.tryToReceiveActiveObject(Game1.player))return End("native_gift_limit_or_rejection");
                if(Count(gift.QualifiedItemId)>=_before)return End("gift_consumption_not_verified");
                _index=2;Completed++;return "running";
            }
        }
        return Finish("requested_villager_interactions_completed");
    }
    private string Daily(AiInputSimulator input)
    {
        var sources=_args.GetProperty("sources").EnumerateArray().Select(s=>s.GetString()!).ToArray();
        if(_index>=sources.Length)return Finish("accessible_daily_sources_read");
        var source=sources[_index];
        if(source=="quests"){
            _results.Add(new{source,quests=Game1.player.questLog.Select(q=>new{id=q.id.Value,title=q.questTitle,completed=q.completed.Value}).ToArray()});
            _index++;Completed++;return "running";
        }
        if(Game1.activeClickableMenu is DialogueBox dialogue)return Dialogue(dialogue);
        if(Game1.activeClickableMenu is LetterViewerMenu letter){
            _ownedMenu=letter;_results.Add(new{source,content=Read(letter,"mailMessage")?.ToString()});
            if(letter.readyToClose()){letter.exitThisMenu();_index++;Completed++;}return "running";
        }
        if(Game1.activeClickableMenu is Billboard board){
            _ownedMenu=board;_results.Add(new{source,calendarObserved=true,quest=Game1.questOfTheDay?.questTitle});
            board.exitThisMenu();_index++;Completed++;return "running";
        }
        var location=source=="mail"?"Farm":source is "calendar" or "help_wanted" or "special_orders"?"Town":Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName;
        if(Game1.currentLocation.NameOrUniqueName!=location){var move=_nav.Move(location,new(3,8),input,_interact);return move is "blocked" or "unsupported"?End("daily_source_route_unavailable:"+source):"running";}
        GridPoint? tile;
        if(source is "weather" or "fortune" or "recipes"){
            var tv=Game1.currentLocation.furniture.OfType<TV>().FirstOrDefault();if(tv==null)return End("television_not_observed");
            tile=new((int)tv.TileLocation.X,(int)tv.TileLocation.Y);
            if(_responses.Count==0)_responses.Enqueue(source=="weather"?"Weather":source=="fortune"?"Fortune":"The Queen of Sauce");
        }else tile=FindAction(a=>source=="mail"?a.Contains("Mailbox"):source=="calendar"?a.Contains("Billboard"):source=="help_wanted"?a.Contains("Quest")||a.Contains("Billboard"):a.Contains("SpecialOrders"));
        if(tile==null)return End("daily_source_not_observed:"+source);
        if(_wait!=0&&Game1.activeClickableMenu==null){_index++;Completed++;_wait=0;return "running";}
        var near=Near(location,tile.Value,input);if(near is "blocked" or "unsupported")return End("daily_source_unreachable");
        if(near=="reached"){_interact(tile.Value);_wait=Environment.TickCount64;}return "running";
    }
    private string Quests(AiInputSimulator input)
    {
        var quest=Game1.player.questLog.FirstOrDefault(q=>q.id.Value==Text("questId"));
        if(Text("operation")=="accept"){
            var tile=FindAction(a=>a.Contains("Quest")||a.Contains("SpecialOrders"));
            if(Game1.activeClickableMenu is Billboard board){
                _ownedMenu=board;var accept=Read(board,"acceptQuestButton") as ClickableComponent;
                if(accept==null)return End("quest_accept_option_not_available");
                var p=accept.bounds.Center;board.receiveLeftClick(p.X,p.Y);
                if(Game1.player.questLog.Any(q=>q.id.Value==Text("questId"))){Completed=1;return Finish("quest_accepted");}
                return End("requested_quest_not_accepted");
            }
            if(tile==null)return End("quest_board_not_observed");
            var near=Near(Map,tile.Value,input);if(near is "blocked" or "unsupported")return End("quest_board_unreachable");if(near=="reached")_interact(tile.Value);return "running";
        }
        if(quest==null)return End("quest_not_active");
        if(Text("operation")=="deliver"){
            var npc=Game1.currentLocation.characters.FirstOrDefault(n=>n.Name==Text("npc"));if(npc==null)return End("quest_npc_not_observed");
            var tile=new GridPoint(npc.TilePoint.X,npc.TilePoint.Y);var near=Near(Map,tile,input);if(near is "blocked" or "unsupported")return End("quest_npc_unreachable");if(near!="reached")return "running";
            if(Game1.activeClickableMenu is DialogueBox dialogue)return Dialogue(dialogue);
            var requests=_args.GetProperty("items").EnumerateArray().ToArray();
            if(_index<requests.Length){
                var r=requests[_index];var item=Item(r.GetProperty("itemId").GetString()!,r.GetProperty("quality").GetInt32());
                if(item==null)return End("quest_delivery_item_missing");
                Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(item);npc.tryToReceiveActiveObject(Game1.player);_index++;return "running";
            }
            if(quest.completed.Value){Completed=1;return Finish("quest_delivery_verified");}return End("quest_delivery_conditions_not_satisfied");
        }
        if(Text("operation")=="claim"&&!quest.completed.Value)return End("quest_not_complete");
        var page=new QuestLog();Game1.activeClickableMenu=page;_ownedMenu=page;
        return QuestMenuOperation(page,quest);
    }
    private string QuestMenuOperation(QuestLog menu,StardewValley.Quests.Quest quest)
    {
        // Use the journal's own selection and claim/cancel handlers rather than editing gold or quest state.
        var pages=Read(menu,"pages") as System.Collections.IList;
        if(pages==null)return End("native_quest_journal_layout_unavailable");
        for(var i=0;i<pages.Count;i++){
            if(pages[i] is not System.Collections.IEnumerable entries)continue;
            var index=0;foreach(var entry in entries){
                if(ReferenceEquals(entry,quest)){Set(menu,"currentPage",i);Call(menu,"ShowQuest",index);var button=Read(menu,Text("operation")=="claim"?"rewardBox":"cancelQuestButton") as ClickableComponent;
                    if(button==null)return End("quest_requested_action_not_available");
                    var point=button.bounds.Center;menu.receiveLeftClick(point.X,point.Y);Completed=1;return Finish("quest_journal_action_submitted");}
                index++;
            }
        }
        return End("quest_journal_entry_not_found");
    }
}
