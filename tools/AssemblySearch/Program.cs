using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: AssemblySearch <assembly-path> <term1> [term2] ...");
            return 1;
        }

        string assemblyPath = Path.GetFullPath(args[0]);
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine("Assembly not found: " + assemblyPath);
            return 2;
        }

        List<string> terms = new List<string>();
        for (int index = 1; index < args.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(args[index]))
            {
                terms.Add(args[index]);
            }
        }

        if (terms.Count == 0)
        {
            Console.Error.WriteLine("No search terms.");
            return 3;
        }

        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
        {
            InMemory = true,
            ReadWrite = false
        }))
        {
            foreach (TypeDefinition type in EnumerateTypes(assembly.MainModule.Types))
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    List<string> matches = CollectMatches(method, terms);
                    if (matches.Count == 0)
                    {
                        continue;
                    }

                    Console.WriteLine("Method: " + BuildSignature(method));
                    foreach (string match in matches)
                    {
                        Console.WriteLine("  Match: " + match);
                    }

                    Console.WriteLine();
                }
            }
        }

        return 0;
    }

    private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition nested in EnumerateTypes(type.NestedTypes))
            {
                yield return nested;
            }
        }
    }

    private static List<string> CollectMatches(MethodDefinition method, List<string> terms)
    {
        List<string> matches = new List<string>();
        if (method == null)
        {
            return matches;
        }

        AddIfMatched(matches, "method " + method.FullName, terms);
        if (!method.HasBody)
        {
            return matches;
        }

        foreach (Instruction instruction in method.Body.Instructions)
        {
            MethodReference calledMethod = instruction.Operand as MethodReference;
            if (calledMethod != null)
            {
                AddIfMatched(matches, "call " + calledMethod.FullName, terms);
            }

            FieldReference fieldReference = instruction.Operand as FieldReference;
            if (fieldReference != null)
            {
                AddIfMatched(matches, "field " + fieldReference.FullName, terms);
            }

            TypeReference typeReference = instruction.Operand as TypeReference;
            if (typeReference != null)
            {
                AddIfMatched(matches, "type " + typeReference.FullName, terms);
            }

            string literal = instruction.Operand as string;
            if (!string.IsNullOrEmpty(literal))
            {
                AddIfMatched(matches, "string " + literal, terms);
            }
        }

        return matches;
    }

    private static void AddIfMatched(List<string> matches, string candidate, List<string> terms)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return;
        }

        foreach (string term in terms)
        {
            if (candidate.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!matches.Contains(candidate))
                {
                    matches.Add(candidate);
                }

                return;
            }
        }
    }

    private static string BuildSignature(MethodDefinition method)
    {
        List<string> parameterTypes = new List<string>();
        foreach (ParameterDefinition parameter in method.Parameters)
        {
            parameterTypes.Add(parameter.ParameterType.FullName);
        }

        return method.DeclaringType.FullName + "::" + method.Name +
            "(" + string.Join(", ", parameterTypes.ToArray()) + ")";
    }
}
