using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

static class Program
{
    static void Main(string[] args)
    {
        string input = args[0];
        string helperPath = args[1];
        string output = args[2];

        ModuleDefMD target = ModuleDefMD.Load(input);
        ModuleDefMD helper = ModuleDefMD.Load(helperPath);

        TypeDef helperType = helper.Types.First(t => t.FullName == "HealthDisplayPatch.HealthColorCustomization");
        TypeDef injected = InjectType(helperType, target);

        TypeDef mod = target.Types.First(t => t.FullName == "HealthDisplay.HealthDisplayMod");
        MethodDef buildMenu = mod.Methods.First(m => m.Name == "BuildMenu");
        MethodDef onUpdate = mod.Methods.First(m => m.Name == "OnUpdate");

        MethodDef injectedBuild = injected.Methods.First(m => m.Name == "BuildMenu");
        MethodDef injectedApply = injected.Methods.First(m => m.Name == "ApplyColor");

        Instruction finalRet = buildMenu.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
        buildMenu.Body.Instructions.InsertBefore(finalRet, Instruction.Create(OpCodes.Call, injectedBuild));

        Instruction refreshCall = onUpdate.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is IMethod m &&
            m.Name == "RefreshText");
        onUpdate.Body.Instructions.InsertAfter(refreshCall, Instruction.Create(OpCodes.Call, injectedApply));

        target.Write(output);
    }

    static TypeDef InjectType(TypeDef src, ModuleDef target)
    {
        var map = new Dictionary<IDnlibDef, IDnlibDef>();
        var importer = new Importer(target, ImporterOptions.TryToUseTypeDefs)
        {
            Resolver = new MapResolver(map)
        };

        var dst = new TypeDefUser(src.Namespace, src.Name)
        {
            Attributes = src.Attributes,
            BaseType = importer.Import(src.BaseType)
        };
        target.Types.Add(dst);
        map[src] = dst;

        foreach (FieldDef field in src.Fields)
        {
            var nf = new FieldDefUser(field.Name, null, field.Attributes);
            dst.Fields.Add(nf);
            map[field] = nf;
        }

        foreach (MethodDef method in src.Methods)
        {
            var nm = new MethodDefUser(method.Name, null, method.ImplAttributes, method.Attributes);
            dst.Methods.Add(nm);
            map[method] = nm;
        }

        foreach (FieldDef field in src.Fields)
        {
            var nf = (FieldDef)map[field];
            nf.Signature = importer.Import(field.Signature);
        }

        foreach (MethodDef method in src.Methods)
        {
            var nm = (MethodDef)map[method];
            nm.Signature = importer.Import(method.Signature);
            nm.Parameters.UpdateParameterTypes();

            if (!method.HasBody) continue;

            nm.Body = new CilBody(method.Body.InitLocals)
            {
                MaxStack = method.Body.MaxStack
            };

            var localMap = new Dictionary<Local, Local>();
            foreach (Local local in method.Body.Variables)
            {
                var nl = new Local(importer.Import(local.Type));
                nm.Body.Variables.Add(nl);
                localMap[local] = nl;
            }

            var instructionMap = new Dictionary<Instruction, Instruction>();
            foreach (Instruction instruction in method.Body.Instructions)
            {
                object operand = instruction.Operand;

                if (operand is IType type)
                    operand = importer.Import(type);
                else if (operand is IMethod calledMethod)
                    operand = importer.Import(calledMethod);
                else if (operand is IField fieldRef)
                    operand = importer.Import(fieldRef);
                else if (operand is Local localRef)
                    operand = localMap[localRef];
                else if (operand is Instruction || operand is Instruction[])
                    operand = null;

                var ni = new Instruction(instruction.OpCode, operand);
                nm.Body.Instructions.Add(ni);
                instructionMap[instruction] = ni;
            }

            for (int i = 0; i < method.Body.Instructions.Count; i++)
            {
                Instruction old = method.Body.Instructions[i];
                Instruction ni = nm.Body.Instructions[i];

                if (old.Operand is Instruction targetInstruction)
                    ni.Operand = instructionMap[targetInstruction];
                else if (old.Operand is Instruction[] targets)
                    ni.Operand = targets.Select(t => instructionMap[t]).ToArray();
            }
        }

        return dst;
    }

    sealed class MapResolver : ImportResolver
    {
        readonly Dictionary<IDnlibDef, IDnlibDef> map;
        public MapResolver(Dictionary<IDnlibDef, IDnlibDef> map) => this.map = map;

        public override TypeDef Resolve(TypeDef typeDef) =>
            map.TryGetValue(typeDef, out var value) ? (TypeDef)value : null;

        public override MethodDef Resolve(MethodDef methodDef) =>
            map.TryGetValue(methodDef, out var value) ? (MethodDef)value : null;

        public override FieldDef Resolve(FieldDef fieldDef) =>
            map.TryGetValue(fieldDef, out var value) ? (FieldDef)value : null;
    }
}