using CUE4Parse.UE4.Objects.UObject;

namespace BlueRange.Utils;
public static class SomeUtils
{
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
            FSetProperty set => $"Set<{GetPropertyType(set.ElementProp)}>",
            FByteProperty bt => bt.Enum.ResolvedObject?.Name.Text ?? "Byte",
            FInterfaceProperty intrfc => $"{intrfc.InterfaceClass.Name} interface",
            FStructProperty strct => strct.Struct.ResolvedObject?.Name.Text ?? "Struct",
            FFieldPathProperty fieldPath => $"{fieldPath.PropertyClass.Text} field path",
            FDelegateProperty dlgt => $"{dlgt.SignatureFunction?.Name ?? "UNKNOWN"} (Delegate)",
            FMapProperty map => $"TMap<{GetPropertyType(map.KeyProp)}, {GetPropertyType(map.ValueProp)}>",
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
