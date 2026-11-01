using StardewCompanion.Protocol;
using StardewValley;

namespace StardewCompanion.Mod;

internal sealed partial class NativeWorkExecutor
{
    private int _machineStage;
    private string _machineItemId="";
    private int _machineItemBefore;
    private string? _machineFuelId;
    private int _machineFuelBefore;
    private string ServiceMachine(AiInputSimulator input)
    {
        var move=Near(Map,Tile,input);if(move is "blocked" or "unsupported")return End("machine_unreachable");if(move!="reached")return "running";
        if(!Game1.currentLocation.objects.TryGetValue(new(Tile.X,Tile.Y),out var machine))return End("machine_missing");
        var mode=Text("mode","collect");
        if(_machineStage==0){
            if(mode!="load"&&machine.readyForHarvest.Value&&machine.heldObject.Value is {} output){
                if(!Game1.player.couldInventoryAcceptThisItem(output))return End("machine_output_inventory_full");
                _machineItemId=output.QualifiedItemId;_machineItemBefore=Count(_machineItemId);Game1.player.CurrentToolIndex=EmptyHand();_interact(Tile);
                _machineStage=1;_wait=Environment.TickCount64+700;return "running";
            }
            if(mode=="collect")return Finish("machine_not_ready");
            _machineStage=2;
        }
        if(_machineStage==1){
            if(Environment.TickCount64<_wait)return "running";
            var collected=Count(_machineItemId)-_machineItemBefore;
            if(collected<=0||machine.readyForHarvest.Value)return End("machine_output_not_verified");
            _results.Add(new{outputId=_machineItemId,collected});Completed++;
            if(mode=="collect")return Finish("machine_output_collected");
            _machineStage=2;
        }
        if(_machineStage==2){
            if(machine.heldObject.Value!=null)return machine.readyForHarvest.Value?End("machine_output_requires_collection"):Finish("machine_already_processing");
            var id=Body.GetProperty("inputId").GetString();if(id==null)return End("machine_input_unspecified");
            var item=Item(id,Body.GetProperty("quality").GetInt32());if(item==null||item.modData.ContainsKey("StardewCompanion/Protected"))return End("machine_input_missing_or_protected");
            _machineFuelId=Body.TryGetProperty("fuelId",out var fuel)&&fuel.ValueKind==System.Text.Json.JsonValueKind.String?fuel.GetString():null;
            if(_machineFuelId!=null&&(Item(_machineFuelId)==null||Item(_machineFuelId)!.modData.ContainsKey("StardewCompanion/Protected")))return End("machine_fuel_missing_or_protected");
            _machineItemId=id;_machineItemBefore=Count(id);_machineFuelBefore=_machineFuelId==null?0:Count(_machineFuelId);
            Game1.player.CurrentToolIndex=Game1.player.Items.IndexOf(item);_interact(Tile);_machineStage=3;_wait=Environment.TickCount64+700;return "running";
        }
        if(Environment.TickCount64<_wait)return "running";
        var consumed=_machineItemBefore-Count(_machineItemId);var fuelConsumed=_machineFuelId==null?0:_machineFuelBefore-Count(_machineFuelId);
        if(machine.heldObject.Value==null||consumed<=0)return End("machine_input_not_verified");
        _results.Add(new{inputId=_machineItemId,consumed,fuelId=_machineFuelId,fuelConsumed,minutesRemaining=machine.MinutesUntilReady});Completed++;
        if(_machineFuelId!=null&&fuelConsumed<=0)return End("requested_machine_fuel_not_consumed");
        return Finish("machine_processing_verified");
    }
}
