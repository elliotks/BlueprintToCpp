using System;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using static System.Runtime.InteropServices.JavaScript.JSType;
public class Config
{
    public string PakFolderPath { get; set; }
    public string BlueprintPath { get; set; }
    public string OodlePath { get; set; }
    public string UsmapPath { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public EGame Version { get; set; }
}
public static class Utils
{
    public static Config LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Config file created, please modify the values.");
            var defaultConfig = new Config
            {
                PakFolderPath = "",
                BlueprintPath = "",
                OodlePath = "",
                UsmapPath = "",
                Version = EGame.GAME_UE5_LATEST
            };

            string jsonTxt = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            File.WriteAllText(path, jsonTxt);
            return defaultConfig;
        }

        string json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<Config>(json);
    }

    public static string GetPrefix(string type)
    {
        return type switch
        {
            "FNameProperty" or "FPackageIndex" or "FTextProperty" or "FStructProperty" => "F",
            "UBlueprintGeneratedClass" or "FActorProperty" => "A",
            "ResolvedScriptObject" or "FSoftObjectProperty" or "FObjectProperty" => "U",
            _ => ""
        };
    }

    // These two functions were taken from
    // https://github.com/CrystalFerrai/UeBlueprintDumper/blob/main/UeBlueprintDumper/BlueprintDumper.cs#L352
    // nothing else in this repository is from UeBlueprintDumper

    public static string GetUnknownFieldType(FField field)
    {
        string typeName = field.GetType().Name;
        int suffixIndex = typeName.IndexOf("Property", StringComparison.Ordinal);
        if (suffixIndex < 0) return typeName;
        return typeName.Substring(1, suffixIndex - 1);
    }

    public static string GetPropertyType(FProperty? property)
    {
        if (property is null)
            return "None";

        return property switch
        {
            FIntProperty => "int",
            FBoolProperty => "bool",
            FStrProperty => "FString",
            FFloatProperty => "float",
            FDoubleProperty => "double",
            FObjectProperty objct => property switch
            {
                FClassProperty clss => $"{clss.MetaClass?.Name ?? "UNKNOWN"} Class",
                FSoftClassProperty softClass => $"{softClass.MetaClass?.Name ?? "UNKNOWN"} Class (soft)",
                _ => objct.PropertyClass?.Name ?? "UNKNOWN"
            },
            FEnumProperty enm => enm.Enum?.Name.ToString() ?? "Enum",
            FSetProperty set => $"TSet<{GetPrefix(set.ElementProp.GetType().Name)}{GetPropertyType(set.ElementProp)}{(set.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || property.PropertyFlags.HasFlag(EPropertyFlags.ReferenceParm) || set.PropertyFlags.HasFlag(EPropertyFlags.ContainsInstancedReference) ? "*" : string.Empty)}>",
            FByteProperty bt => bt.Enum.ResolvedObject?.Name.Text ?? "Byte",
            FInterfaceProperty intrfc => $"{intrfc.InterfaceClass.Name} interface",
            FStructProperty strct => strct.Struct.ResolvedObject?.Name.Text ?? "Struct",
            FFieldPathProperty fieldPath => $"{fieldPath.PropertyClass.Text} field path",
            FDelegateProperty dlgt => $"{dlgt.SignatureFunction?.Name ?? "UNKNOWN"} (Delegate)",
            FMapProperty map => $"TMap<{GetPrefix(map.ValueProp.GetType().Name)}{GetPropertyType(map.KeyProp)}, {GetPrefix(map.ValueProp.GetType().Name)}{GetPropertyType(map.ValueProp)}{(map.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || property.PropertyFlags.HasFlag(EPropertyFlags.ReferenceParm) || map.PropertyFlags.HasFlag(EPropertyFlags.ContainsInstancedReference) ? "*" : string.Empty)}>",
            FMulticastDelegateProperty mdlgt => $"{mdlgt.SignatureFunction?.Name ?? "UNKNOWN"} (MulticastDelegateProperty)",
            FMulticastInlineDelegateProperty midlgt => $"{midlgt.SignatureFunction?.Name ?? "UNKNOWN"} (MulticastInlineDelegateProperty)",
            FArrayProperty array => $"TArray<{GetPrefix(array.Inner.GetType().Name)}{GetPropertyType(array.Inner)}{(array.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || property.PropertyFlags.HasFlag(EPropertyFlags.ReferenceParm) || array.PropertyFlags.HasFlag(EPropertyFlags.ContainsInstancedReference) ? "*" : string.Empty)}>",
            _ => GetUnknownFieldType(property)
        };
    }

    public static bool GetPropertyProperty(FProperty? property)
    {
        if (property is null)
            return false;

        return property switch
        {
            FObjectProperty objct => true,
            _ => false
        };
    }
}
