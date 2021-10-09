using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PInvoke.SourceGenerator;

class DllFileImportReceiver : ISyntaxReceiver
{
    public List<(ClassDeclarationSyntax Class, AttributeSyntax Attr)> SyntaxNodes { get; } = new();
    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is ClassDeclarationSyntax declarationSyntax)
        {
            foreach (var attr in declarationSyntax.AttributeLists.SelectMany(i => i.Attributes))
            {
                SyntaxNodes.Add((declarationSyntax, attr));
            }
        }
    }
}

[Generator]
class SourceGenerator : ISourceGenerator
{
    private static readonly Regex matchRegex = new(@"(\d+).*?\?([_a-zA-Z][_a-zA-Z0-9]*)@@YA([_A-Z]*)(XZ|@Z)", RegexOptions.Compiled);
    private static readonly Dictionary<string, string> typeMaps = new()
    {
        ["X"] = "void",
        ["D"] = "char",
        ["E"] = "byte",
        ["F"] = "short",
        ["G"] = "ushort",
        ["H"] = "int",
        ["I"] = "uint",
        ["M"] = "float",
        ["N"] = "double",
        ["_N"] = "bool",
        ["_J"] = "long",
        ["_K"] = "ulong",
    };
    private readonly DllFileImportReceiver receiver = new();
    public void Execute(GeneratorExecutionContext context)
    {
        var classes = new List<(INamedTypeSymbol ClassSymbol, string DllFileName, string RawFileName)>();
        foreach (var node in receiver.SyntaxNodes)
        {
            var semanticModel = context.Compilation.GetSemanticModel(node.Class.SyntaxTree);
            var attrTypeInfo = semanticModel.GetTypeInfo(node.Attr);
            if (attrTypeInfo.Type is null) continue;
            if (attrTypeInfo.Type.ToString() != typeof(DllFileImportAttribute).FullName) continue;
            var literalNode = node.Attr.DescendantNodes().OfType<LiteralExpressionSyntax>().SingleOrDefault();
            if (literalNode is null) continue;
            var classDeclInfo = semanticModel.GetDeclaredSymbol(node.Class);
            if (classDeclInfo is null) continue;
            var rawFileName = literalNode.ToString();
            var fileName = semanticModel.GetConstantValue(literalNode);
            if (fileName.HasValue && fileName.Value is string str)
            {
                classes.Add((classDeclInfo, str, rawFileName));
            }

        }

        foreach (var (symbol, fileName, rawFileName) in classes)
        {
            var ns = symbol.ContainingNamespace.ToString() ?? "";
            if (ns == "<global namespace>") ns = "";
            var cs = symbol.ToString()[ns.Length..].Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            GenerateClass(in context, ns, cs.ToList(), rawFileName, fileName);
        }
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => receiver);
    }

    private void GenerateClass(in GeneratorExecutionContext context, string ns, List<string> classes, string rawFileName, string dllFileName)
    {
        var source = string.IsNullOrEmpty(ns) ?
@$"using System.Runtime.InteropServices;

{
        string.Join("\n", classes.Select((c, i) =>
        {
            var prefix = string.Concat(Enumerable.Repeat(" ", i * 4));
            return $"{prefix}partial class {c}\n{prefix}{{";
        }))
}
{GenerateMethods(rawFileName, dllFileName, classes.Count + 1)}
{
        string.Join("\n", classes.Select((_, i) =>
        {
            var prefix = string.Concat(Enumerable.Repeat(" ", (classes.Count - i - 1) * 4));
            return $"{prefix}}}";
        }))
}" :
@$"using System.Runtime.InteropServices;

namespace {ns}
{{
{
        string.Join("\n", classes.Select((c, i) =>
        {
            var prefix = string.Concat(Enumerable.Repeat(" ", (i + 1) * 4));
            return $"{prefix}partial class {c}\n{prefix}{{";
        }))
}
{GenerateMethods(rawFileName, dllFileName, classes.Count + 1)}
{
        string.Join("\n", classes.Select((_, i) =>
        {
            var prefix = string.Concat(Enumerable.Repeat(" ", (classes.Count - i) * 4));
            return $"{prefix}}}";
        }))
}
}}";
        context.AddSource(dllFileName.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Last() + ".g.cs", source);
    }

    private string GenerateMethods(string rawFileName, string fileName, int nestingLevel)
    {
        var source = new StringBuilder();
        var prefix = string.Concat(Enumerable.Repeat(" ", nestingLevel * 4));
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = @"dumpbin.exe",
            Arguments = $"/EXPORTS \"{fileName}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false
        });
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        foreach (var i in output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = matchRegex.Match(i);
            if (match.Success)
            {
                if (match.Groups.Count != 5) continue;
                var sb = new StringBuilder();
                sb.Append(@$"{prefix}[DllImport({rawFileName}, EntryPoint = ""#{match.Groups[1].Value}"")] public extern static ");
                var fn = match.Groups[2].Value;
                var sig = match.Groups[3].Value;
                var index = 0;
                var span = sig.AsSpan();

                if (TryGetNextType(ref span, out var type)) sb.Append($"{type} {ToPascalCase(fn)}(");
                else continue;

                while (TryGetNextType(ref span, out type)) sb.Append($", {type} param{++index}");
                sb.Replace("(, ", "(");
                sb.Append(");");
                var line = sb.ToString();
                source.AppendLine(line.Contains("*") ? line.Replace("public extern static", "public unsafe extern static") : line);

            }
        }

        return source.ToString();

    }

    private bool TryGetNextType(ref ReadOnlySpan<char> span, out string? type)
    {
        if (span.Length == 0)
        {
            type = null;
            return false;
        }

        var sb = new StringBuilder();
        var seek = 0;
        var pointer = 0;
        while (span[seek..].StartsWith("PEA".AsSpan()))
        {
            pointer++;
            seek += 3;
        }

        foreach (var i in typeMaps)
        {
            if (span[seek..].StartsWith(i.Key.AsSpan()))
            {
                seek += i.Key.Length;
                sb.Append(i.Value);
                break;
            }
        }

        if (sb.Length == 0) throw new InvalidOperationException("Unrecognized type.");
        sb.Append(string.Concat(Enumerable.Repeat("*", pointer)));
        type = sb.ToString();

        span = span[seek..];

        return true;
    }

    private static readonly Regex invalidCharsRgx = new("[^_a-zA-Z0-9]", RegexOptions.Compiled);
    private static readonly Regex whiteSpace = new(@"(?<=\s)", RegexOptions.Compiled);
    private static readonly Regex startsWithLowerCaseChar = new("^[a-z]", RegexOptions.Compiled);
    private static readonly Regex firstCharFollowedByUpperCasesOnly = new("(?<=[A-Z])[A-Z0-9]+$", RegexOptions.Compiled);
    private static readonly Regex lowerCaseNextToNumber = new("(?<=[0-9])[a-z]", RegexOptions.Compiled);
    private static readonly Regex upperCaseInside = new("(?<=[A-Z])[A-Z]+?((?=[A-Z][a-z])|(?=[0-9]))", RegexOptions.Compiled);

    private string ToPascalCase(string original)
    {
        var pascalCase = invalidCharsRgx.Replace(whiteSpace.Replace(original, "_"), string.Empty)
            .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => startsWithLowerCaseChar.Replace(w, m => m.Value.ToUpper()))
            .Select(w => firstCharFollowedByUpperCasesOnly.Replace(w, m => m.Value.ToLower()))
            .Select(w => lowerCaseNextToNumber.Replace(w, m => m.Value.ToUpper()))
            .Select(w => upperCaseInside.Replace(w, m => m.Value.ToLower()));

        return string.Concat(pascalCase);
    }
}
