using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace JITBrainfuck {
    internal struct LabelPair {
        public Label loopLabel, endLabel;
    }

    internal delegate void RunnerDelegate(TextReader input, TextWriter output);

    public partial class Runner {
        private RunnerDelegate compiledMethod;

        public void Compile() {
            if(compiledMethod != null) return;

            const BindingFlags privateStaticMethod = BindingFlags.NonPublic | BindingFlags.Static;

            Type baseType = typeof(Runner);
            MethodInfo checkBeforeRunMethod = baseType.GetMethod("CheckBeforeRun", privateStaticMethod);
            MethodInfo readByteMethod = baseType.GetMethod("ReadByte", privateStaticMethod);
            MethodInfo writeByteMethod = baseType.GetMethod("WriteByte", privateStaticMethod);

            DynamicMethod generatedMethod = new DynamicMethod(
                "_Compiled_",
                null, new[] {
                    typeof(TextReader),
                    typeof(TextWriter)
                },
                baseType.Module,
                true
            );
            generatedMethod.DefineParameter(1, ParameterAttributes.In, "input");
            generatedMethod.DefineParameter(2, ParameterAttributes.In, "output");

            ILGenerator il = generatedMethod.GetILGenerator();

            LocalBuilder canSeek = il.DeclareLocal(typeof(bool));
            LocalBuilder memory = il.DeclareLocal(typeof(byte[]));
            LocalBuilder pointer = il.DeclareLocal(typeof(int));

            il.Emit(OpCodes.Nop);

            // canSeek = false;
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, canSeek);

            // CheckBeforeRun(input, output, out canSeek);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, canSeek);
            il.Emit(OpCodes.Call, checkBeforeRunMethod);

            // memory = new byte[memoryLength];
            il.Emit(OpCodes.Ldc_I4, memoryLength);
            il.Emit(OpCodes.Newarr, typeof(byte));
            il.Emit(OpCodes.Stloc, memory);

            // pointer = 0;
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, pointer);

            Stack<LabelPair> labelPairs = new Stack<LabelPair>();

            foreach(Instruction instruction in instructions) {
                LabelPair labelPair;
                switch(instruction.op) {
                    case Op.Move:
                        // pointer = (pointer + instruction.count) % memoryLength;
                        il.Emit(OpCodes.Ldloc, pointer);
                        il.Emit(OpCodes.Ldc_I4, instruction.count);
                        il.Emit(OpCodes.Add);
                        il.Emit(OpCodes.Ldc_I4, memoryLength);
                        il.Emit(OpCodes.Rem);
                        il.Emit(OpCodes.Stloc, pointer);
                        break;
                    case Op.Add:
                        // memory[pointer] = (byte)((memory[pointer] + instruction.count) % 256);
                        il.Emit(OpCodes.Ldloc, memory);
                        il.Emit(OpCodes.Ldloc, pointer);
                        il.Emit(OpCodes.Ldloc, memory);
                        il.Emit(OpCodes.Ldloc, pointer);
                        il.Emit(OpCodes.Ldelem_U1);
                        il.Emit(OpCodes.Ldc_I4, instruction.count);
                        il.Emit(OpCodes.Add);
                        il.Emit(OpCodes.Ldc_I4, 256);
                        il.Emit(OpCodes.Rem);
                        il.Emit(OpCodes.Stelem_I1);
                        break;
                    case Op.Reset:
                        // memory[pointer] = 0;
                        il.Emit(OpCodes.Ldloc, memory);
                        il.Emit(OpCodes.Ldloc, pointer);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Stelem_I1);
                        break;
                    case Op.LoopStart:
                        // while(memory[pointer] != 0) {
                        labelPairs.Push(labelPair = new LabelPair {
                            loopLabel = il.DefineLabel(),
                            endLabel = il.DefineLabel()
                        });
                        il.MarkLabel(labelPair.loopLabel);
                        il.Emit(OpCodes.Ldloc, memory);
                        il.Emit(OpCodes.Ldloc, pointer);
                        il.Emit(OpCodes.Ldelem_U1);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                        il.Emit(OpCodes.Brtrue, labelPair.endLabel);
                        break;
                    case Op.LoopEnd:
                        // }
                        labelPair = labelPairs.Pop();
                        il.Emit(OpCodes.Br, labelPair.loopLabel);
                        il.MarkLabel(labelPair.endLabel);
                        break;
                    case Op.Read:
                        // memory[pointer] = ReadByte(input, canSeek);
                        il.Emit(OpCodes.Ldloc, memory);
                        il.Emit(OpCodes.Ldloc, pointer);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldloc, canSeek);
                        il.Emit(OpCodes.Call, readByteMethod);
                        il.Emit(OpCodes.Stelem_I1);
                        break;
                    case Op.Write:
                        // WriteByte(memory[pointer]);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldloc, memory);
                        il.Emit(OpCodes.Ldloc, pointer);
                        il.Emit(OpCodes.Ldelem_I1);
                        il.Emit(OpCodes.Call, writeByteMethod);
                        break;
                }
            }
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Ret);

            compiledMethod = generatedMethod.CreateDelegate(typeof(RunnerDelegate)) as RunnerDelegate;
        }
    }
}