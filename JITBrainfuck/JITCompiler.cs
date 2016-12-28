using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace JITBrainfuck {
    internal struct LabelPair {
        public Label loopLabel, endLabel;

        public LabelPair(ILGenerator il) {
            loopLabel = il.DefineLabel();
            endLabel = il.DefineLabel();
        }
    }

    internal struct OpCodeParam {
        public OpCode opCode;
        public TypeCode typeCode;
        public object para;

        public OpCodeParam(OpCode opCode) {
            this.opCode = opCode;
            this.para = null;
            this.typeCode = TypeCode.Empty;
        }

        public OpCodeParam(OpCode opCode, object para) {
            this.opCode = opCode;
            this.para = para;
            typeCode = Convert.GetTypeCode(para);
        }

        public OpCodeParam(OpCode opCode, TypeCode typeCode) {
            this.opCode = opCode;
            this.para = null;
            this.typeCode = typeCode;
        }

        public static implicit operator OpCodeParam(OpCode opCode) {
            return new OpCodeParam(opCode);
        }
    }

    internal struct OpCodeMapping {
        public Op op;
        public OpCodeParam[] inst;

        public OpCodeMapping(Op op, params OpCodeParam[] inst) {
            this.op = op;
            this.inst = inst;
        }

        public void Emit(ILGenerator il, params object[] ps) {
            if(inst == null) return;
            int idx = 0;
            foreach(OpCodeParam ins in inst) {
                if(ins.typeCode == TypeCode.Empty) {
                    il.Emit(ins.opCode);
                    continue;
                }
                object arg = GetParameter(ins, ref idx, ps);
                switch(Convert.GetTypeCode(arg)) {
                    case TypeCode.Byte: il.Emit(ins.opCode, Convert.ToByte(arg)); break;
                    case TypeCode.SByte: il.Emit(ins.opCode, Convert.ToSByte(arg)); break;
                    case TypeCode.Int16: il.Emit(ins.opCode, Convert.ToInt16(arg)); break;
                    case TypeCode.Int32: il.Emit(ins.opCode, Convert.ToInt32(arg)); break;
                    case TypeCode.Int64: il.Emit(ins.opCode, Convert.ToInt64(arg)); break;
                    case TypeCode.Single: il.Emit(ins.opCode, Convert.ToSingle(arg)); break;
                    case TypeCode.Double: il.Emit(ins.opCode, Convert.ToDouble(arg)); break;
                    case TypeCode.String: il.Emit(ins.opCode, Convert.ToString(arg)); break;
                    default:
                        if(arg == null)
                            il.Emit(ins.opCode);
                        else if(arg is LocalBuilder)
                            il.Emit(ins.opCode, arg as LocalBuilder);
                        else if(arg is Type)
                            il.Emit(ins.opCode, arg as Type);
                        else if(arg is FieldInfo)
                            il.Emit(ins.opCode, arg as FieldInfo);
                        else if(arg is MethodInfo)
                            il.Emit(ins.opCode, arg as MethodInfo);
                        else if(arg is ConstructorInfo)
                            il.Emit(ins.opCode, arg as ConstructorInfo);
                        else if(arg is SignatureHelper)
                            il.Emit(ins.opCode, arg as SignatureHelper);
                        else if(arg is Label)
                            il.Emit(ins.opCode, (Label)arg);
                        else if(arg is Label[])
                            il.Emit(ins.opCode, arg as Label[]);
                        break;
                }
            }
        }

        private static object GetParameter(OpCodeParam instruction, ref int index, object[] parameters) {
            if(instruction.para != null)
                return instruction.para;
            if(parameters.Length > index)
                return parameters[index++];
            return null;
        }
    }

    internal static class Compiler {
        private const BindingFlags staticMethod = BindingFlags.Public | BindingFlags.Static;

        private static Type baseType = typeof(Helper);
        private static MethodInfo readByteMethod = baseType.GetMethod("ReadByte", staticMethod);
        private static MethodInfo writeByteMethod = baseType.GetMethod("WriteByte", staticMethod);

        public static Type[] parameterTypes = new[] {
            typeof(byte[]),
            typeof(int).MakeByRefType(),
            typeof(TextReader),
            typeof(TextWriter),
            typeof(bool)
        };

        private static OpCodeMapping[] mappings = new[] {
            new OpCodeMapping(Op.Unknown),

            // pointer = (pointer + instruction.count) % memoryLength;
            new OpCodeMapping(Op.Move,
                OpCodes.Ldarg_1,
                OpCodes.Ldarg_1,
                OpCodes.Ldind_I4,
                new OpCodeParam(OpCodes.Ldc_I4, TypeCode.Int32),
                OpCodes.Add,
                new OpCodeParam(OpCodes.Ldc_I4, TypeCode.Int32),
                OpCodes.Rem,
                OpCodes.Stind_I4
            ),

            // memory[pointer] = (byte)((memory[pointer] + instruction.count) % 256);
            new OpCodeMapping(Op.Add,
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Ldind_I4,
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Ldind_I4,
                OpCodes.Ldelem_U1,
                new OpCodeParam(OpCodes.Ldc_I4, TypeCode.Int32),
                OpCodes.Add,
                new OpCodeParam(OpCodes.Ldc_I4, TypeCode.Int32),
                OpCodes.Rem,
                OpCodes.Stelem_I1
            ),

            // memory[pointer] = 0;
            new OpCodeMapping(Op.Reset,
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Ldind_I4,
                OpCodes.Ldc_I4_0,
                OpCodes.Stelem_I1
            ),

            // while(memory[pointer] != 0) {
            new OpCodeMapping(Op.LoopStart,
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Ldind_I4,
                OpCodes.Ldelem_U1,
                OpCodes.Ldc_I4_0,
                OpCodes.Ceq,
                new OpCodeParam(OpCodes.Brtrue, TypeCode.Object)
            ),

            // }
            new OpCodeMapping(Op.LoopEnd,
                new OpCodeParam(OpCodes.Br, TypeCode.Object)
            ),

            // memory[pointer] = ReadByte(input, canSeek);
            new OpCodeMapping(Op.Read,
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Ldind_I4,
                OpCodes.Ldarg_2,
                new OpCodeParam(OpCodes.Ldarg_S, (byte)4),
                new OpCodeParam(OpCodes.Call, readByteMethod),
                OpCodes.Stelem_I1
            ),

            // WriteByte(memory[pointer]);
            new OpCodeMapping(Op.Write,
                OpCodes.Ldarg_3,
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Ldind_I4,
                OpCodes.Ldelem_I1,
                new OpCodeParam(OpCodes.Call, writeByteMethod)
            )
        };

        public static void EmitIL(Runner runner, ILGenerator il) {
            Stack<LabelPair> labelPairs = new Stack<LabelPair>(runner.StackDepth);
            int memoryLength = runner.MemoryLength;
            il.Emit(OpCodes.Nop);
            foreach(Instruction inst in runner.Instructions) {
                LabelPair labelPair;
                OpCodeMapping mapping = mappings[(int)inst.op];
                switch(inst.op) {
                    case Op.Move:
                        mapping.Emit(il, inst.count, memoryLength);
                        break;
                    case Op.Add:
                        mapping.Emit(il, inst.count, 256);
                        break;
                    case Op.LoopStart:
                        labelPairs.Push(labelPair = new LabelPair(il));
                        il.MarkLabel(labelPair.loopLabel);
                        mapping.Emit(il, labelPair.endLabel);
                        break;
                    case Op.LoopEnd:
                        labelPair = labelPairs.Pop();
                        mapping.Emit(il, labelPair.loopLabel);
                        il.MarkLabel(labelPair.endLabel);
                        break;
                    case Op.Reset:
                    case Op.Read:
                    case Op.Write:
                        mapping.Emit(il);
                        break;
                }
            }
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Ret);
        }
    }

    internal delegate void RunnerDelegate(byte[] memory, ref int pointer, TextReader input, TextWriter output, bool canSeek);

    public partial class Runner {
        private RunnerDelegate compiledMethod;

        public void Compile() {
            if(compiledMethod != null) return;

            DynamicMethod generatedMethod = new DynamicMethod(
                "_Compiled_",
                null, Compiler.parameterTypes,
                typeof(Runner).Module,
                true
            );
            generatedMethod.DefineParameter(1, ParameterAttributes.In, "memory");
            generatedMethod.DefineParameter(2, ParameterAttributes.In | ParameterAttributes.Out, "pointer");
            generatedMethod.DefineParameter(3, ParameterAttributes.In, "input");
            generatedMethod.DefineParameter(4, ParameterAttributes.In, "output");
            generatedMethod.DefineParameter(5, ParameterAttributes.In, "canSeek");
            
            Compiler.EmitIL(this, generatedMethod.GetILGenerator());
            compiledMethod = generatedMethod.CreateDelegate(typeof(RunnerDelegate)) as RunnerDelegate;
        }
    }
}