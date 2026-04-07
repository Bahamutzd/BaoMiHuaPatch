using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: DispatcherInspector <BaoMiHua.dll> [dispatcher-type-name]");
            return 1;
        }

        string assemblyPath = Path.GetFullPath(args[0]);
        string dispatcherTypeName = args.Length >= 2 ? args[1] : "a.ae";
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine("Assembly not found: " + assemblyPath);
            return 2;
        }

        ReaderParameters readerParameters = new ReaderParameters
        {
            InMemory = true,
            ReadWrite = false
        };

        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters))
        {
            ModuleDefinition module = assembly.MainModule;
            TypeDefinition dispatcherType = module.GetType(dispatcherTypeName);
            if (dispatcherType == null)
            {
                Console.Error.WriteLine("Dispatcher type not found: " + dispatcherTypeName);
                return 3;
            }

            Console.WriteLine("Assembly: " + assemblyPath);
            Console.WriteLine("Dispatcher: " + dispatcherType.FullName);
            Console.WriteLine("Methods:");

            foreach (MethodDefinition method in dispatcherType.Methods)
            {
                DumpMethod(method);
            }
        }

        return 0;
    }

    private static void DumpMethod(MethodDefinition method)
    {
        Console.WriteLine();
        Console.WriteLine("Method: " + BuildSignature(method));
        Console.WriteLine("  HasBody: " + method.HasBody);
        Console.WriteLine("  IsConstructor: " + method.IsConstructor);
        Console.WriteLine("  IsStatic: " + method.IsStatic);
        Console.WriteLine("  Variables: " + (method.HasBody ? method.Body.Variables.Count.ToString() : "0"));

        if (!method.HasBody)
        {
            return;
        }

        HashSet<string> calledMembers = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> fieldOperands = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> typeOperands = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> stringLiterals = new HashSet<string>(StringComparer.Ordinal);

        foreach (Instruction instruction in method.Body.Instructions)
        {
            MethodReference methodReference = instruction.Operand as MethodReference;
            if (methodReference != null)
            {
                calledMembers.Add(methodReference.FullName);
            }

            FieldReference fieldReference = instruction.Operand as FieldReference;
            if (fieldReference != null)
            {
                fieldOperands.Add(fieldReference.FullName);
            }

            TypeReference typeReference = instruction.Operand as TypeReference;
            if (typeReference != null)
            {
                typeOperands.Add(typeReference.FullName);
            }

            string stringOperand = instruction.Operand as string;
            if (!string.IsNullOrEmpty(stringOperand))
            {
                stringLiterals.Add(stringOperand);
            }
        }

        WriteCollection("Calls", calledMembers);
        WriteCollection("Fields", fieldOperands);
        WriteCollection("Types", typeOperands);
        WriteCollection("Strings", stringLiterals);
    }

    private static string BuildSignature(MethodDefinition method)
    {
        List<string> parameterSignatures = new List<string>();
        foreach (ParameterDefinition parameter in method.Parameters)
        {
            parameterSignatures.Add(parameter.ParameterType.FullName + " " + parameter.Name);
        }

        return method.ReturnType.FullName + " " + method.Name + "(" + string.Join(", ", parameterSignatures) + ")";
    }

    private static void WriteCollection(string label, IEnumerable<string> values)
    {
        List<string> items = new List<string>(values);
        items.Sort(StringComparer.Ordinal);
        Console.WriteLine("  " + label + ":");
        if (items.Count == 0)
        {
            Console.WriteLine("    <none>");
            return;
        }

        foreach (string item in items)
        {
            Console.WriteLine("    " + item);
        }
    }
}
