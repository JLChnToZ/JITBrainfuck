using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;

namespace JITBrainfuck {
    public enum ParseMode {
        Interpret,
        Compile,
    }

    public enum Op: byte {
        Unknown,
        Move,
        Add,
        Reset,
        LoopStart,
        LoopEnd,
        Read,
        Write,
    }

    public struct Instruction {
        public Op op;
        public int count;
    }

    public partial class Runner {
        private readonly int memoryLength;
        private readonly int stackDepth;
        private readonly List<Instruction> instructions;
        private IList<Instruction> readOnlyInstructions;

        public const int DefaultMemoryLength = 30000;
        public const int DefaultStackDepth = 256;

        private const int ByteWrap = 256;
        private const int MillisecondsCheckInput = 100;

        public int MemoryLength {
            get { return memoryLength; }
        }

        public int StackDepth {
            get { return stackDepth; }
        }

        public IList<Instruction> Instructions {
            get {
                if(readOnlyInstructions == null)
                    readOnlyInstructions = instructions.AsReadOnly();
                return readOnlyInstructions;
            }
        }

        public static void Run(string code, TextReader input, TextWriter output, int memoryLength = DefaultMemoryLength, int stackDepth = DefaultStackDepth, ParseMode parseMode = ParseMode.Interpret) {
            if(string.IsNullOrEmpty(code))
                return;
            if(input == null)
                throw new ArgumentNullException("input");
            if(output == null)
                throw new ArgumentNullException("output");

            Runner instructionSet = new Runner(code, memoryLength, stackDepth);

            switch(parseMode) {
                case ParseMode.Compile:
                    try {
                        instructionSet.Compile();
                    } catch { }
                    break;
            }
            instructionSet.Run(input, output);
        }

        public Runner(string code, int memoryLength = DefaultMemoryLength, int stackDepth = DefaultStackDepth) {
            if(string.IsNullOrEmpty(code))
                throw new ArgumentNullException("code");
            this.instructions = new List<Instruction>(code.Length);
            this.memoryLength = memoryLength;
            this.stackDepth = stackDepth;

            Parse(code);
        }

        private void Parse(string code) {
            Instruction lastInstruction = default(Instruction);
            int cursor = -1, stackCounter = 0;

            foreach(char c in code) {
                Op op;
                int count = 1;
                switch(c) {
                    case '>': op = Op.Move; break;
                    case '<': op = Op.Move; count = memoryLength - 1; break;
                    case '+': op = Op.Add; break;
                    case '-': op = Op.Add; count = byte.MaxValue; break;
                    case ',': op = Op.Read; break;
                    case '.': op = Op.Write; break;
                    case '[':
                        op = Op.LoopStart;
                        stackCounter++;
                        if(stackCounter >= stackDepth)
                            throw new InvalidOperationException("Too many loops.");
                        break;
                    case ']':
                        op = Op.LoopEnd;
                        stackCounter--;
                        if(stackCounter < 0)
                            throw new InvalidOperationException("End loop does not have a matching start loop command.");

                        // If last command set is [-] or [+], replace with Op.Reset
                        if(cursor > 0 &&
                            lastInstruction.op == Op.Add && (lastInstruction.count == byte.MaxValue || lastInstruction.count == 1) &&
                            instructions[cursor - 1].op == Op.LoopStart) {
                            instructions.RemoveAt(cursor--);
                            instructions[cursor] = lastInstruction = new Instruction {
                                op = Op.Reset,
                                count = 1
                            };
                            continue;
                        }
                        break;
                    default: continue;
                }
                if(lastInstruction.op == op && op < Op.LoopStart) {
                    switch(op) {
                        case Op.Move:
                            lastInstruction.count = (lastInstruction.count + count) % memoryLength;
                            break;
                        case Op.Add:
                            lastInstruction.count = (lastInstruction.count + count) % ByteWrap;
                            break;
                    }
                    instructions[cursor] = lastInstruction;
                } else {
                    if(lastInstruction.count == 0 && cursor >= 0)
                        instructions.RemoveAt(cursor--);
                    instructions.Add(lastInstruction = new Instruction {
                        op = op,
                        count = count
                    });
                    cursor++;
                }
            }
            if(stackCounter > 0)
                throw new InvalidOperationException("Start loop does not have a matching end loop command.");
        }

        public void Run(TextReader input, TextWriter output) {
            bool canSeek;
            CheckBeforeRun(input, output, out canSeek);

            byte[] memory = new byte[memoryLength];
            int pointer = 0;

            if(compiledMethod != null) {
                compiledMethod.Invoke(memory, ref pointer, input, output, canSeek);
                return;
            }

            Stack<int> loopPoints = new Stack<int>(stackDepth);

            for(int cursor = 0, skip = 0, length = instructions.Count; cursor < length; cursor++) {
                Instruction instruction = instructions[cursor];
                switch(instruction.op) {
                    case Op.Move:
                        if(skip == 0)
                            pointer = (pointer + instruction.count) % memoryLength;
                        break;
                    case Op.Add:
                        if(skip == 0)
                            memory[pointer] = (byte)((memory[pointer] + instruction.count) % ByteWrap);
                        break;
                    case Op.Reset:
                        if(skip == 0)
                            memory[pointer] = 0;
                        break;
                    case Op.LoopStart:
                        loopPoints.Push(cursor);
                        if(skip == 0 && memory[pointer] == 0)
                            skip = loopPoints.Count;
                        break;
                    case Op.LoopEnd:
                        if(skip == 0 && memory[pointer] != 0) {
                            cursor = loopPoints.Peek();
                            break;
                        }
                        if(skip == loopPoints.Count)
                            skip = 0;
                        loopPoints.Pop();
                        break;
                    case Op.Read:
                        if(skip == 0)
                            memory[pointer] = ReadByte(input, canSeek);
                        break;
                    case Op.Write:
                        if(skip == 0)
                            WriteByte(output, memory[pointer]);
                        break;
                }
            }
        }

        private static void CheckBeforeRun(TextReader input, TextWriter output, out bool canSeek) {
            if(input == null)
                throw new ArgumentNullException("input");
            if(output == null)
                throw new ArgumentNullException("output");

            canSeek = true;
            StreamReader sr = input as StreamReader;
            if(sr != null && !sr.BaseStream.CanSeek)
                canSeek = false;
        }

        private static byte ReadByte(TextReader input, bool canSeek) {
            if(canSeek)
                while(input.Peek() < 0)
                    Thread.Sleep(MillisecondsCheckInput); // Sleep and wait for input
            int data = input.Read();
            if(data < 0)
                throw new EndOfStreamException("Input stream is not enough to feed the program.");
            return unchecked((byte)data);
        }

        private static void WriteByte(TextWriter output, byte b) {
            output.Write((char)b);
        }
    }
}