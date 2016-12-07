using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace JITBrainfuck {
    public static class BF2Assembly {
        public static void CompileToFile(Runner runner, string assemblyName, string moduleName, string fullFileName) {
            AssemblyName asmName = new AssemblyName(assemblyName);
            AssemblyBuilder asmBuild = AppDomain.CurrentDomain.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Save);
            ModuleBuilder modBuild = asmBuild.DefineDynamicModule(moduleName, Path.GetFileName(fullFileName));
            TypeBuilder typeBuild = modBuild.DefineType("Program");

            MethodBuilder runMethod = typeBuild.DefineMethod("Run", MethodAttributes.Static | MethodAttributes.Private);
            runMethod.SetParameters(
                typeof(byte[]),
                typeof(int).MakeByRefType(),
                typeof(TextReader),
                typeof(TextWriter),
                typeof(bool)
            );
            runner.EmitIL(runMethod.GetILGenerator());

            MethodBuilder mainMethod = typeBuild.DefineMethod("Main", MethodAttributes.Static | MethodAttributes.Public);
            mainMethod.SetParameters(
                typeof(string[])
            );
            ILGenerator il = mainMethod.GetILGenerator();
            LocalBuilder pointer = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, pointer);

            il.Emit(OpCodes.Ldc_I4, runner.MemoryLength);
            il.Emit(OpCodes.Newarr, typeof(byte));
            il.Emit(OpCodes.Ldloca, pointer);
            Type consoleType = typeof(Console);
            il.Emit(OpCodes.Call, consoleType.GetProperty("In").GetGetMethod());
            il.Emit(OpCodes.Call, consoleType.GetProperty("Out").GetGetMethod());
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, runMethod);

            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Ret);

            Type type = typeBuild.CreateType();
            asmBuild.SetEntryPoint(type.GetMethod("Main"));

            asmBuild.Save(fullFileName);
        }
    }
}
