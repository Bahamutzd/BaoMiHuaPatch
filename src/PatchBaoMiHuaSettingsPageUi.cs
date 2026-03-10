using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PatchBaoMiHuaSettingsPageUi
{
    private const string SettingsPageTypeName = "Filmly.Views.SettingsPage";
    private const string HelperTypeName = "BaoMiHuaPatch.SettingsPageUiPatch";

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: PatchBaoMiHuaSettingsPageUi <BaoMiHua.dll> <SettingsPageUiPatchHelper.dll> [backup-path]");
            return 1;
        }

        string targetPath = Path.GetFullPath(args[0]);
        string helperPath = Path.GetFullPath(args[1]);
        string backupPath = args.Length >= 3
            ? Path.GetFullPath(args[2])
            : targetPath + ".settings-ui.bak";
        string tempOutputPath = targetPath + ".settings-ui.patched";

        if (!File.Exists(targetPath))
        {
            Console.Error.WriteLine("Target assembly not found: " + targetPath);
            return 2;
        }

        if (!File.Exists(helperPath))
        {
            Console.Error.WriteLine("Helper assembly not found: " + helperPath);
            return 3;
        }

        try
        {
            BackupTarget(targetPath, backupPath);
            PatchAssembly(targetPath, helperPath, tempOutputPath);
            File.Copy(tempOutputPath, targetPath, true);
            VerifyAssembly(targetPath);
            Console.WriteLine("Settings page UI patch completed successfully.");
            Console.WriteLine("Backup: " + backupPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Patch failed: " + ex);
            return 10;
        }
        finally
        {
            if (File.Exists(tempOutputPath))
            {
                File.Delete(tempOutputPath);
            }
        }
    }

    private static void BackupTarget(string targetPath, string backupPath)
    {
        string backupDirectory = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrEmpty(backupDirectory))
        {
            Directory.CreateDirectory(backupDirectory);
        }

        if (!File.Exists(backupPath))
        {
            File.Copy(targetPath, backupPath, false);
        }
    }

    private static void PatchAssembly(string targetPath, string helperPath, string outputPath)
    {
        ReaderParameters readerParameters = CreateReaderParameters(targetPath, helperPath);

        using (AssemblyDefinition helperAssembly = AssemblyDefinition.ReadAssembly(helperPath, readerParameters))
        using (AssemblyDefinition targetAssembly = AssemblyDefinition.ReadAssembly(targetPath, readerParameters))
        {
            TypeDefinition helperSourceType = helperAssembly.MainModule.GetType(HelperTypeName);
            if (helperSourceType == null)
            {
                throw new InvalidOperationException("Helper type not found: " + HelperTypeName);
            }

            ModuleDefinition module = targetAssembly.MainModule;
            if (module.GetType(HelperTypeName) != null)
            {
                throw new InvalidOperationException("Target assembly already contains settings UI helper type.");
            }

            TypeDefinition settingsPageType = module.GetType(SettingsPageTypeName);
            if (settingsPageType == null)
            {
                throw new InvalidOperationException("Settings page type not found: " + SettingsPageTypeName);
            }

            MethodDefinition constructor = FindMethod(settingsPageType, ".ctor", 0);
            if (constructor == null || !constructor.HasBody)
            {
                throw new InvalidOperationException("Settings page constructor not found.");
            }

            TypeDefinition helperTargetType = CloneHelperType(helperSourceType, module);
            module.Types.Add(helperTargetType);

            MethodDefinition ensureSectionMethod = FindMethod(helperTargetType, "EnsureExternalPlayerSection", 1);
            if (ensureSectionMethod == null)
            {
                throw new InvalidOperationException("EnsureExternalPlayerSection method not found.");
            }

            InjectConstructorCall(constructor, module.ImportReference(ensureSectionMethod));
            targetAssembly.Write(outputPath);
        }
    }

    private static void InjectConstructorCall(MethodDefinition constructor, MethodReference ensureSectionMethod)
    {
        ILProcessor il = constructor.Body.GetILProcessor();
        List<Instruction> retInstructions = new List<Instruction>();
        foreach (Instruction instruction in constructor.Body.Instructions)
        {
            if (instruction.OpCode == OpCodes.Ret)
            {
                retInstructions.Add(instruction);
            }
        }

        if (retInstructions.Count == 0)
        {
            throw new InvalidOperationException("Constructor has no return instruction.");
        }

        foreach (Instruction retInstruction in retInstructions)
        {
            il.InsertBefore(retInstruction, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(retInstruction, il.Create(OpCodes.Call, ensureSectionMethod));
        }
    }

    private static TypeDefinition CloneHelperType(TypeDefinition sourceType, ModuleDefinition targetModule)
    {
        TypeDefinition targetType = new TypeDefinition(
            sourceType.Namespace,
            sourceType.Name,
            sourceType.Attributes,
            ImportTypeReference(sourceType.BaseType, targetModule, sourceType, null));

        foreach (MethodDefinition sourceMethod in sourceType.Methods)
        {
            MethodDefinition targetMethod = new MethodDefinition(
                sourceMethod.Name,
                sourceMethod.Attributes,
                ImportTypeReference(sourceMethod.ReturnType, targetModule, sourceType, targetType));
            targetMethod.ImplAttributes = sourceMethod.ImplAttributes;
            targetType.Methods.Add(targetMethod);
        }

        Dictionary<MethodDefinition, MethodDefinition> methodMap = new Dictionary<MethodDefinition, MethodDefinition>();
        for (int index = 0; index < sourceType.Methods.Count; index++)
        {
            methodMap[sourceType.Methods[index]] = targetType.Methods[index];
        }

        foreach (MethodDefinition sourceMethod in sourceType.Methods)
        {
            MethodDefinition targetMethod = methodMap[sourceMethod];
            CloneMethodSignature(sourceMethod, targetMethod, targetModule, sourceType, targetType);
            CloneMethodBody(sourceMethod, targetMethod, targetModule, sourceType, targetType, methodMap);
        }

        return targetType;
    }

    private static void CloneMethodSignature(
        MethodDefinition sourceMethod,
        MethodDefinition targetMethod,
        ModuleDefinition targetModule,
        TypeDefinition sourceHelperType,
        TypeDefinition targetHelperType)
    {
        foreach (ParameterDefinition parameter in sourceMethod.Parameters)
        {
            targetMethod.Parameters.Add(new ParameterDefinition(
                parameter.Name,
                parameter.Attributes,
                ImportTypeReference(parameter.ParameterType, targetModule, sourceHelperType, targetHelperType)));
        }
    }

    private static void CloneMethodBody(
        MethodDefinition sourceMethod,
        MethodDefinition targetMethod,
        ModuleDefinition targetModule,
        TypeDefinition sourceHelperType,
        TypeDefinition targetHelperType,
        Dictionary<MethodDefinition, MethodDefinition> methodMap)
    {
        if (!sourceMethod.HasBody)
        {
            return;
        }

        MethodBody sourceBody = sourceMethod.Body;
        MethodBody targetBody = targetMethod.Body;
        targetBody.InitLocals = sourceBody.InitLocals;
        targetBody.MaxStackSize = sourceBody.MaxStackSize;

        Dictionary<VariableDefinition, VariableDefinition> variableMap = new Dictionary<VariableDefinition, VariableDefinition>();
        foreach (VariableDefinition variable in sourceBody.Variables)
        {
            VariableDefinition clonedVariable = new VariableDefinition(
                ImportTypeReference(variable.VariableType, targetModule, sourceHelperType, targetHelperType));
            targetBody.Variables.Add(clonedVariable);
            variableMap[variable] = clonedVariable;
        }

        Dictionary<Instruction, Instruction> instructionMap = new Dictionary<Instruction, Instruction>();
        foreach (Instruction sourceInstruction in sourceBody.Instructions)
        {
            Instruction clonedInstruction = CloneInstruction(
                sourceInstruction,
                targetModule,
                sourceMethod,
                targetMethod,
                sourceHelperType,
                targetHelperType,
                variableMap,
                methodMap);
            instructionMap[sourceInstruction] = clonedInstruction;
            targetBody.Instructions.Add(clonedInstruction);
        }

        foreach (Instruction sourceInstruction in sourceBody.Instructions)
        {
            Instruction targetInstruction = instructionMap[sourceInstruction];
            if (sourceInstruction.Operand is Instruction)
            {
                targetInstruction.Operand = instructionMap[(Instruction)sourceInstruction.Operand];
            }
            else if (sourceInstruction.Operand is Instruction[])
            {
                Instruction[] targets = (Instruction[])sourceInstruction.Operand;
                Instruction[] clonedTargets = new Instruction[targets.Length];
                for (int index = 0; index < targets.Length; index++)
                {
                    clonedTargets[index] = instructionMap[targets[index]];
                }

                targetInstruction.Operand = clonedTargets;
            }
        }

        foreach (ExceptionHandler handler in sourceBody.ExceptionHandlers)
        {
            targetBody.ExceptionHandlers.Add(new ExceptionHandler(handler.HandlerType)
            {
                CatchType = handler.CatchType != null
                    ? ImportTypeReference(handler.CatchType, targetModule, sourceHelperType, targetHelperType)
                    : null,
                TryStart = handler.TryStart != null ? instructionMap[handler.TryStart] : null,
                TryEnd = handler.TryEnd != null ? instructionMap[handler.TryEnd] : null,
                HandlerStart = handler.HandlerStart != null ? instructionMap[handler.HandlerStart] : null,
                HandlerEnd = handler.HandlerEnd != null ? instructionMap[handler.HandlerEnd] : null,
                FilterStart = handler.FilterStart != null ? instructionMap[handler.FilterStart] : null
            });
        }
    }

    private static Instruction CloneInstruction(
        Instruction sourceInstruction,
        ModuleDefinition targetModule,
        MethodDefinition sourceMethod,
        MethodDefinition targetMethod,
        TypeDefinition sourceHelperType,
        TypeDefinition targetHelperType,
        Dictionary<VariableDefinition, VariableDefinition> variableMap,
        Dictionary<MethodDefinition, MethodDefinition> methodMap)
    {
        object operand = sourceInstruction.Operand;
        OpCode opCode = sourceInstruction.OpCode;

        if (operand == null)
        {
            return Instruction.Create(opCode);
        }

        if (operand is string)
        {
            return Instruction.Create(opCode, (string)operand);
        }

        if (operand is sbyte)
        {
            return Instruction.Create(opCode, (sbyte)operand);
        }

        if (operand is byte)
        {
            return Instruction.Create(opCode, (byte)operand);
        }

        if (operand is int)
        {
            return Instruction.Create(opCode, (int)operand);
        }

        if (operand is long)
        {
            return Instruction.Create(opCode, (long)operand);
        }

        if (operand is float)
        {
            return Instruction.Create(opCode, (float)operand);
        }

        if (operand is double)
        {
            return Instruction.Create(opCode, (double)operand);
        }

        if (operand is ParameterDefinition)
        {
            ParameterDefinition parameter = (ParameterDefinition)operand;
            return Instruction.Create(opCode, targetMethod.Parameters[parameter.Index]);
        }

        if (operand is VariableDefinition)
        {
            return Instruction.Create(opCode, variableMap[(VariableDefinition)operand]);
        }

        if (operand is MethodReference)
        {
            MethodReference sourceMethodReference = (MethodReference)operand;
            return Instruction.Create(opCode, ImportMethodReference(
                sourceMethodReference,
                targetModule,
                sourceMethod,
                targetMethod,
                sourceHelperType,
                targetHelperType,
                methodMap));
        }

        if (operand is FieldReference)
        {
            return Instruction.Create(opCode, ImportFieldReference(
                (FieldReference)operand,
                targetModule,
                sourceHelperType,
                targetHelperType));
        }

        if (operand is TypeReference)
        {
            return Instruction.Create(opCode, ImportTypeReference(
                (TypeReference)operand,
                targetModule,
                sourceHelperType,
                targetHelperType));
        }

        if (operand is Instruction)
        {
            return Instruction.Create(opCode, Instruction.Create(OpCodes.Nop));
        }

        if (operand is Instruction[])
        {
            return Instruction.Create(opCode, new[] { Instruction.Create(OpCodes.Nop) });
        }

        throw new NotSupportedException("Unsupported IL operand: " + operand.GetType().FullName);
    }

    private static TypeReference ImportTypeReference(
        TypeReference sourceType,
        ModuleDefinition targetModule,
        TypeDefinition sourceHelperType,
        TypeDefinition targetHelperType)
    {
        if (sourceType == null)
        {
            return null;
        }

        if (sourceHelperType != null &&
            targetHelperType != null &&
            sourceType.FullName == sourceHelperType.FullName)
        {
            return targetHelperType;
        }

        return targetModule.ImportReference(sourceType);
    }

    private static FieldReference ImportFieldReference(
        FieldReference sourceField,
        ModuleDefinition targetModule,
        TypeDefinition sourceHelperType,
        TypeDefinition targetHelperType)
    {
        if (sourceField == null)
        {
            return null;
        }

        if (sourceHelperType != null &&
            targetHelperType != null &&
            sourceField.DeclaringType.FullName == sourceHelperType.FullName)
        {
            return new FieldReference(
                sourceField.Name,
                ImportTypeReference(sourceField.FieldType, targetModule, sourceHelperType, targetHelperType),
                targetHelperType);
        }

        return targetModule.ImportReference(sourceField);
    }

    private static MethodReference ImportMethodReference(
        MethodReference sourceMethodReference,
        ModuleDefinition targetModule,
        MethodDefinition sourceMethod,
        MethodDefinition targetMethod,
        TypeDefinition sourceHelperType,
        TypeDefinition targetHelperType,
        Dictionary<MethodDefinition, MethodDefinition> methodMap)
    {
        MethodDefinition resolvedMethod = sourceMethodReference.Resolve();
        if (resolvedMethod != null && methodMap.ContainsKey(resolvedMethod))
        {
            return methodMap[resolvedMethod];
        }

        if (resolvedMethod == sourceMethod)
        {
            return targetMethod;
        }

        if (sourceHelperType != null &&
            targetHelperType != null &&
            sourceMethodReference.DeclaringType.FullName == sourceHelperType.FullName)
        {
            MethodReference targetReference = new MethodReference(
                sourceMethodReference.Name,
                ImportTypeReference(sourceMethodReference.ReturnType, targetModule, sourceHelperType, targetHelperType),
                targetHelperType);
            targetReference.HasThis = sourceMethodReference.HasThis;
            targetReference.ExplicitThis = sourceMethodReference.ExplicitThis;
            targetReference.CallingConvention = sourceMethodReference.CallingConvention;
            foreach (ParameterDefinition parameter in sourceMethodReference.Parameters)
            {
                targetReference.Parameters.Add(new ParameterDefinition(
                    ImportTypeReference(parameter.ParameterType, targetModule, sourceHelperType, targetHelperType)));
            }

            return targetReference;
        }

        return targetModule.ImportReference(sourceMethodReference);
    }

    private static MethodDefinition FindMethod(TypeDefinition ownerType, string methodName, int parameterCount)
    {
        foreach (MethodDefinition method in ownerType.Methods)
        {
            if (method.Name == methodName && method.Parameters.Count == parameterCount)
            {
                return method;
            }
        }

        return null;
    }

    private static void VerifyAssembly(string targetPath)
    {
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(targetPath, CreateReaderParameters(targetPath, null)))
        {
            ModuleDefinition module = assembly.MainModule;
            TypeDefinition helperType = module.GetType(HelperTypeName);
            TypeDefinition settingsPageType = module.GetType(SettingsPageTypeName);
            if (helperType == null || settingsPageType == null)
            {
                throw new InvalidOperationException("Verification failed: helper or settings page type missing.");
            }

            MethodDefinition constructor = FindMethod(settingsPageType, ".ctor", 0);
            if (constructor == null)
            {
                throw new InvalidOperationException("Verification failed: constructor missing.");
            }

            bool callsHelper = false;
            foreach (Instruction instruction in constructor.Body.Instructions)
            {
                MethodReference methodReference = instruction.Operand as MethodReference;
                if (instruction.OpCode == OpCodes.Call &&
                    methodReference != null &&
                    methodReference.DeclaringType.FullName == HelperTypeName &&
                    methodReference.Name == "EnsureExternalPlayerSection")
                {
                    callsHelper = true;
                    break;
                }
            }

            if (!callsHelper)
            {
                throw new InvalidOperationException("Verification failed: constructor does not call EnsureExternalPlayerSection.");
            }

            Console.WriteLine("Verified helper type: " + HelperTypeName);
            Console.WriteLine("Verified settings page constructor injection: " + callsHelper);
        }
    }

    private static ReaderParameters CreateReaderParameters(string targetPath, string helperPath)
    {
        DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
        AddSearchDirectory(resolver, Path.GetDirectoryName(targetPath));
        AddSearchDirectory(resolver, Path.GetDirectoryName(helperPath));
        AddSearchDirectory(resolver, Environment.CurrentDirectory);

        return new ReaderParameters
        {
            AssemblyResolver = resolver,
            InMemory = true,
            ReadWrite = false
        };
    }

    private static void AddSearchDirectory(DefaultAssemblyResolver resolver, string directory)
    {
        if (resolver == null || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            resolver.AddSearchDirectory(directory);
        }
        catch
        {
        }
    }
}
