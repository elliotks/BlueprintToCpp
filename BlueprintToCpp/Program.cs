using System.Text;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Assets.Exports;
using System.Text.RegularExpressions;
using BlueRange.Services;
using BlueRange.Settings;
using BlueRange.Utils;
using CUE4Parse.UE4.Assets.Exports.Verse;
using Serilog;

namespace BlueRange;

public static class Program
{
    private static bool verseTest = false;
    private static DefaultFileProvider Provider => ApplicationService.CUE4Parse.Provider;

    public static async Task Main(string[] args)
    {
        try
        {
            await ApplicationService.Initialize().ConfigureAwait(false);
            await ApplicationService.CUE4Parse.InitializeAsync().ConfigureAwait(false);

            await AppSettings.Save().ConfigureAwait(false);

#if DEBUG
            var blueprintPath = "";
#else
            var blueprintPath = AnsiConsole.Prompt(new TextPrompt<string>("Please enter the [green]blueprint path[/]:")
                .PromptStyle("green"));
#endif
            var package = await Provider.LoadPackageAsync(blueprintPath).ConfigureAwait(false);
            if (package is not AbstractUePackage abstractPackage)
            {
                Log.Error("Package is not of type AbstractUePackage but instead of type '{0}'", package.GetType());
                return;
            }

            var outputBuilder = new StringBuilder();

            string mainClass = string.Empty;

            var blueprintGeneratedClass = package?.ExportsLazy.Where(export => export.Value is UBlueprintGeneratedClass).Select(export => (UBlueprintGeneratedClass)export.Value).FirstOrDefault();

            if (blueprintGeneratedClass != null)
            {
                mainClass = blueprintGeneratedClass.Name;
                outputBuilder.AppendLine($"class {SomeUtils.GetPrefix(blueprintGeneratedClass.GetType().Name)}{blueprintGeneratedClass.Name} : public {SomeUtils.GetPrefix(blueprintGeneratedClass.GetType().Name)}{blueprintGeneratedClass.SuperStruct.Name}\n{{\npublic:");

                foreach (FProperty property in blueprintGeneratedClass.ChildProperties)
                {
                    outputBuilder.AppendLine($"    {SomeUtils.GetPrefix(property.GetType().Name)}{SomeUtils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || property.PropertyFlags.HasFlag(EPropertyFlags.ReferenceParm) || SomeUtils.GetPropertyProperty(property) ? "*" : string.Empty)} {property.Name} = {property.Name}placenolder;");
                }

                foreach (var export in package.ExportsLazy)
                {
                    if (export.Value is not UBlueprintGeneratedClass)
                    {
                        if (export.Value.Name.StartsWith("Default__") && export.Value.Name.EndsWith(mainClass))
                        {
                            var exportObject = (UObject) export.Value;

                            foreach (var key in exportObject.Properties)
                            {
                                if (outputBuilder.ToString().Contains($"{key.Name}placenolder"))
                                {
                                    if (key.Tag.GetType().Name == "ObjectProperty" || key.PropertyType == "ObjectProperty" || key.PropertyType == "StrProperty" || key.PropertyType == "NameProperty" || key.PropertyType == "ClassProperty")
                                    {
                                        outputBuilder.Replace($"{key.Name}placenolder", $"\"{key.Tag.GenericValue.ToString()}\"");
                                    }
                                    else
                                    if (key.Tag.GetType().Name == "BoolProperty")
                                    {
                                        outputBuilder.Replace($"{key.Name}placenolder", $"{key.Tag.GenericValue.ToString().ToLower()}");
                                    }
                                    else
                                    {
                                        //Console.WriteLine(key.Name);
                                        //Console.WriteLine(key.Tag.GetType().Name);
                                        outputBuilder.Replace($"{key.Name}placenolder", key.Tag.GenericValue.ToString());
                                    }

                                }
                                else
                                { // findout how to setup types for propertytag and this is a mess
                                    if (key.Tag.GetType().Name == "ObjectProperty" || key.PropertyType == "StructProperty" || key.PropertyType == "StrProperty" || key.PropertyType == "NameProperty" || key.PropertyType == "ClassProperty")
                                    {
                                        outputBuilder.AppendLine($"    {SomeUtils.GetPrefix(key.GetType().Name)} {key.Name} = \"{key.Tag.GenericValue}\";");
                                    }
                                    else
                                        if (key.Tag.GetType().Name == "BoolProperty")
                                    {
                                        outputBuilder.Replace($"{key.Name}placenolder", $"{key.Tag.GenericValue.ToString().ToLower()}");
                                    }
                                    else
                                    {
                                        outputBuilder.AppendLine($"    {SomeUtils.GetPrefix(key.GetType().Name)} {key.Name} = {key.Tag.GenericValue};");
                                    }
                                }
                            }
                        }
                        else
                        {
                            //outputBuilder.Append($"\nfix\nvoid {export.Value.Name}");
                        }
                    }
                }

                var funcMapOrder = blueprintGeneratedClass.FuncMap.Keys.Select(fname => fname.ToString()).ToList();

                var functions = package.ExportsLazy
                    .Where(e => e.Value is UFunction)
                    .Select(e => (UFunction) e.Value)
                    .OrderBy(f =>
                    {
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
                            returnFunc = $"{(property.PropertyFlags.HasFlag(EPropertyFlags.ConstParm) ? "const " : string.Empty)}{SomeUtils.GetPrefix(property.GetType().Name)}{SomeUtils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || SomeUtils.GetPrefix(property.GetType().Name) == "U" ? "*" : string.Empty)}";
                        }
                        else if (!(property.Name.ToString().EndsWith("_ReturnValue") ||
                                  property.Name.ToString().StartsWith("CallFunc_") ||
                                  property.Name.ToString().StartsWith("K2Node_") ||
                                  property.Name.ToString().StartsWith("Temp_")) || // removes useless args
                                  property.PropertyFlags.HasFlag(EPropertyFlags.Edit))
                        {
                            argsList += $"{(property.PropertyFlags.HasFlag(EPropertyFlags.ConstParm) ? "const " : string.Empty)}{SomeUtils.GetPrefix(property.GetType().Name)}{SomeUtils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || SomeUtils.GetPrefix(property.GetType().Name) == "U" ? "*" : string.Empty)}{(property.PropertyFlags.HasFlag(EPropertyFlags.OutParm) ? "&" : string.Empty)} {property.Name}, ";
                        }
                    }
                    argsList = argsList.TrimEnd(',', ' ');

                    outputBuilder.AppendLine($"\n\t{returnFunc} {function.Name.Replace(" ", "")}({argsList})\n\t{{");
                    if (function?.ScriptBytecode != null)
                    {
                        foreach (KismetExpression property in function.ScriptBytecode)
                        {
                            ProcessExpression(property.Token, property, outputBuilder);
                        }
                    }
                    else
                    {
                        outputBuilder.Append("\n // This function does not have Bytecode \n\n");
                        outputBuilder.Append("\t}\n");
                    }
                }

                outputBuilder.Append("\n\n}\n");
            }
            else if (verseTest)
            {
                var VerseClass = package?.ExportsLazy.Where(export => export.Value is UVerseClass).Select(export => (UVerseClass) export.Value).FirstOrDefault();

                if (VerseClass != null)
                {
                    mainClass = VerseClass.Name;
                    outputBuilder.AppendLine($"class {SomeUtils.GetPrefix(VerseClass.GetType().Name)}{VerseClass.Name} : public {SomeUtils.GetPrefix(VerseClass.GetType().Name)}{VerseClass.SuperStruct.Name}\n{{\npublic:");

                    foreach (FProperty property in VerseClass.ChildProperties)
                    {
                        outputBuilder.AppendLine($"    {SomeUtils.GetPrefix(property.GetType().Name)}{SomeUtils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || property.PropertyFlags.HasFlag(EPropertyFlags.ReferenceParm) || SomeUtils.GetPropertyProperty(property) ? "*" : string.Empty)} {property.Name} = {property.Name}placenolder;");
                    }

                    foreach (var export in package.ExportsLazy)
                    {
                        if (export.Value is not UBlueprintGeneratedClass)
                        {
                            if (export.Value.Name.StartsWith("Default__") && export.Value.Name.EndsWith(mainClass))
                            {
                                var exportObject = (UObject) export.Value;

                                foreach (var key in exportObject.Properties)
                                {
                                    if (outputBuilder.ToString().Contains($"{key.Name}placenolder"))
                                    {
                                        if (key.Tag.GetType().Name == "ObjectProperty" || key.PropertyType == "ObjectProperty" || key.PropertyType == "StrProperty" || key.PropertyType == "NameProperty" || key.PropertyType == "ClassProperty")
                                        {
                                            outputBuilder.Replace($"{key.Name}placenolder", $"\"{key.Tag.GenericValue.ToString()}\"");
                                        }
                                        else
                                        if (key.Tag.GetType().Name == "BoolProperty")
                                        {
                                            outputBuilder.Replace($"{key.Name}placenolder", $"{key.Tag.GenericValue.ToString().ToLower()}");
                                        }
                                        else
                                        {
                                            //Console.WriteLine(key.Name);
                                            //Console.WriteLine(key.Tag.GetType().Name);
                                            outputBuilder.Replace($"{key.Name}placenolder", key.Tag.GenericValue.ToString());
                                        }

                                    }
                                    else
                                    { // findout how to setup types for propertytag and this is a mess
                                        if (key.Tag.GetType().Name == "ObjectProperty" || key.PropertyType == "StructProperty" || key.PropertyType == "StrProperty" || key.PropertyType == "NameProperty" || key.PropertyType == "ClassProperty")
                                        {
                                            outputBuilder.AppendLine($"    {SomeUtils.GetPrefix(key.GetType().Name)} {key.Name} = \"{key.Tag.GenericValue}\";");
                                        }
                                        else
                                            if (key.Tag.GetType().Name == "BoolProperty")
                                        {
                                            outputBuilder.Replace($"{key.Name}placenolder", $"{key.Tag.GenericValue.ToString().ToLower()}");
                                        }
                                        else
                                        {
                                            outputBuilder.AppendLine($"    {SomeUtils.GetPrefix(key.GetType().Name)} {key.Name} = {key.Tag.GenericValue};");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                //outputBuilder.Append($"\nfix\nvoid {export.Value.Name}");
                            }
                        }
                    }

                    var funcMapOrder = VerseClass.FuncMap.Keys.Select(fname => fname.ToString()).ToList();

                    var functions = package.ExportsLazy
                        .Where(e => e.Value is UFunction)
                        .Select(e => (UFunction) e.Value)
                        .OrderBy(f =>
                        {
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
                        if (function?.ChildProperties != null)
                        {
                            foreach (FProperty property in function.ChildProperties)
                            {
                                if (property.Name.PlainText == "ReturnValue")
                                {
                                    returnFunc = $"{(property.PropertyFlags.HasFlag(EPropertyFlags.ConstParm) ? "const " : string.Empty)}{SomeUtils.GetPrefix(property.GetType().Name)}{SomeUtils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || SomeUtils.GetPrefix(property.GetType().Name) == "U" ? "*" : string.Empty)}";
                                }
                                else if (!(property.Name.ToString().EndsWith("_ReturnValue") ||
                                          property.Name.ToString().StartsWith("CallFunc_") ||
                                          property.Name.ToString().StartsWith("K2Node_") ||
                                          property.Name.ToString().StartsWith("Temp_")) || // removes useless args
                                          property.PropertyFlags.HasFlag(EPropertyFlags.Edit))
                                {
                                    argsList += $"{(property.PropertyFlags.HasFlag(EPropertyFlags.ConstParm) ? "const " : string.Empty)}{SomeUtils.GetPrefix(property.GetType().Name)}{SomeUtils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || SomeUtils.GetPrefix(property.GetType().Name) == "U" ? "*" : string.Empty)}{(property.PropertyFlags.HasFlag(EPropertyFlags.OutParm) ? "&" : string.Empty)} {property.Name}, ";
                                }
                            }
                        }
                        argsList = argsList.TrimEnd(',', ' ');

                        outputBuilder.AppendLine($"\n\t{returnFunc} {function.Name.Replace(" ", "")}({argsList})\n\t{{");
                        if (function?.ScriptBytecode != null)
                        {
                            foreach (KismetExpression property in function.ScriptBytecode)
                            {
                                ProcessExpression(property.Token, property, outputBuilder);
                            }
                        }
                        else
                        {
                            outputBuilder.Append("\n // This function does not have Bytecode \n\n");
                            outputBuilder.Append("\t}\n");
                        }
                    }
                    // loop through DisplayNameToUENameFunctionMap and apply fixes
                    outputBuilder.Append("\n\n}");
                }
            }
            else
            {
                Console.WriteLine("No Blueprint Found nor Verse set");
                return;
            }
            int targetIndex = outputBuilder.ToString().IndexOf("placenolder");
            string pattern = $@"\w+placenolder";
            string updatedOutput = Regex.Replace(outputBuilder.ToString(), pattern, "null");
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string outputFilePath = Path.Combine(exeDirectory, "Output.cpp");
            File.WriteAllText(outputFilePath, updatedOutput);

            Console.WriteLine($"Output written to: {outputFilePath}");

            await AppSettings.Save().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void ProcessExpression(EExprToken token, KismetExpression expression, StringBuilder outputBuilder, bool isParameter = false)
    {
        switch (token)
        {
            case EExprToken.EX_LetValueOnPersistentFrame:
                {
                    EX_LetValueOnPersistentFrame op = (EX_LetValueOnPersistentFrame) expression;
                    EX_VariableBase opp = (EX_VariableBase) op.AssignmentExpression;
                    var nerd = string.Join('.', op.DestinationProperty.New.Path.Select(n => n.Text));
                    var nerdd = string.Join('.', opp.Variable.New.Path.Select(n => n.Text));

                    if (!isParameter)
                    {
                        outputBuilder.Append($"\t\t{(nerd.Contains("K2Node_") ? $"UberGraphFrame->{nerd}" : nerd)} = {nerdd};\n\n"); // hardcoded
                    }
                    else
                    {
                        outputBuilder.Append($"\t\t{(nerd.Contains("K2Node_") ? $"UberGraphFrame->{nerd}" : nerd)} = {nerdd}");
                    }
                    break;
                }
            case EExprToken.EX_LocalFinalFunction:
            case EExprToken.EX_FinalFunction:
                {
                    EX_FinalFunction op = (EX_FinalFunction) expression;
                    KismetExpression[] opp = (KismetExpression[]) op.Parameters;
                    if (isParameter)
                    {
                        outputBuilder.Append($"{op.StackNode.Name.Replace(" ", "")}(");
                    }
                    else if (opp.Length < 1)
                    {
                        outputBuilder.Append($"\t\t{op?.StackNode?.Name.Replace(" ", "")}(");
                    }
                    else
                    {
                        outputBuilder.Append($"\t\t{SomeUtils.GetPrefix(op?.StackNode?.ResolvedObject?.Outer?.GetType()?.Name)}{op?.StackNode?.Name.Replace(" ", "")}(");
                    }

                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 4) outputBuilder.Append("\n    ");
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
                        outputBuilder.Append($");\n");
                    }
                    break;
                }
            case EExprToken.EX_CallMath:
                {
                    EX_FinalFunction op = (EX_FinalFunction) expression;
                    KismetExpression[] opp = (KismetExpression[]) op.Parameters;
                    outputBuilder.Append($"{SomeUtils.GetPrefix(op.StackNode.ResolvedObject.Outer.GetType().Name)}{op.StackNode.ResolvedObject.Outer.Name.ToString().Replace(" ", "")}::{op.StackNode.Name}(");
                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 4) outputBuilder.Append("\n    ");
                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, true); // if context it fails and does ;\n\n
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
                    EX_VirtualFunction op = (EX_VirtualFunction) expression;
                    KismetExpression[] opp = (KismetExpression[]) op.Parameters;

                    if (isParameter)
                    {
                        outputBuilder.Append($"{op.VirtualFunctionName.PlainText.Replace(" ", "")}(");
                    }
                    else
                    {
                        outputBuilder.Append($"\t\t{op.VirtualFunctionName.PlainText.Replace(" ", "")}(");
                    }
                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 4) outputBuilder.Append("\n    ");

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
                    EX_ComputedJump op = (EX_ComputedJump) expression;
                    if (op.CodeOffsetExpression is EX_VariableBase)
                    {
                        EX_VariableBase opp = (EX_VariableBase) op.CodeOffsetExpression;
                        outputBuilder.AppendLine($"\t\tgoto {string.Join('.', ((EX_VariableBase) op.CodeOffsetExpression).Variable.New.Path.Select(n => n.Text))};\n");
                    }
                    else
                    {
                        EX_CallMath opp = (EX_CallMath) op.CodeOffsetExpression;
                        ProcessExpression(opp.Token, opp, outputBuilder, true);
                        //outputBuilder.AppendLine($"\t\tgoto {string.Join('.', ((EX_VariableBase) op.CodeOffsetExpression).Variable.New.Path.Select(n => n.Text))};\n");
                    }
                    break;
                }
            case EExprToken.EX_PopExecutionFlowIfNot:
                {
                    EX_PopExecutionFlowIfNot op = (EX_PopExecutionFlowIfNot) expression;
                    outputBuilder.Append("\t\tif (!");
                    ProcessExpression(op.BooleanExpression.Token, op.BooleanExpression, outputBuilder);
                    outputBuilder.Append(") \r\n");
                    outputBuilder.Append($"\t\t    FlowStack.Pop();\n\n");
                    break;
                }
            case EExprToken.EX_Cast:
                {
                    EX_Cast op = (EX_Cast) expression;// support CST_ObjectToInterface when i have a example of how it works

                    if (ECastToken.CST_ObjectToBool == op.ConversionType || ECastToken.CST_InterfaceToBool == op.ConversionType)
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
                    EX_InterfaceContext op = (EX_InterfaceContext) expression;
                    ProcessExpression(op.InterfaceValue.Token, op.InterfaceValue, outputBuilder);
                    break;
                }
            case EExprToken.EX_ArrayConst:
                {
                    EX_ArrayConst op = (EX_ArrayConst) expression;
                    outputBuilder.Append("TArray {");
                    foreach (KismetExpression element in op.Elements)
                    {
                        outputBuilder.Append(" ");
                        ProcessExpression(element.Token, element, outputBuilder);
                    }
                    if (op.Elements.Length < 1)
                        outputBuilder.Append("  ");
                    else
                        outputBuilder.Append(" ");

                    outputBuilder.Append("}");
                    break;
                }
            case EExprToken.EX_SetArray:
                {
                    EX_SetArray op = (EX_SetArray) expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.AssigningProperty.Token, op.AssigningProperty, outputBuilder); // if TArray is a var it fails
                    outputBuilder.Append(" = ");
                    outputBuilder.Append("TArray {");
                    for (int i = 0; i < op.Elements.Length; i++)
                    {
                        KismetExpression element = op.Elements[i];
                        outputBuilder.Append(" ");
                        ProcessExpression(element.Token, element, outputBuilder);

                        if (i < op.Elements.Length - 1)
                        {
                            outputBuilder.Append(",");
                        } else
                        {
                            outputBuilder.Append(" ");
                        }
                    }

                    if (op.Elements.Length < 1)
                        outputBuilder.Append("  ");
                    else
                        outputBuilder.Append(" ");

                    outputBuilder.Append("};\n\n");
                    break;
                }
            case EExprToken.EX_SetMap:
                {
                    EX_SetMap op = (EX_SetMap) expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.MapProperty.Token, op.MapProperty, outputBuilder);
                    outputBuilder.Append(" = ");
                    outputBuilder.Append("TMap {");
                    for (int i = 0; i < op.Elements.Length; i++)
                    {
                        var element = op.Elements[i];
                        outputBuilder.Append(" ");
                        ProcessExpression(element.Token, element, outputBuilder);

                        if (i < op.Elements.Length - 1)
                        {
                            if (element.Token == EExprToken.EX_InstanceVariable)
                            {
                                outputBuilder.Append(": ");
                            }
                            else
                            {
                                outputBuilder.Append(", ");
                            }
                        } else
                        {
                            outputBuilder.Append(" ");
                        }
                    }

                    if (op.Elements.Length < 1)
                        outputBuilder.Append("  ");
                    outputBuilder.Append("}\n");
                    break;
                }
            case EExprToken.EX_SwitchValue:
                {
                    EX_SwitchValue op = (EX_SwitchValue) expression;

                    bool useTernary = op.Cases.Length <= 2
                        && op.Cases.All(c => c.CaseIndexValueTerm.Token == EExprToken.EX_True || c.CaseIndexValueTerm.Token == EExprToken.EX_False);

                    if (useTernary)
                    {
                        ProcessExpression(op.IndexTerm.Token, op.IndexTerm, outputBuilder);
                        outputBuilder.Append(" ? ");

                        bool isFirst = true;
                        foreach (var caseItem in op.Cases.Where(c => c.CaseIndexValueTerm.Token == EExprToken.EX_True))
                        {
                            if (!isFirst)
                                outputBuilder.Append(" : ");

                            ProcessExpression(caseItem.CaseTerm.Token, caseItem.CaseTerm, outputBuilder, true);
                            isFirst = false;
                        }

                        foreach (var caseItem in op.Cases.Where(c => c.CaseIndexValueTerm.Token == EExprToken.EX_False))
                        {
                            if (!isFirst)
                                outputBuilder.Append(" : ");

                            ProcessExpression(caseItem.CaseTerm.Token, caseItem.CaseTerm, outputBuilder, true);
                        }
                    }
                    else
                    {
                        outputBuilder.Append("switch (");
                        ProcessExpression(op.IndexTerm.Token, op.IndexTerm, outputBuilder);
                        outputBuilder.Append(")\n");
                        outputBuilder.Append("{\n");

                        foreach (var caseItem in op.Cases)
                        {
                            if (caseItem.CaseIndexValueTerm.Token == EExprToken.EX_IntConst)
                            {
                                int caseValue = ((EX_IntConst) caseItem.CaseIndexValueTerm).Value;
                                outputBuilder.Append($"\t\tcase {caseValue}:\n");
                            }
                            else
                            {
                                outputBuilder.Append("\t\tcase ");
                                ProcessExpression(caseItem.CaseIndexValueTerm.Token, caseItem.CaseIndexValueTerm, outputBuilder);
                                outputBuilder.Append(":\n");
                            }

                            outputBuilder.Append("\t\t{\n");
                            outputBuilder.Append("\t\t    ");
                            ProcessExpression(caseItem.CaseTerm.Token, caseItem.CaseTerm, outputBuilder);
                            outputBuilder.Append(";\n");
                            outputBuilder.Append("\t\t    break;\n");
                            outputBuilder.Append("\t\t}\n");
                        }

                        if (op.DefaultTerm != null)
                        {
                            outputBuilder.Append("\t\tdefault:\n");
                            outputBuilder.Append("\t\t{\n");
                            outputBuilder.Append("\t\t    ");
                            ProcessExpression(op.DefaultTerm.Token, op.DefaultTerm, outputBuilder);
                            outputBuilder.Append("\n    }\n\n");
                        }

                        outputBuilder.Append("}\n");
                    }
                    break;
                }
            case EExprToken.EX_ArrayGetByRef: // i assume get array with index
                {
                    EX_ArrayGetByRef op = (EX_ArrayGetByRef) expression; // FortniteGame/Plugins/GameFeatures/FM/PilgrimCore/Content/Player/Components/BP_PilgrimPlayerControllerComponent.uasset
                    ProcessExpression(op.ArrayVariable.Token, op.ArrayVariable, outputBuilder, true);
                    outputBuilder.Append("[");
                    ProcessExpression(op.ArrayIndex.Token, op.ArrayIndex, outputBuilder);
                    outputBuilder.Append("]");
                    break;
                }
            case EExprToken.EX_BitFieldConst:
                {
                    EX_BitFieldConst op = (EX_BitFieldConst)expression;
                    outputBuilder.Append(op.ConstValue);
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
                    ProcessExpression(op.Target.Token, op.Target, outputBuilder, true);
                    outputBuilder.Append(")");
                    break;
                }
            case EExprToken.EX_StructConst:
                {
                    EX_StructConst op = (EX_StructConst)expression;
                    outputBuilder.Append($"{SomeUtils.GetPrefix(op.Struct.GetType().Name)}{op.Struct.Name}");
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
            case EExprToken.EX_ObjectConst:
                {
                    EX_ObjectConst op = (EX_ObjectConst)expression;
                    outputBuilder.Append("FindObject<");
                    string classString = op?.Value?.ResolvedObject?.Class?.ToString()?.Replace("'", "");

                    if (classString?.Contains(".") == true)
                    {
                        outputBuilder.Append("U" + classString.Split(".")[1]);
                    }
                    else
                    {
                        outputBuilder.Append("U" + classString); // renove hardcoded and sometimes incorrect
                    }
                    outputBuilder.Append(">(\"");
                    //Console.WriteLine(classString);
                    //Console.WriteLine(op?.Value?.ResolvedObject?.Outer.ToString());
                    outputBuilder.Append(
                        op?.Value?.ResolvedObject?.Outer // sometimes incorrect
                            .ToString()
                            .Replace("'", "")
                            .Replace(op?.Value?.ResolvedObject?.Class.ToString().Replace("'", ""), "") +
                        "." +
                        op.Value.Name
                    );
                    if (isParameter)
                    {
                        outputBuilder.Append("\")");
                    }
                    else
                    {
                        outputBuilder.Append("\")");
                    }
                    break;
                }
            case EExprToken.EX_BindDelegate:
                {
                    EX_BindDelegate op = (EX_BindDelegate)expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder);
                    outputBuilder.Append($".BindUFunction(");
                    ProcessExpression(op.ObjectTerm.Token, op.ObjectTerm, outputBuilder);
                    outputBuilder.Append($", \"{op.FunctionName}\"");
                    outputBuilder.Append($");\n\n");
                    break;
                }
                // all the delegate functions suck
            case EExprToken.EX_AddMulticastDelegate:
                {
                    EX_AddMulticastDelegate op = (EX_AddMulticastDelegate) expression;
                    if (op.Delegate.Token == EExprToken.EX_LocalVariable || op.Delegate.Token == EExprToken.EX_InstanceVariable)
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append(".AddDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder);
                        outputBuilder.Append($");\n\n");
                    } else if (op.Delegate.Token != EExprToken.EX_Context)
                    {
                        Console.WriteLine($"Issue: EX_AddMulticastDelegate missing info: {op.StatementIndex}, {op.Delegate.Token}");
                    } else
                    {
                        EX_Context opp = (EX_Context) op.Delegate;
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append("->");
                        ProcessExpression(opp.ContextExpression.Token, opp.ContextExpression, outputBuilder);
                        outputBuilder.Append(".AddDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder);
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_RemoveMulticastDelegate: // everything here has been guessed not compared to actual UE but does work fine and displays all infomation
                {
                    EX_RemoveMulticastDelegate op = (EX_RemoveMulticastDelegate) expression;
                    if (op.Delegate.Token == EExprToken.EX_LocalVariable || op.Delegate.Token == EExprToken.EX_InstanceVariable)
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append(".RemoveDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder);
                        outputBuilder.Append($");\n\n");
                    } else if (op.Delegate.Token != EExprToken.EX_Context)
                    {
                        Console.WriteLine("Issue: EX_RemoveMulticastDelegate missing info: ", op.StatementIndex);
                    } else
                    {
                        EX_Context opp = (EX_Context) op.Delegate;
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append("->");
                        ProcessExpression(opp.ContextExpression.Token, opp.ContextExpression, outputBuilder);
                        outputBuilder.Append(".RemoveDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder);
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_CallMulticastDelegate: // everything here has been guessed not compared to actual UE but does work fine and displays all infomation
                {
                    EX_CallMulticastDelegate op = (EX_CallMulticastDelegate) expression;
                    KismetExpression[] opp = (KismetExpression[]) op.Parameters;
                    if (op.Delegate.Token == EExprToken.EX_LocalVariable || op.Delegate.Token == EExprToken.EX_InstanceVariable)
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append(".Call(");
                        for (int i = 0; i < opp.Length; i++)
                        {
                            if (opp.Length > 4)
                                outputBuilder.Append("\n    ");
                            ProcessExpression(opp[i].Token, opp[i], outputBuilder, true);
                            if (i < opp.Length - 1)
                            {
                                outputBuilder.Append(", ");
                            }
                        }
                        outputBuilder.Append($");\n\n");
                    } else if (op.Delegate.Token != EExprToken.EX_Context)
                    {
                        Console.WriteLine("Issue: EX_CallMulticastDelegate missing info: ", op.StatementIndex);
                    }
                    else
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append(".Call(");
                        for (int i = 0; i < opp.Length; i++)
                        {
                            if (opp.Length > 4)
                                outputBuilder.Append("\n    ");
                            ProcessExpression(opp[i].Token, opp[i], outputBuilder, true);
                            if (i < opp.Length - 1)
                            {
                                outputBuilder.Append(", ");
                            }
                        }
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_ClassContext:
            case EExprToken.EX_Context:
                {
                    EX_Context op = (EX_Context)expression;
                    ProcessExpression(op.ObjectExpression.Token, op.ObjectExpression, outputBuilder, true);

                    outputBuilder.Append("->");
                    ProcessExpression(op.ContextExpression.Token, op.ContextExpression, outputBuilder, true);
                    if (!isParameter)
                    {
                        outputBuilder.Append(";\n\n");
                    }
                    break;
                }
            case EExprToken.EX_Context_FailSilent:
                {
                    EX_Context op = (EX_Context)expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.ObjectExpression.Token, op.ObjectExpression, outputBuilder, true);
                    if (!isParameter)
                    {
                        outputBuilder.Append("->");
                        ProcessExpression(op.ContextExpression.Token, op.ContextExpression, outputBuilder, true);
                        outputBuilder.Append($";\n\n");
                    }
                    break;
                }
            case EExprToken.EX_Let:
                {
                    EX_Let op = (EX_Let)expression;
                    if (!isParameter)
                    {
                        outputBuilder.Append("\t\t");
                    }
                    ProcessExpression(op.Variable.Token, op.Variable, outputBuilder, true);
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
                        outputBuilder.Append("\t\t");
                    }
                    ProcessExpression(op.Variable.Token, op.Variable, outputBuilder, true);
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
            case EExprToken.EX_JumpIfNot:
                {
                    EX_JumpIfNot op = (EX_JumpIfNot)expression;
                    outputBuilder.Append("\t\tif (!");
                    ProcessExpression(op.BooleanExpression.Token, op.BooleanExpression, outputBuilder);
                    outputBuilder.Append(") \r\n");
                    outputBuilder.Append("\t\t    goto Label_");
                    outputBuilder.Append(op.CodeOffset);
                    outputBuilder.Append(";\n\n");
                    break;
                }
            case EExprToken.EX_Jump:
                {
                    EX_Jump op = (EX_Jump)expression;
                    outputBuilder.Append($"\t\tgoto Label_{op.CodeOffset};\n\n");
                    break;
                }
            // Static expressions
            case EExprToken.EX_StructMemberContext:
                {
                    EX_StructMemberContext op = (EX_StructMemberContext)expression;
                    ProcessExpression(op.StructExpression.Token, op.StructExpression, outputBuilder);
                    outputBuilder.Append(".");
                    outputBuilder.Append(string.Join('.', op.Property.New.Path.Select(n => n.Text)));
                    break;
                }
            case EExprToken.EX_LocalVariable:
            case EExprToken.EX_DefaultVariable:
            case EExprToken.EX_InstanceVariable:
            case EExprToken.EX_LocalOutVariable:
            case EExprToken.EX_ClassSparseDataVariable:
                {
                    EX_VariableBase op = (EX_VariableBase)expression;
                    outputBuilder.Append(string.Join('.', op.Variable.New.Path.Select(n => n.Text)).Replace(" ", ""));
                    break;
                }
            case EExprToken.EX_SoftObjectConst:
                {
                    EX_SoftObjectConst op = (EX_SoftObjectConst)expression;
                    ProcessExpression(op.Value.Token, op.Value, outputBuilder);
                    break;
                }
            case EExprToken.EX_ByteConst:
            case EExprToken.EX_IntConstByte:
                {
                    KismetExpression<byte> op = (KismetExpression<byte>)expression;
                    outputBuilder.Append($"0x{op.Value.ToString("X")}");
                    break;
                }
            case EExprToken.EX_Return:
                {
                    EX_Return op = (EX_Return)expression;
                    bool tocheck = op.ReturnExpression.Token == EExprToken.EX_Nothing;
                    outputBuilder.Append($"\n\t\treturn");
                    if (!tocheck) outputBuilder.Append(" ");
                    ProcessExpression(op.ReturnExpression.Token, op.ReturnExpression, outputBuilder, true);
                    outputBuilder.AppendLine(";\n\n");
                    break;
                }
            case EExprToken.EX_DoubleConst:
                {
                    var value = ((EX_DoubleConst)expression).Value;
                    outputBuilder.Append(value == (int)value ? (int)value : value.ToString("R"));
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
            case EExprToken.EX_IntConst:
                {
                    EX_IntConst op = (EX_IntConst) expression;
                    outputBuilder.Append(op.Value.ToString());
                    break;
                }
            case EExprToken.EX_PropertyConst:
                {
                    EX_PropertyConst op = (EX_PropertyConst) expression;
                    outputBuilder.Append(string.Join('.', op.Property.New.Path.Select(n => n.Text)).Replace(" ", ""));
                    break;
                }
            case EExprToken.EX_StringConst:
                outputBuilder.Append($"\"{((EX_StringConst)expression).Value}\"");
                break;
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
                if (expression is EX_TextConst textConst)
                {
                    if (textConst.Value is FScriptText scriptText)
                    {
                        ProcessExpression(scriptText.SourceString.Token, scriptText.SourceString, outputBuilder, true); // cursed sometimes so i'll add a name length just for now
                    }
                    else
                    {
                        outputBuilder.Append(textConst.Value.ToString());
                    }
                }
                break;
            case EExprToken.EX_UnicodeStringConst:
                outputBuilder.Append(((EX_UnicodeStringConst)expression).Value);
                break;
            case EExprToken.EX_EndOfScript:
            case EExprToken.EX_EndParmValue:
                outputBuilder.AppendLine("\t}");
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
            case EExprToken.EX_Tracepoint:
            case EExprToken.EX_PopExecutionFlow:
            case EExprToken.EX_PushExecutionFlow:
                //case EExprToken.EX_RemoveMulticastDelegate:// some here are unsupported
                //case EExprToken.EX_ClearMulticastDelegate:
                break;
            default:
                Console.WriteLine($"Unsupported token: {token}");
                outputBuilder.Append($"{token}");
                break;
        }
    }
}
