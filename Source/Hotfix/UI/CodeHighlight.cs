using System;
using System.Collections.Generic;
using System.Text;

namespace Hotfix;

// 코드 블록 글자에 색 태그를 입힌다. 뜻은 분석하지 않고 낱말만 본다
internal static class CodeHighlight
{
    private const string Keyword = "#569CD6";
    private const string Str = "#CE9178";
    private const string Comment = "#6A9955";
    private const string Number = "#B5CEA8";
    private const string Type = "#4EC9B0";
    private const string Method = "#DCDCAA";
    private const string Attribute = "#9CDCFE";
    private const string Punct = "#808080";

    private static readonly HashSet<string> Keywords = new()
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        "var", "async", "await", "record", "get", "set", "init", "nameof", "when", "where", "yield", "partial",
        "required", "with", "and", "or", "not",
    };

    public static string Paint(string language, string code)
    {
        switch (language.ToLowerInvariant())
        {
            case "":
                return code.TrimStart().StartsWith("<") ? Xml(code) : CSharp(code);
            case "csharp":
            case "cs":
            case "c#":
                return CSharp(code);
            case "xml":
                return Xml(code);
            default:
                return Markdown.Literal(code);
        }
    }

    private static string CSharp(string s)
    {
        var sb = new StringBuilder(s.Length * 2);
        var i = 0;
        var afterDot = false;
        var afterNew = false;
        while (i < s.Length)
        {
            var c = s[i];
            var start = i;
            var isNew = false;
            if (c == '/' && At(s, i + 1) == '/')
            {
                i = s.IndexOf('\n', i);
                if (i < 0) i = s.Length;
                Put(sb, s, start, i, Comment);
            }
            else if (c == '/' && At(s, i + 1) == '*')
            {
                i = End(s, s.IndexOf("*/", i + 2, StringComparison.Ordinal), 2);
                Put(sb, s, start, i, Comment);
            }
            else if (StringEnd(s, i) is var end && end > 0)
            {
                i = end;
                Put(sb, s, start, i, Str);
            }
            else if (c == '\'')
            {
                i = QuotedEnd(s, i + 1, '\'');
                Put(sb, s, start, i, Str);
            }
            else if (char.IsDigit(c))
            {
                for (i++; i < s.Length && (IsWord(s[i]) || s[i] == '.' && char.IsDigit(At(s, i + 1))); i++) { }
                Put(sb, s, start, i, Number);
            }
            else if (IsWord(c) || c == '@' && IsWord(At(s, i + 1)))
            {
                for (i++; i < s.Length && IsWord(s[i]); i++) { }
                var word = s.Substring(start, i - start);
                isNew = word == "new";
                var color = Keywords.Contains(word) ? Keyword
                    : afterNew ? Type
                    : NextVisible(s, i) == '(' ? Method
                    : !afterDot && char.IsUpper(word[0]) ? Type
                    : null;
                Put(sb, s, start, i, color);
            }
            else
            {
                i++;
                Put(sb, s, start, i, null);
            }
            if (!char.IsWhiteSpace(c))
            {
                afterDot = c == '.';
                afterNew = isNew;
            }
        }
        return sb.ToString();
    }

    private static string Xml(string s)
    {
        var sb = new StringBuilder(s.Length * 2);
        var i = 0;
        var inTag = false;
        while (i < s.Length)
        {
            var c = s[i];
            var start = i;
            if (!inTag && string.CompareOrdinal(s, i, "<!--", 0, 4) == 0)
            {
                i = End(s, s.IndexOf("-->", i + 4, StringComparison.Ordinal), 3);
                Put(sb, s, start, i, Comment);
            }
            else if (!inTag && c == '<')
            {
                i++;
                if (At(s, i) is '/' or '?' or '!') i++;
                Put(sb, s, start, i, Punct);
                start = i;
                for (; i < s.Length && IsXmlName(s[i]); i++) { }
                Put(sb, s, start, i, Keyword);
                inTag = true;
            }
            else if (!inTag)
            {
                i = End(s, s.IndexOf('<', i), 0);
                Put(sb, s, start, i, null);
            }
            else if (c == '>' || (c is '/' or '?') && At(s, i + 1) == '>')
            {
                i += c == '>' ? 1 : 2;
                Put(sb, s, start, i, Punct);
                inTag = false;
            }
            else if (c is '"' or '\'')
            {
                i = End(s, s.IndexOf(c, i + 1), 1);
                Put(sb, s, start, i, Str);
            }
            else if (IsXmlName(c))
            {
                for (; i < s.Length && IsXmlName(s[i]); i++) { }
                Put(sb, s, start, i, Attribute);
            }
            else
            {
                i++;
                Put(sb, s, start, i, null);
            }
        }
        return sb.ToString();
    }

    // 문자열이 i에서 시작하면 끝난 다음 위치, 아니면 -1. 스트리밍 중에 안 닫힌 문자열은 끝까지로 본다
    private static int StringEnd(string s, int i)
    {
        var j = i;
        var verbatim = false;
        for (; At(s, j) is '$' or '@'; j++)
            verbatim |= s[j] == '@';
        if (At(s, j) != '"')
            return -1;
        var quotes = 0;
        for (; At(s, j + quotes) == '"'; quotes++) { }
        if (!verbatim && quotes >= 3)
        {
            var close = s.IndexOf(new string('"', quotes), j + quotes, StringComparison.Ordinal);
            if (close < 0) return s.Length;
            // 닫는 따옴표가 더 길면 맨 끝의 것들이 닫는 쪽이다
            for (close += quotes; At(s, close) == '"'; close++) { }
            return close;
        }
        if (!verbatim)
            return QuotedEnd(s, j + 1, '"');
        for (j++; j < s.Length; j++)
        {
            if (s[j] != '"') continue;
            if (At(s, j + 1) != '"') return j + 1;
            j++;
        }
        return s.Length;
    }

    // 한 줄짜리 문자열과 문자 리터럴
    private static int QuotedEnd(string s, int from, char quote)
    {
        for (var j = from; j < s.Length; j++)
        {
            if (s[j] == '\\') j++;
            else if (s[j] == quote) return j + 1;
            else if (s[j] == '\n') return j;
        }
        return s.Length;
    }

    private static char At(string s, int i) => i < s.Length ? s[i] : '\0';

    private static int End(string s, int found, int length) => found < 0 ? s.Length : found + length;

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsXmlName(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':';

    private static char NextVisible(string s, int i)
    {
        for (; i < s.Length && s[i] == ' '; i++) { }
        return At(s, i);
    }

    // 색 태그가 줄을 넘지 않게 줄마다 따로 감싼다
    private static void Put(StringBuilder sb, string s, int start, int end, string? color)
    {
        while (start < end)
        {
            var newline = s.IndexOf('\n', start, end - start);
            var stop = newline < 0 ? end : newline;
            if (stop > start)
            {
                if (color != null) sb.Append("<color=").Append(color).Append('>');
                for (var k = start; k < stop; k++)
                {
                    if (s[k] == '<') sb.Append("<<b></b>");
                    else sb.Append(s[k]);
                }
                if (color != null) sb.Append("</color>");
            }
            if (newline < 0) break;
            sb.Append('\n');
            start = newline + 1;
        }
    }
}
