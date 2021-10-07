using PInvoke.SourceGenerator;
using System;

Console.WriteLine("test");

[DllFileImport(@"test.dll")]
partial class NativeMethods
{

}
