using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
void VerifyStack(MethodDefinition m){
 var code=m.Body.Instructions;var pending=new Queue<(Instruction,int)>();var seen=new Dictionary<Instruction,int>();pending.Enqueue((code[0],0));int peak=0;
 while(pending.Count>0){var (i,height)=pending.Dequeue();if(seen.TryGetValue(i,out var prev)){if(prev!=height)throw new Exception("Stack merge mismatch: "+m.Name+" "+i);continue;}seen[i]=height;
  int pop,push;
  if(i.OpCode==OpCodes.Call||i.OpCode==OpCodes.Callvirt||i.OpCode==OpCodes.Newobj){var callee=(MethodReference)i.Operand;pop=callee.Parameters.Count+(i.OpCode!=OpCodes.Newobj&&callee.HasThis?1:0);push=i.OpCode==OpCodes.Newobj||callee.ReturnType.MetadataType!=MetadataType.Void?1:0;}
  else if(i.OpCode==OpCodes.Ret){pop=m.ReturnType.MetadataType==MetadataType.Void?0:1;push=0;}
  else{pop=i.OpCode.StackBehaviourPop==StackBehaviour.Pop0?0:i.OpCode.StackBehaviourPop.ToString().Split('_').Length;push=i.OpCode.StackBehaviourPush==StackBehaviour.Push0?0:i.OpCode.StackBehaviourPush.ToString().Split('_').Length;}
  if(height<pop)throw new Exception("Stack underflow: "+m.Name+" "+i);var nextHeight=height-pop+push;peak=Math.Max(peak,nextHeight);
  if(i.OpCode.FlowControl==FlowControl.Return){if(nextHeight!=0)throw new Exception("Nonempty return stack "+m.Name);continue;}
  if(i.OpCode.FlowControl==FlowControl.Throw)continue;
  if(i.Operand is Instruction target)pending.Enqueue((target,nextHeight));
  if(i.Operand is Instruction[] targets)foreach(var t in targets)pending.Enqueue((t,nextHeight));
  if(i.OpCode.FlowControl!=FlowControl.Branch&&i.Next!=null)pending.Enqueue((i.Next,nextHeight));
 }
 if(peak>m.Body.MaxStackSize)throw new Exception("MaxStack too small: "+m.Name);
 Console.WriteLine("IL stack PASS "+m.Name+" max="+peak);
}
if(args.Length!=3)throw new ArgumentException("Usage: FixLocalization <baseline-dll> <managed-directory> <output-dll>");
var game=Path.GetFullPath(args[1]);
var baseline=Path.GetFullPath(args[0]);
if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline))).ToLowerInvariant()!="b84f6278bf6a57fe0ff2f431dd84644a1ea17f4bb6935928e2e4d55087cc74c0")throw new Exception("Unexpected baseline SHA256");
var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(game);
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
foreach(var variant in new[]{"vanilla"}){
 var src=baseline;
 using var asm=AssemblyDefinition.ReadAssembly(src,new ReaderParameters{AssemblyResolver=resolver});
 var module=asm.MainModule;var loc=module.GetType("Localization");
 MethodDefinition Method(string n)=>loc.Methods.Single(m=>m.Name==n);
 var allrefs=Types(module.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>().ToList();
 MethodReference Ref(string type,string name,int count)=>allrefs.First(m=>m.DeclaringType.FullName==type&&m.Name==name&&m.Parameters.Count==count);
 // Use the target game's framework references, never the patcher's .NET 9 references.
 var pathType=allrefs.First(m=>m.DeclaringType.FullName=="System.IO.Path").DeclaringType;
 var fullPath=new MethodReference("GetFullPath",module.TypeSystem.String,pathType);fullPath.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
 var combine=Ref("System.IO.Path","Combine",2);var appData=Ref("UnityEngine.Application","get_dataPath",0);
 var ctor=Method(".cctor");var il=ctor.Body.GetILProcessor();
 var dirStore=ctor.Body.Instructions.Single(i=>i.OpCode==OpCodes.Stsfld&&i.Operand is FieldReference f&&f.Name=="CustomDirectory");
 var oldDir=dirStore.Previous;if(oldDir.OpCode!=OpCodes.Ldstr||(string)oldDir.Operand!="Localization")throw new Exception("Unexpected directory initialization");
 oldDir.OpCode=OpCodes.Call;oldDir.Operand=appData;
 il.InsertBefore(dirStore,il.Create(OpCodes.Ldstr,"../Localization"));il.InsertBefore(dirStore,il.Create(OpCodes.Call,combine));il.InsertBefore(dirStore,il.Create(OpCodes.Call,fullPath));
 // Text default remains id; unsupported voice-over must fall back to en.
 var voice=Method("GetSupportedVoiceOverLanguage");
 var fallback=voice.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldsfld&&i.Operand is FieldReference f&&f.Name=="DefaultLanguage");fallback.OpCode=OpCodes.Ldstr;fallback.Operand="en";
 // FileExists must search the bundle when there is no matching disk file.
 var exists=Method("FileExists");il=exists.Body.GetILProcessor();
 var diskCheck=exists.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.Name=="FileExist");
 var diskReturn=diskCheck.Next;var bundleStart=diskReturn.Next;
 if(diskReturn.OpCode!=OpCodes.Ret||bundleStart.OpCode!=OpCodes.Ldsfld)throw new Exception("Unexpected FileExists control flow");
 diskReturn.OpCode=OpCodes.Brfalse;diskReturn.Operand=bundleStart;
 il.InsertAfter(diskReturn,il.Create(OpCodes.Ldc_I4_1));il.InsertAfter(diskReturn.Next,il.Create(OpCodes.Ret));
 // English core data always comes from the current game's bundle, not stale disk copies.
 var load=Method("LoadJSon");il=load.Body.GetILProcessor();
 var entry=load.Body.Instructions[0];
 var bundle=load.Body.Instructions.First(i=>i.OpCode==OpCodes.Ldsfld&&i.Operand is FieldReference f&&f.Name=="DefaultLocalizationDirectory");
 var equal=Ref("System.String","op_Equality",2);
 foreach(var part in new[]{"strings_en","text_en","text_override_en"}){
  il.InsertBefore(entry,il.Create(OpCodes.Ldarg_0));il.InsertBefore(entry,il.Create(OpCodes.Ldstr,part));il.InsertBefore(entry,il.Create(OpCodes.Call,equal));il.InsertBefore(entry,il.Create(OpCodes.Brtrue,bundle));
 }
 // Language label is present even when English core strings came from the bundle.
 var setup=Method("Setup");il=setup.Body.GetILProcessor();
 var beforeFonts=setup.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.Name=="SetupFallbacks");
 var db=loc.Fields.Single(f=>f.Name=="m_DataBase");
 var setter=Method("Parse").Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().Single(m=>m.Name=="set_Item");
 il.InsertBefore(beforeFonts,il.Create(OpCodes.Ldsfld,db));il.InsertBefore(beforeFonts,il.Create(OpCodes.Ldstr,"settings.language.id"));il.InsertBefore(beforeFonts,il.Create(OpCodes.Ldstr,"Bahasa Indonesia"));il.InsertBefore(beforeFonts,il.Create(OpCodes.Callvirt,setter));
 // Added IL may enlarge branches; promote short branches to prevent byte-offset overflow.
 var branches=new Dictionary<OpCode,OpCode>{{OpCodes.Br_S,OpCodes.Br},{OpCodes.Brtrue_S,OpCodes.Brtrue},{OpCodes.Brfalse_S,OpCodes.Brfalse},{OpCodes.Ble_S,OpCodes.Ble},{OpCodes.Bge_S,OpCodes.Bge},{OpCodes.Blt_S,OpCodes.Blt},{OpCodes.Bgt_S,OpCodes.Bgt},{OpCodes.Beq_S,OpCodes.Beq},{OpCodes.Bne_Un_S,OpCodes.Bne_Un},{OpCodes.Leave_S,OpCodes.Leave}};
 foreach(var m in new[]{ctor,voice,exists,load,setup})foreach(var ins in m.Body.Instructions)if(branches.TryGetValue(ins.OpCode,out var longOp))ins.OpCode=longOp;
 var dest=Path.GetFullPath(args[2]);Directory.CreateDirectory(Path.GetDirectoryName(dest));asm.Write(dest);
 using var check=AssemblyDefinition.ReadAssembly(dest);var checkedLoc=check.MainModule.GetType("Localization");
 foreach(var name in new[]{".cctor","GetSupportedVoiceOverLanguage","FileExists","LoadJSon","Setup"})VerifyStack(checkedLoc.Methods.Single(m=>m.Name==name));
 foreach(var t in Types(asm.MainModule.Types).Where(t=>t.FullName!="Localization")){
  var after=Types(check.MainModule.Types).Single(x=>x.FullName==t.FullName);
  for(int j=0;j<t.Methods.Count;j++)if(t.Methods[j].HasBody){var b=t.Methods[j];var c=after.Methods[j];if(string.Join("\n",b.Body.Instructions)!=string.Join("\n",c.Body.Instructions))throw new Exception("Unexpected method change "+b.FullName);}
 }
 foreach(var m in checkedLoc.Methods.Where(m=>m.HasBody))foreach(var ins in m.Body.Instructions){if(ins.Operand is Instruction target&&!m.Body.Instructions.Contains(target))throw new Exception("Invalid branch target");}
 Console.WriteLine(variant+": "+dest+" SHA256="+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dest))).ToLowerInvariant());
}
