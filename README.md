# P/Invoke Source Generator
A source generator which generates C# P/Invoke methods with dumpbin.

## Prerequsities
- dumpbin (included in Visual Studio, and need to be set in PATH)

## Quick start
Assuming you have the following C++ code in `test.cpp`:

```cpp
__declspec(dllexport) void test1() { }
__declspec(dllexport) int test2() { return 1; }
__declspec(dllexport) void* test3() { return nullptr; }
__declspec(dllexport) int* test4() { return nullptr; }
__declspec(dllexport) void test5(void* i) { }
__declspec(dllexport) void test6(int* u) { }
__declspec(dllexport) void test7(long long* x) { }
```

Compile the code with MSVC:

```
cl.exe test.cpp /LD /std:c++latest /O2 /EHsc /FD /Fetest.dll
```

To generate P/Invoke methods, you need to reference the project `PInvoke.SourceGenerator`:

```xml
<ProjectReference Include="PInvoke.SourceGenerator\PInvoke.SourceGenerator.csproj" OutputItemType="Analyzer" />
```

Then you can write below code in C# for code generation:

```csharp
[DllFileImport("test.dll")]
partial class TestLibrary { }
```

To use generated code, for example, calling to `test1`:

```csharp
TestLibrary.Test1();
```