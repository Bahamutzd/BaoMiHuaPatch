using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PatchBaoMiHuaExternalPlayer
{
    private const string DispatcherTypeName = "a.ae";
    private const string PlayerWindowCoordinatorTypeName = "a.aF";
    private const string ReceiverTypeName = "Filmly.ViewModels.MediaPlayerListViewModel";
    private const string HelperTypeName = "BaoMiHuaPatch.ExternalPlayerPatch";
    private const string MediaViewModelTypeName = "Filmly.ViewModels.MediaViewModel";
    private const string PlayMessageTypeName = "Filmly.Messages.PlayVLCMediaMessage";
    private const string OpenMethodName = "C";
    private const string PlayMethodName = "A";
    private const string EnsurePlayerWindowMethodName = "C";
    private const string OpenFallbackMethodName = "C_BuiltinFallback";
    private const string PlayFallbackMethodName = "A_BuiltinFallback";
    private const string EnsurePlayerWindowFallbackMethodName = "C_PlayerWindowBuiltinFallback";
    private const string ReceiverFallbackMethodName = "Receive_PlayVLCMediaMessage_BuiltinFallback";
    private const string TraceMethodName = "Trace";
    private const string PlayMessageTypeHint = "PlayVLCMediaMessage";

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: PatchBaoMiHuaExternalPlayer <BaoMiHua.dll> <ExternalPlayerPatchHelper.dll> [backup-path]");
            return 1;
        }

        string targetPath = Path.GetFullPath(args[0]);
        string helperPath = Path.GetFullPath(args[1]);
        string backupPath = args.Length >= 3
            ? Path.GetFullPath(args[2])
            : targetPath + ".bak";
        string tempOutputPath = targetPath + ".patched";

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
            ReplaceTarget(tempOutputPath, targetPath);
            VerifyAssembly(targetPath);
            Console.WriteLine("Patch completed successfully.");
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
        {
            TypeDefinition sourceHelperType = helperAssembly.MainModule.GetType(HelperTypeName);
            if (sourceHelperType == null)
            {
                throw new InvalidOperationException("Helper type not found: " + HelperTypeName);
            }

            using (AssemblyDefinition targetAssembly = AssemblyDefinition.ReadAssembly(targetPath, readerParameters))
            {
                ModuleDefinition module = targetAssembly.MainModule;
                TypeDefinition targetHelperType = module.GetType(HelperTypeName);
                if (targetHelperType == null)
                {
                    targetHelperType = CloneHelperType(sourceHelperType, module);
                    module.Types.Add(targetHelperType);
                }

                TypeDefinition dispatcherType = module.GetType(DispatcherTypeName);
                if (dispatcherType == null)
                {
                    throw new InvalidOperationException("Dispatcher type not found: " + DispatcherTypeName);
                }

                TypeDefinition receiverType = module.GetType(ReceiverTypeName);
                if (receiverType == null)
                {
                    throw new InvalidOperationException("Receiver type not found: " + ReceiverTypeName);
                }

                TypeDefinition playerWindowCoordinatorType = module.GetType(PlayerWindowCoordinatorTypeName);
                MethodDefinition ensurePlayerWindowMethod = playerWindowCoordinatorType != null
                    ? FindMethod(playerWindowCoordinatorType, EnsurePlayerWindowMethodName, 0)
                    : null;

                DispatcherHookResolution hookResolution = ResolveDispatcherHooks(dispatcherType);
                MethodDefinition openMethod = hookResolution.OpenMethod;
                MethodDefinition playMethod = hookResolution.PlayMethod;
                MethodDefinition receiverMethod = FindTypedMethod(receiverType, "Receive", PlayMessageTypeName);

                if (playMethod == null)
                {
                    throw new InvalidOperationException(hookResolution.BuildFailureMessage());
                }

                if (receiverMethod == null)
                {
                    throw new InvalidOperationException(
                        "Receiver method not found: " + ReceiverTypeName + ".Receive(" + PlayMessageTypeName + ")");
                }

                if (hookResolution.HasResolutionNotes)
                {
                    Console.WriteLine(hookResolution.ResolutionNotes);
                }

                Console.WriteLine("Resolved player window ensure method: " +
                    (ensurePlayerWindowMethod != null
                        ? FormatMethodSignature(ensurePlayerWindowMethod)
                        : "<not found; window suppression patch skipped>"));

                MethodReference hasConfiguredMethod = FindMethod(targetHelperType, "HasConfiguredExternalPlayer", 0);
                MethodReference tryPlayMethod = FindMethod(targetHelperType, "TryPlay", 1);
                MethodReference traceMethod = FindMethod(targetHelperType, TraceMethodName, 1);
                if (hasConfiguredMethod == null || tryPlayMethod == null || traceMethod == null)
                {
                    throw new InvalidOperationException("Target helper type is missing required methods.");
                }

                bool senderAlreadyPatched = CallsHelperMethod(playMethod, "TryPlay");
                bool receiverAlreadyPatched = CallsHelperMethod(receiverMethod, "TryPlay");
                bool playerWindowAlreadyPatched = ensurePlayerWindowMethod != null &&
                    CallsHelperMethod(ensurePlayerWindowMethod, "HasConfiguredExternalPlayer");

                MethodDefinition openFallbackMethod = openMethod != null
                    ? EnsureFallbackMethod(openMethod, OpenFallbackMethodName, dispatcherType)
                    : null;
                MethodDefinition playFallbackMethod = EnsureFallbackMethod(playMethod, PlayFallbackMethodName, dispatcherType);
                MethodDefinition receiverFallbackMethod = EnsureFallbackMethod(receiverMethod, ReceiverFallbackMethodName, receiverType);
                MethodDefinition ensurePlayerWindowFallbackMethod = ensurePlayerWindowMethod != null
                    ? EnsureFallbackMethod(
                        ensurePlayerWindowMethod,
                        EnsurePlayerWindowFallbackMethodName,
                        playerWindowCoordinatorType)
                    : null;
                MethodReference completedTaskGetter = module.ImportReference(
                    typeof(System.Threading.Tasks.Task).GetProperty("CompletedTask").GetGetMethod());

                if (!senderAlreadyPatched)
                {
                    RewriteOpenMethod(
                        openMethod,
                        module.ImportReference(hasConfiguredMethod),
                        module.ImportReference(traceMethod),
                        openFallbackMethod);
                    RewritePlayMethod(
                        playMethod,
                        module.ImportReference(tryPlayMethod),
                        module.ImportReference(traceMethod),
                        openFallbackMethod,
                        playFallbackMethod);
                }
                else
                {
                    Console.WriteLine("Dispatcher sender hook already patched, keeping existing sender rewrite.");
                }

                if (!receiverAlreadyPatched)
                {
                    RewritePlayMessageReceiverMethod(
                        receiverMethod,
                        module.ImportReference(tryPlayMethod),
                        module.ImportReference(traceMethod),
                        receiverFallbackMethod);
                }
                else
                {
                    Console.WriteLine("Play message receiver hook already patched, keeping existing receiver rewrite.");
                }

                if (ensurePlayerWindowMethod == null)
                {
                    Console.WriteLine("Player window ensure hook not found, skipping player window suppression patch.");
                }
                else if (!playerWindowAlreadyPatched)
                {
                    RewriteEnsurePlayerWindowMethod(
                        ensurePlayerWindowMethod,
                        module.ImportReference(hasConfiguredMethod),
                        module.ImportReference(traceMethod),
                        completedTaskGetter,
                        ensurePlayerWindowFallbackMethod);
                }
                else
                {
                    Console.WriteLine("Player window ensure hook already patched, keeping existing window suppression rewrite.");
                }

                targetAssembly.Write(outputPath);
            }
        }
    }

    private sealed class CloneContext
    {
        internal CloneContext(ModuleDefinition targetModule)
        {
            TargetModule = targetModule;
        }

        internal ModuleDefinition TargetModule { get; private set; }

        internal Dictionary<TypeDefinition, TypeDefinition> TypeMap { get; } =
            new Dictionary<TypeDefinition, TypeDefinition>();

        internal Dictionary<FieldDefinition, FieldDefinition> FieldMap { get; } =
            new Dictionary<FieldDefinition, FieldDefinition>();

        internal Dictionary<MethodDefinition, MethodDefinition> MethodMap { get; } =
            new Dictionary<MethodDefinition, MethodDefinition>();
    }

    private static TypeDefinition CloneHelperType(TypeDefinition sourceType, ModuleDefinition targetModule)
    {
        CloneContext context = new CloneContext(targetModule);
        TypeDefinition targetType = CreateTypeSkeleton(sourceType, null, context);
        CloneTypeMembers(sourceType, context);
        CloneTypeBodies(sourceType, context);
        return targetType;
    }

    private static TypeDefinition CreateTypeSkeleton(
        TypeDefinition sourceType,
        TypeDefinition targetDeclaringType,
        CloneContext context)
    {
        TypeDefinition targetType = new TypeDefinition(
            sourceType.Namespace,
            sourceType.Name,
            sourceType.Attributes,
            ImportTypeReference(sourceType.BaseType, context));

        context.TypeMap[sourceType] = targetType;

        if (targetDeclaringType != null)
        {
            targetDeclaringType.NestedTypes.Add(targetType);
        }

        foreach (TypeDefinition nestedType in sourceType.NestedTypes)
        {
            CreateTypeSkeleton(nestedType, targetType, context);
        }

        return targetType;
    }

    private static void CloneTypeMembers(TypeDefinition sourceType, CloneContext context)
    {
        TypeDefinition targetType = context.TypeMap[sourceType];

        foreach (FieldDefinition sourceField in sourceType.Fields)
        {
            FieldDefinition targetField = new FieldDefinition(
                sourceField.Name,
                sourceField.Attributes,
                ImportTypeReference(sourceField.FieldType, context));
            if (sourceField.HasConstant)
            {
                targetField.Constant = sourceField.Constant;
            }

            targetType.Fields.Add(targetField);
            context.FieldMap[sourceField] = targetField;
        }

        foreach (MethodDefinition sourceMethod in sourceType.Methods)
        {
            MethodDefinition targetMethod = new MethodDefinition(
                sourceMethod.Name,
                sourceMethod.Attributes,
                ImportTypeReference(sourceMethod.ReturnType, context));
            targetMethod.ImplAttributes = sourceMethod.ImplAttributes;
            targetMethod.SemanticsAttributes = sourceMethod.SemanticsAttributes;
            targetType.Methods.Add(targetMethod);
            context.MethodMap[sourceMethod] = targetMethod;
            CloneMethodSignature(sourceMethod, targetMethod, context);
        }

        foreach (TypeDefinition nestedType in sourceType.NestedTypes)
        {
            CloneTypeMembers(nestedType, context);
        }
    }

    private static void CloneTypeBodies(TypeDefinition sourceType, CloneContext context)
    {
        foreach (MethodDefinition sourceMethod in sourceType.Methods)
        {
            CloneMethodBody(sourceMethod, context.MethodMap[sourceMethod], context);
        }

        foreach (TypeDefinition nestedType in sourceType.NestedTypes)
        {
            CloneTypeBodies(nestedType, context);
        }
    }

    private static MethodDefinition CloneMethodForFallback(MethodDefinition sourceMethod, string targetName, TypeDefinition ownerType)
    {
        CloneContext context = new CloneContext(ownerType.Module);
        MethodDefinition targetMethod = new MethodDefinition(
            targetName,
            MethodAttributes.Private | MethodAttributes.HideBySig,
            ImportTypeReference(sourceMethod.ReturnType, context));
        targetMethod.ImplAttributes = sourceMethod.ImplAttributes;
        ownerType.Methods.Add(targetMethod);
        CloneMethodSignature(sourceMethod, targetMethod, context);
        CloneMethodBody(sourceMethod, targetMethod, context);
        return targetMethod;
    }

    private static MethodDefinition EnsureFallbackMethod(
        MethodDefinition sourceMethod,
        string targetName,
        TypeDefinition ownerType)
    {
        if (sourceMethod == null)
        {
            return null;
        }

        MethodDefinition existingMethod = FindMethod(ownerType, targetName, sourceMethod.Parameters.Count);
        return existingMethod ?? CloneMethodForFallback(sourceMethod, targetName, ownerType);
    }

    private static void CloneMethodSignature(
        MethodDefinition sourceMethod,
        MethodDefinition targetMethod,
        CloneContext context)
    {
        foreach (ParameterDefinition parameter in sourceMethod.Parameters)
        {
            targetMethod.Parameters.Add(new ParameterDefinition(
                parameter.Name,
                parameter.Attributes,
                ImportTypeReference(parameter.ParameterType, context)));
        }
    }

    private static void CloneMethodBody(
        MethodDefinition sourceMethod,
        MethodDefinition targetMethod,
        CloneContext context)
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
                ImportTypeReference(variable.VariableType, context));
            targetBody.Variables.Add(clonedVariable);
            variableMap[variable] = clonedVariable;
        }

        Dictionary<Instruction, Instruction> instructionMap = new Dictionary<Instruction, Instruction>();
        foreach (Instruction sourceInstruction in sourceBody.Instructions)
        {
            Instruction clonedInstruction = CloneInstruction(
                sourceInstruction,
                sourceMethod,
                targetMethod,
                context,
                variableMap,
                instructionMap);
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
                    ? ImportTypeReference(handler.CatchType, context)
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
        MethodDefinition sourceMethod,
        MethodDefinition targetMethod,
        CloneContext context,
        Dictionary<VariableDefinition, VariableDefinition> variableMap,
        Dictionary<Instruction, Instruction> instructionMap)
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
            MethodReference methodReference = (MethodReference)operand;
            return Instruction.Create(opCode, ImportMethodReference(
                methodReference,
                context,
                sourceMethod,
                targetMethod));
        }

        if (operand is FieldReference)
        {
            return Instruction.Create(opCode, ImportFieldReference(
                (FieldReference)operand,
                context));
        }

        if (operand is TypeReference)
        {
            return Instruction.Create(opCode, ImportTypeReference(
                (TypeReference)operand,
                context));
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

    private static TypeReference ImportTypeReference(TypeReference sourceType, CloneContext context)
    {
        if (sourceType == null)
        {
            return null;
        }

        if (sourceType is ArrayType)
        {
            ArrayType arrayType = (ArrayType)sourceType;
            return new ArrayType(ImportTypeReference(arrayType.ElementType, context), arrayType.Rank);
        }

        if (sourceType is ByReferenceType)
        {
            ByReferenceType byReferenceType = (ByReferenceType)sourceType;
            return new ByReferenceType(ImportTypeReference(byReferenceType.ElementType, context));
        }

        if (sourceType is PointerType)
        {
            PointerType pointerType = (PointerType)sourceType;
            return new PointerType(ImportTypeReference(pointerType.ElementType, context));
        }

        if (sourceType is PinnedType)
        {
            PinnedType pinnedType = (PinnedType)sourceType;
            return new PinnedType(ImportTypeReference(pinnedType.ElementType, context));
        }

        if (sourceType is OptionalModifierType)
        {
            OptionalModifierType optionalModifierType = (OptionalModifierType)sourceType;
            return new OptionalModifierType(
                ImportTypeReference(optionalModifierType.ModifierType, context),
                ImportTypeReference(optionalModifierType.ElementType, context));
        }

        if (sourceType is RequiredModifierType)
        {
            RequiredModifierType requiredModifierType = (RequiredModifierType)sourceType;
            return new RequiredModifierType(
                ImportTypeReference(requiredModifierType.ModifierType, context),
                ImportTypeReference(requiredModifierType.ElementType, context));
        }

        if (sourceType is SentinelType)
        {
            SentinelType sentinelType = (SentinelType)sourceType;
            return new SentinelType(ImportTypeReference(sentinelType.ElementType, context));
        }

        if (sourceType is GenericInstanceType)
        {
            GenericInstanceType genericInstanceType = (GenericInstanceType)sourceType;
            GenericInstanceType targetGenericInstance = new GenericInstanceType(
                ImportTypeReference(genericInstanceType.ElementType, context));
            foreach (TypeReference argument in genericInstanceType.GenericArguments)
            {
                targetGenericInstance.GenericArguments.Add(ImportTypeReference(argument, context));
            }

            return targetGenericInstance;
        }

        if (sourceType is GenericParameter)
        {
            return sourceType;
        }

        TypeDefinition resolvedType = ResolveTypeDefinition(sourceType);
        if (resolvedType != null && context.TypeMap.ContainsKey(resolvedType))
        {
            return context.TypeMap[resolvedType];
        }

        return context.TargetModule.ImportReference(sourceType);
    }

    private static FieldReference ImportFieldReference(
        FieldReference sourceField,
        CloneContext context)
    {
        if (sourceField == null)
        {
            return null;
        }

        FieldDefinition resolvedField = ResolveFieldDefinition(sourceField);
        if (resolvedField != null && context.FieldMap.ContainsKey(resolvedField))
        {
            return context.FieldMap[resolvedField];
        }

        TypeDefinition declaringType = ResolveTypeDefinition(sourceField.DeclaringType);
        if (declaringType != null && context.TypeMap.ContainsKey(declaringType))
        {
            return new FieldReference(
                sourceField.Name,
                ImportTypeReference(sourceField.FieldType, context),
                context.TypeMap[declaringType]);
        }

        return context.TargetModule.ImportReference(sourceField);
    }

    private static MethodReference ImportMethodReference(
        MethodReference sourceMethodReference,
        CloneContext context,
        MethodDefinition sourceMethod,
        MethodDefinition targetMethod)
    {
        if (sourceMethodReference is GenericInstanceMethod)
        {
            GenericInstanceMethod genericInstanceMethod = (GenericInstanceMethod)sourceMethodReference;
            MethodReference elementMethod = ImportMethodReference(
                genericInstanceMethod.ElementMethod,
                context,
                sourceMethod,
                targetMethod);
            GenericInstanceMethod targetGenericInstance = new GenericInstanceMethod(elementMethod);
            foreach (TypeReference argument in genericInstanceMethod.GenericArguments)
            {
                targetGenericInstance.GenericArguments.Add(ImportTypeReference(argument, context));
            }

            return targetGenericInstance;
        }

        MethodDefinition resolvedMethod = ResolveMethodDefinition(sourceMethodReference);
        if (resolvedMethod != null && context.MethodMap.ContainsKey(resolvedMethod))
        {
            return context.MethodMap[resolvedMethod];
        }

        if (resolvedMethod == sourceMethod)
        {
            return targetMethod;
        }

        TypeDefinition declaringType = ResolveTypeDefinition(sourceMethodReference.DeclaringType);
        if (declaringType != null && context.TypeMap.ContainsKey(declaringType))
        {
            MethodReference targetReference = new MethodReference(
                sourceMethodReference.Name,
                ImportTypeReference(sourceMethodReference.ReturnType, context),
                context.TypeMap[declaringType]);
            targetReference.HasThis = sourceMethodReference.HasThis;
            targetReference.ExplicitThis = sourceMethodReference.ExplicitThis;
            targetReference.CallingConvention = sourceMethodReference.CallingConvention;
            foreach (ParameterDefinition parameter in sourceMethodReference.Parameters)
            {
                targetReference.Parameters.Add(new ParameterDefinition(
                    ImportTypeReference(parameter.ParameterType, context)));
            }

            return targetReference;
        }

        return context.TargetModule.ImportReference(sourceMethodReference);
    }

    private static TypeDefinition ResolveTypeDefinition(TypeReference typeReference)
    {
        if (typeReference == null)
        {
            return null;
        }

        try
        {
            return typeReference.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static FieldDefinition ResolveFieldDefinition(FieldReference fieldReference)
    {
        if (fieldReference == null)
        {
            return null;
        }

        try
        {
            return fieldReference.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static MethodDefinition ResolveMethodDefinition(MethodReference methodReference)
    {
        if (methodReference == null)
        {
            return null;
        }

        try
        {
            return methodReference.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static void RewriteOpenMethod(
        MethodDefinition openMethod,
        MethodReference hasConfiguredMethod,
        MethodReference traceMethod,
        MethodDefinition fallbackMethod)
    {
        if (openMethod == null || fallbackMethod == null)
        {
            return;
        }

        MethodBody body = openMethod.Body;
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        body.InitLocals = false;
        body.MaxStackSize = 2;

        ILProcessor il = body.GetILProcessor();
        Instruction useBuiltin = il.Create(OpCodes.Nop);
        string tracePrefix = BuildTracePrefix(openMethod);

        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " enter"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Call, hasConfiguredMethod));
        il.Append(il.Create(OpCodes.Brfalse_S, useBuiltin));
        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " skip builtin player window because external player is configured"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(useBuiltin);
        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " open builtin player window"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, fallbackMethod));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewritePlayMethod(
        MethodDefinition playMethod,
        MethodReference tryPlayMethod,
        MethodReference traceMethod,
        MethodDefinition openFallbackMethod,
        MethodDefinition fallbackMethod)
    {
        MethodBody body = playMethod.Body;
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        body.InitLocals = false;
        body.MaxStackSize = 2;

        ILProcessor il = body.GetILProcessor();
        Instruction useBuiltin = il.Create(OpCodes.Nop);
        string tracePrefix = BuildTracePrefix(playMethod);

        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " enter"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, tryPlayMethod));
        il.Append(il.Create(OpCodes.Brfalse_S, useBuiltin));
        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " external player handled playback"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(useBuiltin);
        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " fallback to builtin player"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        if (openFallbackMethod != null)
        {
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, openFallbackMethod));
        }
        else
        {
            il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " no dispatcher open hook found; fallback directly to builtin play"));
            il.Append(il.Create(OpCodes.Call, traceMethod));
        }

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, fallbackMethod));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewritePlayMessageReceiverMethod(
        MethodDefinition receiverMethod,
        MethodReference tryPlayMethod,
        MethodReference traceMethod,
        MethodDefinition fallbackMethod)
    {
        if (receiverMethod == null || fallbackMethod == null)
        {
            return;
        }

        MethodReference messageValueGetter = ResolvePlayMessageValueGetter(receiverMethod);
        if (messageValueGetter == null)
        {
            throw new InvalidOperationException("Play message value getter not found for receiver method.");
        }

        MethodBody body = receiverMethod.Body;
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        body.InitLocals = false;
        body.MaxStackSize = 2;

        ILProcessor il = body.GetILProcessor();
        Instruction useBuiltin = il.Create(OpCodes.Nop);
        string tracePrefix = BuildTracePrefix(receiverMethod);

        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " enter"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Brfalse_S, useBuiltin));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Callvirt, messageValueGetter));
        il.Append(il.Create(OpCodes.Call, tryPlayMethod));
        il.Append(il.Create(OpCodes.Brfalse_S, useBuiltin));
        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " external player handled play message"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(useBuiltin);
        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " fallback to builtin play message receiver"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, fallbackMethod));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteEnsurePlayerWindowMethod(
        MethodDefinition ensurePlayerWindowMethod,
        MethodReference hasConfiguredMethod,
        MethodReference traceMethod,
        MethodReference completedTaskGetter,
        MethodDefinition fallbackMethod)
    {
        if (ensurePlayerWindowMethod == null ||
            fallbackMethod == null ||
            completedTaskGetter == null)
        {
            return;
        }

        MethodBody body = ensurePlayerWindowMethod.Body;
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        body.InitLocals = false;
        body.MaxStackSize = 2;

        ILProcessor il = body.GetILProcessor();
        Instruction useBuiltin = il.Create(OpCodes.Nop);
        string tracePrefix = BuildTracePrefix(ensurePlayerWindowMethod);

        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " enter"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Call, hasConfiguredMethod));
        il.Append(il.Create(OpCodes.Brfalse_S, useBuiltin));
        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " skip builtin player window creation because external player is configured"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Call, completedTaskGetter));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(useBuiltin);
        il.Append(il.Create(OpCodes.Ldstr, tracePrefix + " ensure builtin player window"));
        il.Append(il.Create(OpCodes.Call, traceMethod));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, fallbackMethod));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void ReplaceTarget(string patchedPath, string targetPath)
    {
        File.Copy(patchedPath, targetPath, true);
    }

    private static void VerifyAssembly(string targetPath)
    {
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(targetPath, CreateReaderParameters(targetPath, null)))
        {
            ModuleDefinition module = assembly.MainModule;

            TypeDefinition helperType = module.GetType(HelperTypeName);
            TypeDefinition dispatcherType = module.GetType(DispatcherTypeName);
            if (helperType == null || dispatcherType == null)
            {
                throw new InvalidOperationException("Verification failed: helper or dispatcher type missing.");
            }

            DispatcherHookResolution hookResolution = ResolveDispatcherHooks(dispatcherType);
            MethodDefinition openMethod = hookResolution.OpenMethod;
            MethodDefinition playMethod = hookResolution.PlayMethod;
            TypeDefinition receiverType = module.GetType(ReceiverTypeName);
            MethodDefinition receiverMethod = receiverType != null
                ? FindTypedMethod(receiverType, "Receive", PlayMessageTypeName)
                : null;
            TypeDefinition playerWindowCoordinatorType = module.GetType(PlayerWindowCoordinatorTypeName);
            MethodDefinition ensurePlayerWindowMethod = playerWindowCoordinatorType != null
                ? FindMethod(playerWindowCoordinatorType, EnsurePlayerWindowMethodName, 0)
                : null;

            bool hasOpenFallback = HasMethod(dispatcherType, OpenFallbackMethodName);
            bool hasPlayFallback = HasMethod(dispatcherType, PlayFallbackMethodName);
            bool playCallsHelper = playMethod != null && CallsHelperMethod(playMethod, "TryPlay");
            bool hasReceiverFallback = receiverType != null && HasMethod(receiverType, ReceiverFallbackMethodName);
            bool receiverCallsHelper = receiverMethod != null && CallsHelperMethod(receiverMethod, "TryPlay");
            bool hasEnsurePlayerWindowFallback = playerWindowCoordinatorType != null &&
                HasMethod(playerWindowCoordinatorType, EnsurePlayerWindowFallbackMethodName);
            bool ensurePlayerWindowCallsHelper = ensurePlayerWindowMethod != null &&
                CallsHelperMethod(ensurePlayerWindowMethod, "HasConfiguredExternalPlayer");

            if (playMethod == null)
            {
                throw new InvalidOperationException("Verification failed: play method not resolved after patching.\n" +
                    DescribeDispatcherMethods(dispatcherType));
            }

            if (receiverType == null || receiverMethod == null)
            {
                throw new InvalidOperationException("Verification failed: play message receiver method missing.");
            }

            if ((openMethod != null && !hasOpenFallback) || !hasPlayFallback || !playCallsHelper)
            {
                throw new InvalidOperationException("Verification failed: wrapper methods are incomplete.");
            }

            if (!hasReceiverFallback || !receiverCallsHelper)
            {
                throw new InvalidOperationException("Verification failed: receiver wrapper method is incomplete.");
            }

            if (ensurePlayerWindowMethod != null &&
                (!hasEnsurePlayerWindowFallback || !ensurePlayerWindowCallsHelper))
            {
                throw new InvalidOperationException("Verification failed: player window suppression wrapper is incomplete.");
            }

            Console.WriteLine("Verified helper type: " + HelperTypeName);
            Console.WriteLine("Verified dispatcher type: " + DispatcherTypeName);
            Console.WriteLine("Verified open method fallback: " + hasOpenFallback);
            Console.WriteLine("Verified play method fallback: " + hasPlayFallback);
            Console.WriteLine("Verified play message receiver fallback: " + hasReceiverFallback);
            Console.WriteLine("Verified player window suppression fallback: " + hasEnsurePlayerWindowFallback);
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

    private static MethodDefinition FindMediaPlayMethod(TypeDefinition ownerType)
    {
        foreach (MethodDefinition method in ownerType.Methods)
        {
            if (method.Name == PlayMethodName &&
                method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType.FullName == MediaViewModelTypeName)
            {
                return method;
            }
        }

        return null;
    }

    private static MethodDefinition FindTypedMethod(
        TypeDefinition ownerType,
        string methodName,
        string parameterTypeFullName)
    {
        foreach (MethodDefinition method in ownerType.Methods)
        {
            if (method.Name != methodName || method.Parameters.Count != 1)
            {
                continue;
            }

            if (method.Parameters[0].ParameterType.FullName == parameterTypeFullName)
            {
                return method;
            }
        }

        return null;
    }

    private static DispatcherHookResolution ResolveDispatcherHooks(TypeDefinition dispatcherType)
    {
        DispatcherHookResolution resolution = new DispatcherHookResolution();
        resolution.PlayMethod = ResolvePlayMethod(dispatcherType);
        resolution.OpenMethod = ResolveOpenMethod(dispatcherType);

        List<string> notes = new List<string>();
        notes.Add("Resolved dispatcher play method: " +
            (resolution.PlayMethod != null ? FormatMethodSignature(resolution.PlayMethod) : "<not found>"));
        notes.Add("Resolved dispatcher open method: " +
            (resolution.OpenMethod != null ? FormatMethodSignature(resolution.OpenMethod) : "<not found; play-only patch mode>"));
        resolution.ResolutionNotes = string.Join(Environment.NewLine, notes.ToArray());
        resolution.Diagnostics = DescribeDispatcherMethods(dispatcherType);
        return resolution;
    }

    private static MethodDefinition ResolvePlayMethod(TypeDefinition ownerType)
    {
        MethodDefinition exactMethod = FindMediaPlayMethod(ownerType);
        if (exactMethod != null)
        {
            return exactMethod;
        }

        List<MethodDefinition> mediaCandidates = new List<MethodDefinition>();
        List<MethodDefinition> messageCandidates = new List<MethodDefinition>();

        foreach (MethodDefinition method in ownerType.Methods)
        {
            if (!LooksLikeDispatcherPlayMethod(method))
            {
                continue;
            }

            mediaCandidates.Add(method);
            if (CallsMemberContaining(method, PlayMessageTypeHint))
            {
                messageCandidates.Add(method);
            }
        }

        if (messageCandidates.Count == 1)
        {
            return messageCandidates[0];
        }

        if (mediaCandidates.Count == 1)
        {
            return mediaCandidates[0];
        }

        return null;
    }

    private static MethodDefinition ResolveOpenMethod(TypeDefinition ownerType)
    {
        MethodDefinition exactMethod = FindMethod(ownerType, OpenMethodName, 0);
        if (exactMethod != null)
        {
            return exactMethod;
        }

        List<MethodDefinition> candidates = new List<MethodDefinition>();
        foreach (MethodDefinition method in ownerType.Methods)
        {
            if (!LooksLikeDispatcherOpenMethod(method))
            {
                continue;
            }

            if (CallsAnyMemberContaining(method, new[] { "Player", "Playback", "VLC", "Window", "Dialog", "Page" }) ||
                UsesAnyTypeContaining(method, new[] { "Player", "Playback", "VLC", "Window", "Dialog", "Page" }) ||
                UsesAnyStringLiteralContaining(method, new[] { "player", "playback", "vlc", "window", "dialog", "page" }))
            {
                candidates.Add(method);
            }
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        return null;
    }

    private static bool LooksLikeDispatcherPlayMethod(MethodDefinition method)
    {
        return method != null &&
            !method.IsConstructor &&
            !method.IsStatic &&
            method.HasBody &&
            method.ReturnType.FullName == "System.Void" &&
            method.Parameters.Count == 1 &&
            LooksLikeMediaViewModelType(method.Parameters[0].ParameterType);
    }

    private static bool LooksLikeDispatcherOpenMethod(MethodDefinition method)
    {
        return method != null &&
            !method.IsConstructor &&
            !method.IsStatic &&
            method.HasBody &&
            method.ReturnType.FullName == "System.Void" &&
            method.Parameters.Count == 0;
    }

    private static bool LooksLikeMediaViewModelType(TypeReference typeReference)
    {
        if (typeReference == null)
        {
            return false;
        }

        if (typeReference.FullName == MediaViewModelTypeName)
        {
            return true;
        }

        return string.Equals(typeReference.Name, "MediaViewModel", StringComparison.Ordinal) ||
            typeReference.FullName.EndsWith(".MediaViewModel", StringComparison.Ordinal);
    }

    private static bool HasMethod(TypeDefinition ownerType, string methodName)
    {
        foreach (MethodDefinition method in ownerType.Methods)
        {
            if (method.Name == methodName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CallsMemberContaining(MethodDefinition method, string text)
    {
        if (method == null || !method.HasBody || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (Instruction instruction in method.Body.Instructions)
        {
            MethodReference reference = instruction.Operand as MethodReference;
            if (reference != null &&
                (ContainsIgnoreCase(reference.FullName, text) || ContainsIgnoreCase(reference.Name, text)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CallsAnyMemberContaining(MethodDefinition method, string[] texts)
    {
        if (texts == null)
        {
            return false;
        }

        foreach (string text in texts)
        {
            if (CallsMemberContaining(method, text))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UsesAnyTypeContaining(MethodDefinition method, string[] texts)
    {
        if (method == null || !method.HasBody || texts == null)
        {
            return false;
        }

        foreach (Instruction instruction in method.Body.Instructions)
        {
            TypeReference reference = instruction.Operand as TypeReference;
            if (reference == null)
            {
                continue;
            }

            foreach (string text in texts)
            {
                if (ContainsIgnoreCase(reference.FullName, text) || ContainsIgnoreCase(reference.Name, text))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool UsesAnyStringLiteralContaining(MethodDefinition method, string[] texts)
    {
        if (method == null || !method.HasBody || texts == null)
        {
            return false;
        }

        foreach (Instruction instruction in method.Body.Instructions)
        {
            string value = instruction.Operand as string;
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            foreach (string text in texts)
            {
                if (ContainsIgnoreCase(value, text))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static MethodReference ResolvePlayMessageValueGetter(MethodDefinition receiverMethod)
    {
        if (receiverMethod == null || receiverMethod.Parameters.Count != 1)
        {
            return null;
        }

        TypeReference messageType = receiverMethod.Parameters[0].ParameterType;
        TypeDefinition resolvedMessageType = ResolveTypeDefinition(messageType);
        if (resolvedMessageType == null)
        {
            return null;
        }

        TypeReference baseType = resolvedMessageType.BaseType;
        GenericInstanceType genericBaseType = baseType as GenericInstanceType;
        TypeDefinition baseDefinition = ResolveTypeDefinition(baseType);
        if (genericBaseType == null || baseDefinition == null)
        {
            return null;
        }

        MethodDefinition getterDefinition = FindMethod(baseDefinition, "get_Value", 0);
        if (getterDefinition == null)
        {
            return null;
        }

        TypeReference valueType = genericBaseType.GenericArguments.Count > 0
            ? genericBaseType.GenericArguments[0]
            : receiverMethod.Module.TypeSystem.Object;

        MethodReference getterReference = new MethodReference(
            getterDefinition.Name,
            valueType,
            genericBaseType);
        getterReference.HasThis = getterDefinition.HasThis;
        getterReference.ExplicitThis = getterDefinition.ExplicitThis;
        getterReference.CallingConvention = getterDefinition.CallingConvention;
        return receiverMethod.Module.ImportReference(getterReference);
    }

    private static bool CallsHelperMethod(MethodDefinition method, string helperMethodName)
    {
        if (method == null || !method.HasBody)
        {
            return false;
        }

        foreach (Instruction instruction in method.Body.Instructions)
        {
            MethodReference reference = instruction.Operand as MethodReference;
            if (instruction.OpCode == OpCodes.Call &&
                reference != null &&
                reference.DeclaringType.FullName == HelperTypeName &&
                reference.Name == helperMethodName)
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeDispatcherMethods(TypeDefinition dispatcherType)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Dispatcher methods:");
        foreach (MethodDefinition method in dispatcherType.Methods)
        {
            builder.Append(" - ");
            builder.AppendLine(DescribeMethod(method));
        }

        return builder.ToString().TrimEnd();
    }

    private static string DescribeMethod(MethodDefinition method)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(FormatMethodSignature(method));
        builder.Append(" | calls=");

        List<string> hints = new List<string>();
        if (method != null && method.HasBody)
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                MethodReference reference = instruction.Operand as MethodReference;
                if (reference == null)
                {
                    continue;
                }

                string hint = reference.DeclaringType.Name + "::" + reference.Name;
                if (!hints.Contains(hint))
                {
                    hints.Add(hint);
                }
            }
        }

        if (hints.Count == 0)
        {
            builder.Append("<none>");
        }
        else
        {
            builder.Append(string.Join(", ", hints.ToArray()));
        }

        return builder.ToString();
    }

    private static string FormatMethodSignature(MethodDefinition method)
    {
        if (method == null)
        {
            return "<null>";
        }

        List<string> parameters = new List<string>();
        foreach (ParameterDefinition parameter in method.Parameters)
        {
            parameters.Add(parameter.ParameterType.FullName);
        }

        return method.ReturnType.FullName + " " + method.Name + "(" + string.Join(", ", parameters.ToArray()) + ")";
    }

    private static string BuildTracePrefix(MethodDefinition method)
    {
        if (method == null || method.DeclaringType == null)
        {
            return DispatcherTypeName + ".<unknown>";
        }

        return method.DeclaringType.FullName + "." + method.Name;
    }

    private static bool ContainsIgnoreCase(string source, string text)
    {
        return !string.IsNullOrEmpty(source) &&
            !string.IsNullOrEmpty(text) &&
            source.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed class DispatcherHookResolution
    {
        internal MethodDefinition OpenMethod { get; set; }

        internal MethodDefinition PlayMethod { get; set; }

        internal string ResolutionNotes { get; set; }

        internal string Diagnostics { get; set; }

        internal bool HasResolutionNotes
        {
            get { return !string.IsNullOrWhiteSpace(ResolutionNotes); }
        }

        internal string BuildFailureMessage()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Dispatcher hooks not resolved.");
            if (!string.IsNullOrWhiteSpace(ResolutionNotes))
            {
                builder.AppendLine(ResolutionNotes);
            }

            if (!string.IsNullOrWhiteSpace(Diagnostics))
            {
                builder.AppendLine(Diagnostics);
            }

            return builder.ToString().TrimEnd();
        }
    }
}
