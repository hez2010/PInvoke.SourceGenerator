using PInvoke.SourceGenerator;
using System;

Console.WriteLine(TestLibrary.Add(3, 4));

[DllFileImport(@"..\NativeLibrary\add.dll")]
partial class TestLibrary { }
