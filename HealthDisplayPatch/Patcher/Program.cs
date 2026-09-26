using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

static class Program
{
    static void Main(string[] args)
    {
        ModuleDefMD target = ModuleDefMD.Load(args[0]);
        ModuleDefMD helper = ModuleDefMD.Load(args[1]);
        string output = args[2];

        TypeDef helperType = helper.Types.First(t => t.FullName == "HealthDisplayPatch.HealthColorCustomization");
        TypeDef injected = InjectType(helperType, target);

        TypeDef mod = target.Types.First(t => t.FullName == "HealthDisplay.HealthDisplayMod");
        MethodDef buildMenu = mod.Methods.First(m => m.Name == "BuildMenu");
        MethodDef onUpdate = mod.Methods.First(m => m.Name == "OnUpdate");
        MethodDef injectedBuild = injected.Methods.First(m => m.Name == "BuildMenu");
        MethodDef injectedApply = injected.Methods.First(m => m.Name == "ApplyColor");

        Instruction finalRet = buildMenu.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
        buildMenu.Body.Instructions.Insert(buildMenu.Body.Instructions.IndexOf(finalRet),
            Instruction.Create(OpCodes.Call, injectedBuild));

        int refreshIndex = onUpdate.Body.Instructions.IndexOf(onUpdate.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "RefreshText"));
        onUpdate.Body.Instructions.Insert(refreshIndex + 1,
            Instruction.Create(OpCodes.Call, injectedApply));

        target.Write(output);
    }

    static TypeDef InjectType(TypeDef src, ModuleDef target)
    {
        var map = new Dictionary<IDnlibDef, IDnlibDef>();
        var importer = new Importer(target, ImporterOptions.TryToUseTypeDefs);

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
            ((FieldDef)map[field]).Signature = importer.Import(field.Signature);

        foreach (MethodDef method in src.Methods)
        {
            var nm = (MethodDef)map[method];
            nm.Signature = importer.Import(method.Signature);
            nm.Parameters.UpdateParameterTypes();

            if (!method.HasBody) continue;

            nm.Body = new CilBody(
                method.Body.InitLocals,
                new List<Instruction>(),
                new List<ExceptionHandler>(),
                new List<Local>())
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
                object operand = ImportOperand(instruction.Operand, map, importer, localMap);
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

    static object ImportOperand(
        object operand,
        Dictionary<IDnlibDef, IDnlibDef> map,
        Importer importer,
        Dictionary<Local, Local> localMap)
    {
        if (operand is IType type)
        {
            if (type is TypeDef td && map.TryGetValue(td, out IDnlibDef mappedType))
                return mappedType;
            return importer.Import(type);
        }

        if (operand is IMethod method)
        {
            if (method is MethodDef md && map.TryGetValue(md, out IDnlibDef mappedMethod))
                return mappedMethod;
            return importer.Import(method);
        }

        if (operand is IField field)
        {
            if (field is FieldDef fd && map.TryGetValue(fd, out IDnlibDef mappedField))
                return mappedField;
            return importer.Import(field);
        }

        if (operand is Local local)
            return localMap[local];

        if (operand is Instruction || operand is Instruction[])
            return null;

        return operand;
    }
}