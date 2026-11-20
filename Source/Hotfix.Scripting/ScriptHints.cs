using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hotfix.Scripting;

// 없는 멤버를 쓴 컴파일 오류에, 그 형식에 실제로 있는 비슷한 이름의 멤버를 덧붙인다
internal static class ScriptHints
{

    private static readonly SymbolDisplayFormat Format = new(
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeModifiers,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeDefaultValue,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static string For(Diagnostic error, CSharpCompilation compilation)
    {
        // CS1061: 값에 없는 멤버
        // CS0117은 형식에 없는 멤버
        if (error.Id is not ("CS1061" or "CS0117") || error.Location.SourceTree is not { } tree)
            return "";
        // a?.b의 오류는 이름이 아니라 점까지 포함한 ".b"를 가리킨다
        var name = tree.GetRoot().FindNode(error.Location.SourceSpan, getInnermostNodeForTie: true) switch
        {
            SimpleNameSyntax simple => simple,
            MemberBindingExpressionSyntax binding => binding.Name,
            _ => null,
        };
        var owner = name?.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == name => access.Expression,
            // a?.b에서 b의 주인은 물음표 앞의 식
            MemberBindingExpressionSyntax => name.Ancestors().OfType<ConditionalAccessExpressionSyntax>()
                .FirstOrDefault(conditional => conditional.WhenNotNull.Span.Contains(name.Span))?.Expression,
            _ => null,
        };
        if (name is null || owner is null)
            return "";
        var model = compilation.GetSemanticModel(tree);
        if ((model.GetTypeInfo(owner).Type ?? model.GetSymbolInfo(owner).Symbol as ITypeSymbol) is not { } type)
            return "";

        var wanted = Grams(name.Identifier.ValueText);
        var similar = type.GetAllMembers().Where(member => member.CanBeReferencedByName && !member.IsOverride)
            .GroupBy(member => member.Name)
            .Select(group => (Members: group.ToList(), Score: Similarity(wanted, Grams(group.Key))
                * (group.First().DeclaredAccessibility == Accessibility.Public ? 1 : 0.8)))
            .Where(item => item.Score >= 0.3)
            .OrderByDescending(item => item.Score).Take(8).ToList();
        if (similar.Count == 0)
            return "";
        return $"\n  Similar members of {type.ToDisplayString()}:" + string.Concat(similar.Select(item =>
            $"\n    {item.Members[0].ToDisplayString(Format)}{(item.Members.Count > 1 ? $" (+{item.Members.Count - 1} overloads)" : "")}"));
    }

    private static IEnumerable<ISymbol> GetAllMembers(this ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
                yield return member;
        }
        if (type.TypeKind != TypeKind.Interface)
            yield break;
        foreach (var member in type.AllInterfaces.SelectMany(parent => parent.GetMembers()))
            yield return member;
    }

    // 이름을 글자 세 개씩 묶은 것. 밑줄과 대소문자는 무시한다
    private static HashSet<string> Grams(string name)
    {
        var text = " " + name.Replace("_", "").ToLowerInvariant() + " ";
        var grams = new HashSet<string>();
        for (var i = 0; i + 3 <= text.Length; i++)
            grams.Add(text.Substring(i, 3));
        return grams;
    }

    private static double Similarity(HashSet<string> a, HashSet<string> b) => 2.0 * a.Count(b.Contains) / (a.Count + b.Count);
}
