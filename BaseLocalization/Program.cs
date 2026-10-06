using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 3) throw new ArgumentException("Usage: BaseLocalization <original-dll> <managed-directory> <output-dll>");
var srcDll = Path.GetFullPath(args[0]);
var managedDir = Path.GetFullPath(args[1]);
var pureDll = Path.GetFullPath(args[2]);
var originalHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(srcDll))).ToLowerInvariant();
if (originalHash != "d614f88984d843726c1c3f702aae5eac6455682bc4c8a6393c1d1b0c9533dc76") throw new Exception("Unsupported original DLL SHA256");
Directory.CreateDirectory(Path.GetDirectoryName(pureDll));

var cecilResolver = new DefaultAssemblyResolver();
cecilResolver.AddSearchDirectory(managedDir);
var readerParams = new ReaderParameters { AssemblyResolver = cecilResolver };

var asm = AssemblyDefinition.ReadAssembly(srcDll, readerParams);

// Only Localization changes (no gameplay tweaks)
var locType = asm.MainModule.GetType("Localization");
foreach (var m in locType.Methods)
{
    if (m.Name == ".cctor")
    {
        var il = m.Body.GetILProcessor();
        var instructions = m.Body.Instructions.ToList();

        // AvailableLanguages add "id"
        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode == OpCodes.Stsfld && instr.Operand is FieldReference fr && fr.Name == "_availableLanguages")
            {
                var addMethod = ((MethodReference)instructions[i - 1].Operand);
                il.InsertBefore(instr, il.Create(OpCodes.Dup));
                il.InsertBefore(instr, il.Create(OpCodes.Ldstr, "id"));
                il.InsertBefore(instr, il.Create(OpCodes.Callvirt, addMethod));
                break;
            }
        }

        // DefaultLanguage = "id"
        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode == OpCodes.Stsfld && instr.Operand is FieldReference fr2 && fr2.Name == "DefaultLanguage")
            {
                instructions[i - 1].Operand = "id";
                break;
            }
        }

        // CustomDirectory = "Localization"
        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode == OpCodes.Stsfld && instr.Operand is FieldReference fr3 && fr3.Name == "CustomDirectory")
            {
                instructions[i - 1].Operand = "Localization";
                break;
            }
        }
    }

    if (m.Name == "Setup")
    {
        var il = m.Body.GetILProcessor();
        for (int i = 0; i < m.Body.Instructions.Count; i++)
        {
            var instr = m.Body.Instructions[i];
            if (instr.OpCode == OpCodes.Ldstr && (string)instr.Operand == "strings_")
            {
                var loadStringsMethod = (MethodReference)m.Body.Instructions[i + 2].Operand;
                var loadTextsMethod = (MethodReference)m.Body.Instructions[i + 5].Operand;
                var loadTextsOverrideMethod = (MethodReference)m.Body.Instructions[i + 8].Operand;
                var langField = (FieldReference)m.Body.Instructions[i + 1].Operand;

                var strType = asm.MainModule.TypeSystem.String.Resolve();
                MethodReference opInequality = null;
                foreach (var sm in strType.Methods)
                {
                    if (sm.Name == "op_Inequality")
                    {
                        opInequality = asm.MainModule.ImportReference(sm);
                        break;
                    }
                }

                il.InsertBefore(instr, il.Create(OpCodes.Ldsfld, langField));
                il.InsertBefore(instr, il.Create(OpCodes.Ldstr, "en"));
                il.InsertBefore(instr, il.Create(OpCodes.Call, opInequality));
                il.InsertBefore(instr, il.Create(OpCodes.Brfalse, instr));

                il.InsertBefore(instr, il.Create(OpCodes.Ldstr, "strings_"));
                il.InsertBefore(instr, il.Create(OpCodes.Ldstr, "en"));
                il.InsertBefore(instr, il.Create(OpCodes.Call, loadStringsMethod));

                il.InsertBefore(instr, il.Create(OpCodes.Ldstr, "text_"));
                il.InsertBefore(instr, il.Create(OpCodes.Ldstr, "en"));
                il.InsertBefore(instr, il.Create(OpCodes.Call, loadTextsMethod));

                il.InsertBefore(instr, il.Create(OpCodes.Ldstr, "text_override_"));
                il.InsertBefore(instr, il.Create(OpCodes.Ldstr, "en"));
                il.InsertBefore(instr, il.Create(OpCodes.Call, loadTextsOverrideMethod));
                break;
            }
        }
    }
}

asm.Write(pureDll);
Console.WriteLine($"Saved pure localization DLL to {pureDll}");
