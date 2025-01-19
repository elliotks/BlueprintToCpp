using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Compression;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.Core.Math;
using System.Linq.Expressions;
using System.Collections.Generic;
using Newtonsoft.Json;
using CUE4Parse.UE4.Assets.Exports;
using System.Text.RegularExpressions;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Objects;

public class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            string pakFolderPath = args.ElementAtOrDefault(0) ?? "C:\\Program Files\\Epic Games\\Fortnite\\FortniteGame\\Content\\Paks";
            string usmapPath = args.ElementAtOrDefault(1) ?? "C:\\Users\\krowe\\Downloads\\++Fortnite+Release-33.20-CL-39082670-Windows_oo.usmap";
            string oodlePath = args.ElementAtOrDefault(2) ?? "C:\\Users\\krowe\\Downloads\\Athena\\Athena\\bin\\Debug\\net8.0\\oo2core_5_win64.dll";
            string aesUrl = args.ElementAtOrDefault(3) ?? "https://fortnitecentral.genxgames.gg/api/v1/aes";

            var provider = InitializeProvider(pakFolderPath, usmapPath, oodlePath);
            provider.ReadScriptData = true;
            await LoadAesKeysAsync(provider, aesUrl);

            var package = provider.LoadPackage("FortniteGame/Content/Creative/Devices/MatchmakingPortal/BP_Creative_MatchmakingPortal.uasset") as AbstractUePackage;// FortniteGame/Content/Athena/Prototype/Blueprints/MeshNetwork/BP_MeshNetworkStatusFlare.uasset FortniteGame/Content/Athena/Athena_PlayerController.uasset
            var outputBuilder = new StringBuilder();

            string mainClass = string.Empty;

            var blueprintGeneratedClass = package?.ExportsLazy.Where(export => export.Value is UBlueprintGeneratedClass).Select(export => (UBlueprintGeneratedClass)export.Value).FirstOrDefault();

            if (blueprintGeneratedClass != null)
            {
                mainClass = blueprintGeneratedClass.Name;
                outputBuilder.AppendLine($"class {Utils.GetPrefix(blueprintGeneratedClass.GetType().Name)}{blueprintGeneratedClass.Name} : public {Utils.GetPrefix(blueprintGeneratedClass.GetType().Name)}{blueprintGeneratedClass.SuperStruct.Name}\n{{\npublic:");

                foreach (FProperty property in blueprintGeneratedClass.ChildProperties)
                {
                    outputBuilder.AppendLine($"    {Utils.GetPrefix(property.GetType().Name)}{Utils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) ? "*" : string.Empty)} {property.Name} = {property.Name}placenolder;");
                }

                foreach (var export in package.ExportsLazy)
                {
                    if (export.Value is not UBlueprintGeneratedClass)
                    {
                        if (export.Value.Name.StartsWith("Default__") && export.Value.Name.EndsWith(mainClass))
                        {
                            var exportObject = (UObject)export.Value;

                            foreach (var key in exportObject.Properties)
                            {
                                if (outputBuilder.ToString().Contains($"{key.Name}placenolder"))
                                {
                                    outputBuilder.Replace($"{key.Name}placenolder", key.Tag.GenericValue.ToString());
                                }
                                else
                                {
                                    outputBuilder.AppendLine($"    {Utils.GetPrefix(key.GetType().Name)} {key.Name} = {key.Tag.GenericValue};");
                                }
                            }
                        }
                        else
                        {
                            //outputBuilder.AppendLine($"fix\nvoid {export.Value.Name}");
                        }
                    }
                }

                var funcMapOrder = blueprintGeneratedClass.FuncMap.Keys.Select(fname => fname.ToString()).ToList();

                var functions = package.ExportsLazy
                    .Where(e => e.Value is UFunction)
                    .Select(e => (UFunction)e.Value)
                    .OrderBy(f => {
                        var functionName = f.Name.ToString();
                        int index = funcMapOrder.IndexOf(functionName);
                        return index >= 0 ? index : int.MaxValue;
                    })
                    .ThenBy(f => f.Name.ToString())
                    .ToList();

                foreach (var function in functions)
                {
                    string argsList = "";
                    string returnFunc = "void";
                    foreach (FProperty property in function.ChildProperties)
                    {
                        if (property.Name.PlainText == "ReturnValue")
                        {
                            returnFunc = $"{(property.PropertyFlags.HasFlag(EPropertyFlags.ConstParm) ? "const " : string.Empty)}{Utils.GetPrefix(property.GetType().Name)}{Utils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || Utils.GetPrefix(property.GetType().Name) == "U" ? "*" : string.Empty)}";
                        }
                        else if (!(property.Name.ToString().EndsWith("_ReturnValue") ||
                                  property.Name.ToString().StartsWith("CallFunc_") ||
                                  property.Name.ToString().StartsWith("K2Node_") ||
                                  property.Name.ToString().StartsWith("Temp_")) ||
                                  property.PropertyFlags.HasFlag(EPropertyFlags.Edit))
                        {
                            argsList += $"{(property.PropertyFlags.HasFlag(EPropertyFlags.ConstParm) ? "const " : string.Empty)}{Utils.GetPrefix(property.GetType().Name)}{Utils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || Utils.GetPrefix(property.GetType().Name) == "U" ? "*" : string.Empty)}{(property.PropertyFlags.HasFlag(EPropertyFlags.OutParm) ? "&" : string.Empty)} {property.Name}, ";
                        }
                    }
                    argsList = argsList.TrimEnd(',', ' ');

                    outputBuilder.AppendLine($"\n{returnFunc} {function.Name.Replace(" ", "")}({argsList})\n{{");
                    foreach (KismetExpression property in function.ScriptBytecode)
                    {
                        ProcessExpression(property.Token, property, outputBuilder);
                    }
                }

                outputBuilder.Append("\n\n}");
            }

            int targetIndex = outputBuilder.ToString().IndexOf("placenolder");
            string pattern = $@"\w+placenolder";
            string updatedOutput = Regex.Replace(outputBuilder.ToString(), pattern, "null");
            Console.WriteLine(updatedOutput);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}\n{ex.StackTrace}");
        }
    }

    static DefaultFileProvider InitializeProvider(string pakFolderPath, string usmapPath, string oodlePath)
    {
        OodleHelper.Initialize(oodlePath);

        var provider = new DefaultFileProvider(pakFolderPath, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE5_LATEST))
        {
            MappingsContainer = new FileUsmapTypeMappingsProvider(usmapPath)
        };
        provider.Initialize();

        return provider;
    }

    static async Task LoadAesKeysAsync(DefaultFileProvider provider, string aesUrl)
    {
        string cacheFilePath = "aes_keys_cache.json";

        if (File.Exists(cacheFilePath))
        {
            string cachedAesJson = await File.ReadAllTextAsync(cacheFilePath);
            LoadAesKeysFromJson(provider, cachedAesJson);
        }
        else
        {
            using var webClient = new WebClient();
            string aesJson = await webClient.DownloadStringTaskAsync(aesUrl);
            await File.WriteAllTextAsync(cacheFilePath, aesJson);
            LoadAesKeysFromJson(provider, aesJson);
        }

        provider.PostMount();
        provider.LoadLocalization(ELanguage.English);
    }

    private static void LoadAesKeysFromJson(DefaultFileProvider provider, string aesJson)
    {
        var aesData = JObject.Parse(aesJson);
        string mainKey = aesData["mainKey"]?.ToString() ?? string.Empty;
        provider.SubmitKey(new FGuid(), new FAesKey(mainKey));

        foreach (var key in aesData["dynamicKeys"]?.ToObject<JArray>() ?? new JArray())
        {
            var guid = key["guid"]?.ToString();
            var aesKey = key["key"]?.ToString();
            if (!string.IsNullOrEmpty(guid) && !string.IsNullOrEmpty(aesKey))
            {
                provider.SubmitKey(new FGuid(guid), new FAesKey(aesKey));
            }
        }
    }

    private static void ProcessExpression(EExprToken token, KismetExpression expression, StringBuilder outputBuilder, bool isParameter = false)
    {
        switch (token)
        {
            case EExprToken.EX_LetValueOnPersistentFrame:
                {
                    EX_LetValueOnPersistentFrame op = (EX_LetValueOnPersistentFrame)expression;
                    EX_VariableBase opp = (EX_VariableBase)op.AssignmentExpression;
                    var nerd = string.Join('.', op.DestinationProperty.New.Path.Select(n => n.Text));
                    var nerdd = string.Join('.', opp.Variable.New.Path.Select(n => n.Text));
                    if (!isParameter)
                    {
                        outputBuilder.Append($"\n    {nerd} = {nerdd};\n\n");
                    }
                    else
                    {
                        outputBuilder.Append($"\n    {nerd} = {nerdd}");
                    }
                    break;
                }
            case EExprToken.EX_LocalFinalFunction:
            case EExprToken.EX_FinalFunction:
                {
                    EX_FinalFunction op = (EX_FinalFunction)expression;
                    KismetExpression[] opp = (KismetExpression[])op.Parameters;
                    if (isParameter)
                    {
                        outputBuilder.Append($"{op.StackNode.Name.Replace(" ", "")}(");
                    }
                    else if (opp.Length < 1)
                    {
                        outputBuilder.Append($"    {op?.StackNode?.Name.Replace(" ", "")}(");
                    }
                    else
                    {
                        outputBuilder.Append($"    {Utils.GetPrefix(op?.StackNode?.ResolvedObject?.Outer?.GetType()?.Name)}{op?.StackNode?.Name.Replace(" ", "")}(");
                    }

                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 5) outputBuilder.Append("\n");
                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, true);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    if (isParameter)
                    {
                        outputBuilder.Append($")");
                    }
                    else
                    {
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_CallMath:
                {
                    EX_FinalFunction op = (EX_FinalFunction)expression;
                    KismetExpression[] opp = (KismetExpression[])op.Parameters;
                    outputBuilder.Append($"{Utils.GetPrefix(op.StackNode.ResolvedObject.Outer.GetType().Name)}{op.StackNode.ResolvedObject.Outer.Name.ToString().Replace(" ", "")}::{op.StackNode.Name}(");
                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 5) outputBuilder.Append("\n");
                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, false);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    if (isParameter)
                    {
                        outputBuilder.Append($")");
                    }
                    else
                    {
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_LocalVirtualFunction:
            case EExprToken.EX_VirtualFunction:
                {
                    EX_VirtualFunction op = (EX_VirtualFunction)expression;
                    KismetExpression[] opp = (KismetExpression[])op.Parameters;

                    if (isParameter)
                    {
                        outputBuilder.Append($"{op.VirtualFunctionName.PlainText.Replace(" ", "")}(");
                    }
                    else
                    {
                        outputBuilder.Append($"    {op.VirtualFunctionName.PlainText.Replace(" ", "")}(");
                    }
                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 5) outputBuilder.Append("\n");

                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, true);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    if (isParameter)
                    {
                        outputBuilder.Append($")");
                    }
                    else
                    {
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_ComputedJump:
                {
                    EX_ComputedJump op = (EX_ComputedJump)expression;
                    EX_VariableBase opp = (EX_VariableBase)op.CodeOffsetExpression;
                    outputBuilder.AppendLine($"    goto {string.Join('.', ((EX_VariableBase)op.CodeOffsetExpression).Variable.New.Path.Select(n => n.Text))};\n");
                    break;
                }
            case EExprToken.EX_JumpIfNot:
                {
                    EX_JumpIfNot op = (EX_JumpIfNot)expression;
                    outputBuilder.Append("    if (!");
                    ProcessExpression(op.BooleanExpression.Token, op.BooleanExpression, outputBuilder);
                    outputBuilder.Append(") \r\n");
                    outputBuilder.Append("        goto Label_");
                    outputBuilder.Append(op.CodeOffset);
                    outputBuilder.Append(";\n\n");
                    break;
                }
            case EExprToken.EX_PopExecutionFlowIfNot:
                {
                    EX_PopExecutionFlowIfNot op = (EX_PopExecutionFlowIfNot)expression;
                    outputBuilder.Append("    if (!");
                    ProcessExpression(op.BooleanExpression.Token, op.BooleanExpression, outputBuilder);
                    outputBuilder.Append(") \r\n");
                    outputBuilder.Append($"        FlowStack.Pop();\n\n");
                    break;
                }
            case EExprToken.EX_Cast:
                {
                    EX_Cast op = (EX_Cast)expression;//CST_ObjectToInterface

                    if (ECastToken.CST_ObjectToBool == op.ConversionType|| ECastToken.CST_InterfaceToBool == op.ConversionType)
                    {
                        outputBuilder.Append("(bool)");
                    }
                    if (ECastToken.CST_DoubleToFloat == op.ConversionType)
                    {
                        outputBuilder.Append("(float)");
                    }
                    if (ECastToken.CST_FloatToDouble == op.ConversionType)
                    {
                        outputBuilder.Append("(double)");
                    }
                    ProcessExpression(op.Target.Token, op.Target, outputBuilder);
                    break;
                }
            case EExprToken.EX_InterfaceContext:
                {
                    EX_InterfaceContext op = (EX_InterfaceContext)expression;
                    ProcessExpression(op.InterfaceValue.Token, op.InterfaceValue, outputBuilder);
                    break;
                }
            case EExprToken.EX_NameConst:
                {
                    EX_NameConst op = (EX_NameConst)expression;
                    outputBuilder.Append($"\"{op.Value}\"");
                    break;
                }
            case EExprToken.EX_RotationConst:
                {
                    EX_RotationConst op = (EX_RotationConst)expression;
                    FRotator value = op.Value;
                    outputBuilder.Append($"FRotator({value.Pitch}, {value.Yaw}, {value.Roll})");
                    break;
                }
            case EExprToken.EX_VectorConst:
                {
                    EX_VectorConst op = (EX_VectorConst)expression;
                    FVector value = op.Value;
                    outputBuilder.Append($"FVector({value.X}, {value.Y}, {value.Z})");
                    break;
                }
            case EExprToken.EX_Vector3fConst:
                {
                    EX_Vector3fConst op = (EX_Vector3fConst)expression;
                    FVector value = op.Value;
                    outputBuilder.Append($"FVector3f({value.X}, {value.Y}, {value.Z})");
                    break;
                }
            case EExprToken.EX_StructMemberContext:
                {
                    EX_StructMemberContext op = (EX_StructMemberContext)expression;
                    ProcessExpression(op.StructExpression.Token, op.StructExpression, outputBuilder);
                    outputBuilder.Append(".");
                    outputBuilder.Append(string.Join('.', op.Property.New.Path.Select(n => n.Text)));
                    break;
                }
            case EExprToken.EX_ArrayConst:
                {
                    EX_ArrayConst op = (EX_ArrayConst)expression;
                    outputBuilder.Append("TArray {");
                    foreach (KismetExpression element in op.Elements)
                    {
                        outputBuilder.Append(" ");
                        ProcessExpression(element.Token, element, outputBuilder);
                    }
                    outputBuilder.Append("}");
                    break;
                }
            case EExprToken.EX_SetArray:
                {
                    EX_SetArray op = (EX_SetArray)expression;
                    outputBuilder.Append("TArray {");
                    foreach (KismetExpression element in op.Elements)
                    {
                        outputBuilder.Append(" ");
                        ProcessExpression(element.Token, element, outputBuilder);
                        outputBuilder.Append(" ");
                    }
                    if (op.Elements.Length < 0) outputBuilder.Append("  ");
                    outputBuilder.Append("}\n");
                    break;
                }
            case EExprToken.EX_SwitchValue:
                {
                    EX_SwitchValue op = (EX_SwitchValue)expression;
                    ProcessExpression(op.IndexTerm.Token, op.IndexTerm, outputBuilder);
                    outputBuilder.Append(" ? ");
                    for (int caseIndex = 0; caseIndex < op.Cases.Length; ++caseIndex)
                    {
                        if (caseIndex != op.Cases.Length - 1 || caseIndex != 0) outputBuilder.Append(" : ");
                        ProcessExpression(op.Cases[caseIndex].CaseTerm.Token, op.Cases[caseIndex].CaseTerm, outputBuilder);
                    }

                    break;
                }
            case EExprToken.EX_ArrayGetByRef:
                {
                    EX_ArrayGetByRef op = (EX_ArrayGetByRef)expression;
                    ProcessExpression(op.ArrayVariable.Token, op.ArrayVariable, outputBuilder);
                    ProcessExpression(op.ArrayIndex.Token, op.ArrayIndex, outputBuilder);
                    break;
                }
            case EExprToken.EX_MetaCast:
            case EExprToken.EX_DynamicCast:
            case EExprToken.EX_ObjToInterfaceCast:
            case EExprToken.EX_CrossInterfaceCast:
            case EExprToken.EX_InterfaceToObjCast:
                {
                    EX_CastBase op = (EX_CastBase)expression;
                    outputBuilder.Append($"Cast<U{op.ClassPtr.Name}*>(");
                    ProcessExpression(op.Target.Token, op.Target, outputBuilder);
                    outputBuilder.Append(")");
                    break;
                }
            case EExprToken.EX_StructConst:
                {
                    EX_StructConst op = (EX_StructConst)expression;
                    outputBuilder.Append($"{Utils.GetPrefix(op.Struct.GetType().Name)}{op.Struct.Name}");
                    outputBuilder.Append($"(");
                    for (int i = 0; i < op.Properties.Length; i++)
                    {
                        var property = op.Properties[i];
                        ProcessExpression(property.Token, property, outputBuilder);
                        if (i < op.Properties.Length - 1 && property.Token != EExprToken.EX_ArrayConst)
                            outputBuilder.Append(", ");
                    }
                    outputBuilder.Append($")");
                    break;
                }
            case EExprToken.EX_LocalVariable:
            case EExprToken.EX_DefaultVariable:
            case EExprToken.EX_InstanceVariable:
            case EExprToken.EX_LocalOutVariable:
            case EExprToken.EX_ClassSparseDataVariable:
                {
                    EX_VariableBase op = (EX_VariableBase)expression;
                    outputBuilder.Append(string.Join('.', op.Variable.New.Path.Select(n => n.Text)).Replace(" ",""));
                    break;
                }
            case EExprToken.EX_IntConst:
                {
                    EX_IntConst op = (EX_IntConst)expression;
                    outputBuilder.Append(op.Value.ToString());
                    break;
                }
            case EExprToken.EX_ObjectConst:
                {
                    EX_ObjectConst op = (EX_ObjectConst)expression;
                    outputBuilder.Append("LoadObject<");
                    string classString = op?.Value?.ResolvedObject?.Class?.ToString()?.Replace("'", "");
                    if (classString?.Contains(".") == true)
                    {
                        outputBuilder.Append("U" + classString.Split(".")[1]);
                    }
                    else
                    {
                        outputBuilder.Append("U" + classString); // renove gardicded
                    }
                    outputBuilder.Append(">(\"");
                    outputBuilder.Append(
                        op.Value.ResolvedObject.Outer
                            .ToString()
                            .Replace("'", "")
                            .Replace(op.Value.ResolvedObject.Class.ToString().Replace("'", ""), "") +
                        "." +
                        op.Value.Name
                    );
                    outputBuilder.Append("\")");
                    break;
                }
            case EExprToken.EX_ByteConst:
            case EExprToken.EX_IntConstByte:
                {
                    KismetExpression<byte> op = (KismetExpression<byte>)expression;
                    outputBuilder.Append("0x" + op.Value.ToString("X"));
                    break;
                }
            case EExprToken.EX_BindDelegate:
                {
                    EX_BindDelegate op = (EX_BindDelegate)expression;
                    outputBuilder.Append("    ");
                    ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder);
                    outputBuilder.Append($".BindUFunction(");
                    ProcessExpression(op.ObjectTerm.Token, op.ObjectTerm, outputBuilder);
                    outputBuilder.Append($", \"{op.FunctionName}\"");
                    outputBuilder.Append($");\n\n");
                    break;
                }
            case EExprToken.EX_AddMulticastDelegate:
                {
                    EX_AddMulticastDelegate op = (EX_AddMulticastDelegate)expression;
                    EX_Context opp = (EX_Context)op.Delegate;
                    outputBuilder.Append("    ");
                    ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                    outputBuilder.Append("->");
                    ProcessExpression(opp.ContextExpression.Token, opp.ContextExpression, outputBuilder);
                    outputBuilder.Append(".AddDelegate(");
                    ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder);
                    outputBuilder.Append($");\n\n");
                    break;
                }
            case EExprToken.EX_ClassContext:
            case EExprToken.EX_Context:
                {
                    EX_Context op = (EX_Context)expression;
                    ProcessExpression(op.ObjectExpression.Token, op.ObjectExpression, outputBuilder, true);
                    //if (!isParameter)
                    //{
                        outputBuilder.Append("->");
                        ProcessExpression(op.ContextExpression.Token, op.ContextExpression, outputBuilder, true);
                    //}
                    break;
                }
            case EExprToken.EX_Context_FailSilent:
                {
                    EX_Context op = (EX_Context)expression;
                    outputBuilder.Append("    ");
                    ProcessExpression(op.ObjectExpression.Token, op.ObjectExpression, outputBuilder, true);
                    if (!isParameter)
                    {
                        outputBuilder.Append("->");
                        ProcessExpression(op.ContextExpression.Token, op.ContextExpression, outputBuilder, true);
                        outputBuilder.Append($"\n\n");
                    }
                    break;
                }
            case EExprToken.EX_Jump:
                {
                    EX_Jump op = (EX_Jump)expression;
                    outputBuilder.Append($"    goto Label_{op.CodeOffset};\n\n");
                    break;
                }
            case EExprToken.EX_Let:
                {
                    EX_Let op = (EX_Let)expression;
                    if (!isParameter)
                    {
                        outputBuilder.Append("    ");
                    }
                    ProcessExpression(op.Variable.Token, op.Variable, outputBuilder);
                    outputBuilder.Append(" = ");
                    ProcessExpression(op.Assignment.Token, op.Assignment, outputBuilder, true);
                    if (!isParameter)
                    {
                        outputBuilder.Append(";\n\n");
                    }
                    break;
                }
            case EExprToken.EX_LetObj:
            case EExprToken.EX_LetWeakObjPtr:
            case EExprToken.EX_LetBool:
            case EExprToken.EX_LetDelegate:
            case EExprToken.EX_LetMulticastDelegate:
                {
                    EX_LetBase op = (EX_LetBase)expression;
                    if (!isParameter)
                    {
                        outputBuilder.Append("    ");
                    }
                    ProcessExpression(op.Variable.Token, op.Variable, outputBuilder);
                    outputBuilder.Append(" = ");
                    ProcessExpression(op.Assignment.Token, op.Assignment, outputBuilder, true);
                    if (!isParameter || op.Assignment.Token == EExprToken.EX_LocalFinalFunction || op.Assignment.Token == EExprToken.EX_FinalFunction || op.Assignment.Token == EExprToken.EX_CallMath)
                    {
                        outputBuilder.Append($";\n\n");
                    }
                    else
                    {
                        outputBuilder.Append($";");
                    }
                    break;
                }
            // Static
            case EExprToken.EX_Int64Const:
                outputBuilder.Append(((EX_Int64Const)expression).Value.ToString());
                break;
            case EExprToken.EX_UInt64Const:
                outputBuilder.Append(((EX_UInt64Const)expression).Value.ToString());
                break;
            case EExprToken.EX_SkipOffsetConst:
                outputBuilder.Append(((EX_SkipOffsetConst)expression).Value.ToString());
                break;
            case EExprToken.EX_FloatConst:
                outputBuilder.Append(((EX_FloatConst)expression).Value.ToString());
                break;
            case EExprToken.EX_TextConst:
                outputBuilder.Append(((EX_TextConst)expression).Value.ToString());
                break;
            case EExprToken.EX_DoubleConst:
                {
                    var value = ((EX_DoubleConst)expression).Value;
                    outputBuilder.Append(value == (int)value ? (int)value : value.ToString("0.0###"));
                    break;
                }
            case EExprToken.EX_StringConst:
                outputBuilder.Append($"\"{((EX_StringConst)expression).Value}\"");
                break;
            case EExprToken.EX_UnicodeStringConst:
                outputBuilder.Append(((EX_UnicodeStringConst)expression).Value);
                break;
            case EExprToken.EX_Return:
                {
                    EX_Return op = (EX_Return)expression;
                    bool tocheck = op.ReturnExpression.Token == EExprToken.EX_Nothing;
                    outputBuilder.Append($"\n    return");
                    if (!tocheck) outputBuilder.Append(" ");
                    ProcessExpression(op.ReturnExpression.Token, op.ReturnExpression, outputBuilder, true);
                    outputBuilder.AppendLine(";\n\n");
                    break;
                }
            case EExprToken.EX_EndOfScript:
            case EExprToken.EX_EndParmValue:
                outputBuilder.AppendLine("}");
                break;
            case EExprToken.EX_NoObject:
            case EExprToken.EX_NoInterface:
                outputBuilder.Append("nullptr");
                break;
            case EExprToken.EX_IntOne:
                outputBuilder.Append(1);
                break;
            case EExprToken.EX_True:
                outputBuilder.Append("true");
                break;
            case EExprToken.EX_IntZero:
                outputBuilder.Append(0);
                break;
            case EExprToken.EX_False:
                outputBuilder.Append("false");
                break;
            case EExprToken.EX_Self:
                outputBuilder.Append("this");
                break;
            case EExprToken.EX_Nothing:
            case EExprToken.EX_PopExecutionFlow:
            case EExprToken.EX_PushExecutionFlow:
            case EExprToken.EX_CallMulticastDelegate:
            case EExprToken.EX_RemoveMulticastDelegate:
                break;
            default:
                outputBuilder.Append($"{token}");
                break;
        }
    }
}
