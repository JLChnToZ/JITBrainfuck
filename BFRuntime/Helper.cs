using System;
using System.IO;
using System.Threading;

namespace JITBrainfuck {
    public static class Helper {
        private const int MillisecondsCheckInput = 100;

        public static byte ReadByte(TextReader input, bool canSeek) {
            if(canSeek)
                while(input.Peek() < 0)
                    Thread.Sleep(MillisecondsCheckInput); // Sleep and wait for input
            int data = input.Read();
            if(data < 0)
                throw new EndOfStreamException("Input stream is not enough to feed the program.");
            return unchecked((byte)data);
        }

        public static void WriteByte(TextWriter output, byte b) {
            output.Write((char)b);
        }
    }
}
