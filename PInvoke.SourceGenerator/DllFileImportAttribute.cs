using System;

namespace PInvoke.SourceGenerator;

[AttributeUsage(AttributeTargets.Class)]
public class DllFileImportAttribute : Attribute
{
    public DllFileImportAttribute(string fileName)
    {
        FileName = fileName;
    }

    public string FileName { get; }
}