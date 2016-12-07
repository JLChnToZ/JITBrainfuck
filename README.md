JIT Brainfuck
=============

[![Build status](https://ci.appveyor.com/api/projects/status/18oev07p7tke61k6?svg=true)](https://ci.appveyor.com/project/JLChnToZ/jitbrainfuck)

This is a tiny experiment on creating a JIT compiler for [Brainfuck](https://en.wikipedia.org/wiki/Brainfuck) written in C#. This program can optimize and compile the Brainfuck code into [CIL bytecode](https://en.wikipedia.org/wiki/Common_Intermediate_Language), which can be run by .NET Framework and Mono directly. Although this is a JIT compiler, it still have a traditional interpreter fallback, which in case the JIT compiler is not working.

Nothing more or less, the entire source for JIT Brainfuck is licensed in [MIT license](LICENSE).