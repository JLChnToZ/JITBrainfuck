using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace JITBrainfuck {
    internal struct LabelPair {
        public Label loopLabel, endLabel;
    }

    internal delegate void RunnerDelegate(byte[] memory, ref int pointer, TextReader input, TextWriter output, bool canSeek);

    public partial class Runner {
        private RunnerDelegate compiledMethod;

        public void Compile() {
            if(compiledMethod != null) return;

            DynamicMethod generatedMethod = new DynamicMethod(
                "_Compiled_",
                null, new[] {
                    typeof(byte[]),
                    typeof(int).MakeByRefType(),
                    typeof(TextReader),
                    typeof(TextWriter),
                    typeof(bool)
                },
                typeof(Runner).Module,
                true
            );
            generatedMethod.DefineParameter(1, ParameterAttributes.In, "memory");
            generatedMethod.DefineParameter(2, ParameterAttributes.In | ParameterAttributes.Out, "pointer");
            generatedMethod.DefineParameter(3, ParameterAttributes.In, "input");
            generatedMethod.DefineParameter(4, ParameterAttributes.In, "output");
            generatedMethod.DefineParameter(5, ParameterAttributes.In, "canSeek");
            
            EmitIL(generatedMethod.GetILGenerator());

            compiledMethod = generatedMethod.CreateDelegate(typeof(RunnerDelegate)) as RunnerDelegate;
        }

        internal void EmitIL(ILGenerator il) {
            const BindingFlags privateStaticMethod = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            Type baseType = typeof(Helper);
            MethodInfo readByteMethod = baseType.GetMethod("ReadByte", privateStaticMethod);
            MethodInfo writeByteMethod = baseType.GetMethod("WriteByte", privateStaticMethod);

            Stack<LabelPair> labelPairs = new Stack<LabelPair>();

            il.Emit(OpCodes.Nop);

            foreach(Instruction instruction in instructions) {
                LabelPair labelPair;
                switch(instruction.op) {
                    case Op.Move:
                        // pointer = (pointer + instruction.count) % memoryLength;
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldind_I4);
                        il.Emit(OpCodes.Ldc_I4, instruction.count);
                        il.Emit(OpCodes.Add);
                        il.Emit(OpCodes.Ldc_I4, memoryLength);
                        il.Emit(OpCodes.Rem);
                        il.Emit(OpCodes.Stind_I4);
                        break;
                    case Op.Add:
                        // memory[pointer] = (byte)((memory[pointer] + instruction.count) % 256);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldind_I4);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldind_I4);
                        il.Emit(OpCodes.Ldelem_U1);
                        il.Emit(OpCodes.Ldc_I4, instruction.count);
                        il.Emit(OpCodes.Add);
                        il.Emit(OpCodes.Ldc_I4, ByteWrap);
                        il.Emit(OpCodes.Rem);
                        il.Emit(OpCodes.Stelem_I1);
                        break;
                    case Op.Reset:
                        // memory[pointer] = 0;
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldind_I4);
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
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldind_I4);
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
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldind_I4);
                        il.Emit(OpCodes.Ldarg_2);
                        il.Emit(OpCodes.Ldarg_S, (byte)4);
                        il.Emit(OpCodes.Call, readByteMethod);
                        il.Emit(OpCodes.Stelem_I1);
                        break;
                    case Op.Write:
                        // WriteByte(memory[pointer]);
                        il.Emit(OpCodes.Ldarg_3);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldind_I4);
                        il.Emit(OpCodes.Ldelem_I1);
                        il.Emit(OpCodes.Call, writeByteMethod);
                        break;
                }
            }
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Ret);
        }
    }
}