using System.Text.Json;

namespace StardewCompanion.Protocol;

public static class ContractValidation
{
    public static void ValidateSchema(JsonElement schema,JsonElement value,string path="arguments")=>Check(value,schema,path);
    public static void Validate(BusinessCapability contract,JsonElement args,bool internalFields=false)
    {
        using var properties=JsonDocument.Parse(contract.PropertiesJson);
        foreach(var key in contract.Required)if(!args.TryGetProperty(key,out _))throw new InvalidOperationException(contract.Name+":missing_parameter:"+key);
        foreach(var field in args.EnumerateObject()){
            if(internalFields&&field.Name=="_bindings")continue;
            if(!properties.RootElement.TryGetProperty(field.Name,out var schema))throw new InvalidOperationException(contract.Name+":unknown_parameter:"+field.Name);
            Check(field.Value,schema,field.Name);
        }
    }
    private static void Check(JsonElement value,JsonElement schema,string path)
    {
        if(schema.TryGetProperty("oneOf",out var variants)){
            var matches=0;foreach(var variant in variants.EnumerateArray())try{Check(value,variant,path);matches++;}catch(InvalidOperationException){ }
            if(matches!=1)throw new InvalidOperationException("parameter_variant:"+path);
        }
        if(schema.TryGetProperty("type",out var type)){
            var types=type.ValueKind==JsonValueKind.Array?type.EnumerateArray().Select(v=>v.GetString()).ToArray():new[]{type.GetString()};
            bool Matches(string? t)=>t switch{
                "null"=>value.ValueKind==JsonValueKind.Null,"object"=>value.ValueKind==JsonValueKind.Object,
                "array"=>value.ValueKind==JsonValueKind.Array,"string"=>value.ValueKind==JsonValueKind.String,
                "boolean"=>value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "integer"=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out _),"number"=>value.ValueKind==JsonValueKind.Number,_=>false};
            if(!types.Any(Matches))throw new InvalidOperationException("parameter_type:"+path);
        }
        if(schema.TryGetProperty("enum",out var allowed)&&!allowed.EnumerateArray().Any(v=>v.ValueKind==value.ValueKind&&(v.ValueKind==JsonValueKind.String?v.GetString()==value.GetString():v.GetRawText()==value.GetRawText())))
            throw new InvalidOperationException("parameter_enum:"+path);
        if(value.ValueKind==JsonValueKind.Number){
            var n=value.GetDouble();
            if(schema.TryGetProperty("minimum",out var min)&&n<min.GetDouble()||schema.TryGetProperty("maximum",out var max)&&n>max.GetDouble())
                throw new InvalidOperationException("parameter_range:"+path);
        }
        if(value.ValueKind==JsonValueKind.Array){
            var count=value.GetArrayLength();
            if(schema.TryGetProperty("minItems",out var min)&&count<min.GetInt32()||schema.TryGetProperty("maxItems",out var max)&&count>max.GetInt32())
                throw new InvalidOperationException("parameter_array_length:"+path);
            if(schema.TryGetProperty("items",out var items)){var index=0;foreach(var element in value.EnumerateArray())Check(element,items,path+"["+index+++"]");}
        }
        if(value.ValueKind==JsonValueKind.Object){
            if(schema.TryGetProperty("required",out var required))foreach(var key in required.EnumerateArray())
                if(!value.TryGetProperty(key.GetString()!,out _))throw new InvalidOperationException("missing_parameter:"+path+"."+key.GetString());
            if(!schema.TryGetProperty("properties",out var props))return;
            foreach(var field in value.EnumerateObject()){
                if(!props.TryGetProperty(field.Name,out var child)){
                    if(schema.TryGetProperty("additionalProperties",out var extra)&&extra.ValueKind==JsonValueKind.False)throw new InvalidOperationException("unknown_parameter:"+path+"."+field.Name);
                    continue;
                }
                Check(field.Value,child,path+"."+field.Name);
            }
        }
    }
}
