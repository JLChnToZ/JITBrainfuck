using System;
using System.IO;
using System.Text;

namespace JITBrainfuck {
    class Program {
        static void Main(string[] args) {
            Console.InputEncoding = Encoding.ASCII;
            foreach(string arg in args) {
                string content;
                try {
                    string path = Path.GetFullPath(arg);
                    FileInfo fileInfo = new FileInfo(path);
                    if(!fileInfo.Exists) continue;
                    using(StreamReader sr = fileInfo.OpenText())
                        content = sr.ReadToEnd();
                } catch(ArgumentException) {
                    content = arg;
                }
                try {
                    Runner.Run(
                        content,
                        Console.In,
                        Console.Out,
                        Runner.DefaultMemoryLength,
                        Runner.DefaultStackDepth,
                        ParseMode.Compile
                    );
                } catch(Exception ex) {
                    Console.OutputEncoding = Encoding.Default;
                    Console.WriteLine("Error: {0}\n{1}", ex.Message, ex.StackTrace);
                    Console.OutputEncoding = Encoding.ASCII;
                }
            }
            Console.ReadKey(true);
        }
    }
}
