using System;
using CUE4Parse.UE4.Objects.UObject;

public static class Utils
{
    public static string GetPrefix(string type)
    {
        return type switch
        {
            "FPackageIndex" => "F",
            "FTextProperty" => "F",
            "FActorProperty" => "A",
            "FStructProperty" => "F",
            "FObjectProperty" => "U",
            "FSoftObjectProperty" => "U",
            "ResolvedScriptObject" => "U",
            "UBlueprintGeneratedClass" => "A",
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
        if (property is null) return "None";

        return property switch
        {
            FIntProperty => "int",
            FBoolProperty => "bool",
            FFloatProperty => "float",
            FStrProperty => "FString",
            FObjectProperty objct => property switch
            {
                FClassProperty clss => $"{clss.MetaClass?.Name ?? "Unknown"} Class",
                FSoftClassProperty softClass => $"{softClass.MetaClass?.Name ?? "Unknown"} Class (soft)",
                _ => objct.PropertyClass?.Name ?? "Unknown"
            },
            FEnumProperty enm => enm.Enum?.Name.ToString() ?? "Enum",
            FSetProperty set => $"Set<{GetPropertyType(set.ElementProp)}>",
            FByteProperty bt => bt.Enum.ResolvedObject?.Name.Text ?? "Byte",
            FInterfaceProperty intrfc => $"{intrfc.InterfaceClass.Name} interface",
            FStructProperty strct => strct.Struct.ResolvedObject?.Name.Text ?? "Struct",
            FFieldPathProperty fieldPath => $"{fieldPath.PropertyClass.Text} field path",
            FDelegateProperty dlgt => $"{dlgt.SignatureFunction?.Name ?? "Unknown"} (Delegate)",
            FMapProperty map => $"Map<{GetPropertyType(map.KeyProp)}, {GetPropertyType(map.ValueProp)}>",
            FMulticastDelegateProperty mdlgt => $"{mdlgt.SignatureFunction?.Name ?? "Unknown"} (Multicast Delegate)",
            FMulticastInlineDelegateProperty midlgt => $"{midlgt.SignatureFunction?.Name ?? "Unknown"} (Multicast Inline Delegate)",
            FArrayProperty array => $"TArray<{GetPrefix(array.Inner.GetType().Name)}{GetPropertyType(array.Inner)}{(array.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || array.PropertyFlags.HasFlag(EPropertyFlags.ContainsInstancedReference) ? "*" : string.Empty)}>",
            _ => GetUnknownFieldType(property)
        };
    }
}
