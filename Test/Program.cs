using PInvoke.SourceGenerator;
using System;

Console.WriteLine(TestLibrary.Add(3, 4));

[DllFileImport(@"test.dll")]
partial class TestLibrary { }
