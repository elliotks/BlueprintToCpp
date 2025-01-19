using System;
using CUE4Parse.UE4.Objects.UObject;

public static class Utils
{
    public static string GetPrefix(string type)
    {
        return type switch
        {
            "FSoftObjectProperty" => "U",
            "FStructProperty" => "F",
            "FPackageIndex" => "F",
            "FTextProperty" => "F",
            "FObjectProperty" => "U",
            "ResolvedScriptObject" => "U",
            "UBlueprintGeneratedClass" => "A",
            "FActorProperty" => "A",
            _ => ""
        };
    }

    public static string GetUnknownFieldType(FField field)
    {
        string typeName = field.GetType().Name;
        int suffixIndex = typeName.IndexOf("Property", StringComparison.Ordinal);
        if (suffixIndex < 0)
        {
            return typeName;
        }
        return typeName.Substring(1, suffixIndex - 1);
    }

    public static string GetPropertyType(FProperty? property)
    {
        if (property is null) return "None";

        return property switch
        {
            FArrayProperty array => $"TArray<{GetPrefix(array.Inner.GetType().Name)}{GetPropertyType(array.Inner)}{(array.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || array.PropertyFlags.HasFlag(EPropertyFlags.ContainsInstancedReference) ? "*" : string.Empty)}>",
            FByteProperty bt => bt.Enum.ResolvedObject?.Name.Text ?? "Byte",
            FDelegateProperty dlgt => $"{dlgt.SignatureFunction?.Name ?? "Unknown"} (Delegate)",
            FEnumProperty enm => enm.Enum?.Name.ToString() ?? "Enum",
            FFieldPathProperty fieldPath => $"{fieldPath.PropertyClass.Text} field path",
            FInterfaceProperty intrfc => $"{intrfc.InterfaceClass.Name} interface",
            FMapProperty map => $"Map<{GetPropertyType(map.KeyProp)}, {GetPropertyType(map.ValueProp)}>",
            FMulticastDelegateProperty mdlgt => $"{mdlgt.SignatureFunction?.Name ?? "Unknown"} (Multicast Delegate)",
            FMulticastInlineDelegateProperty midlgt => $"{midlgt.SignatureFunction?.Name ?? "Unknown"} (Multicast Inline Delegate)",
            FObjectProperty objct => property switch
            {
                FClassProperty clss => $"{clss.MetaClass?.Name ?? "Unknown"} Class",
                FSoftClassProperty softClass => $"{softClass.MetaClass?.Name ?? "Unknown"} Class (soft)",
                _ => objct.PropertyClass?.Name ?? "Unknown"
            },
            FSetProperty set => $"Set<{GetPropertyType(set.ElementProp)}>",
            FStructProperty strct => strct.Struct.ResolvedObject?.Name.Text ?? "Struct",
            FBoolProperty => "bool",
            FIntProperty => "int",
            FFloatProperty => "float",
            FStrProperty => "FString",
            _ => GetUnknownFieldType(property)
        };
    }
}
