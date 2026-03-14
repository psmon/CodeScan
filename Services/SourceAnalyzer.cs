using System.Text.RegularExpressions;
using CodeScan.Models;

namespace CodeScan.Services;

public static partial class SourceAnalyzer
{
    // 지원 확장자 → 파서 매핑 (추후 js, py 등 확장 가능)
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".cshtml"
    };

    public static bool IsSourceFile(string extension)
        => SupportedExtensions.Contains(extension);

    public static List<MethodEntry> ExtractMethods(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => ExtractCSharpMethods(filePath),
            _ => []
        };
    }

    private static List<MethodEntry> ExtractCSharpMethods(string filePath)
    {
        var methods = new List<MethodEntry>();
        string[] lines;

        try { lines = File.ReadAllLines(filePath); }
        catch { return methods; }

        var currentClass = "Global";
        var classStack = new Stack<string>();
        var braceDepth = 0;
        var classBraceDepth = new Stack<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // 빈 줄, 주석 스킵
            if (line.Length == 0 || line.StartsWith("//"))
                continue;

            // 클래스/구조체/인터페이스/레코드 감지
            var classMatch = ClassPattern().Match(line);
            if (classMatch.Success)
            {
                currentClass = classMatch.Groups[1].Value;
                classStack.Push(currentClass);
                classBraceDepth.Push(braceDepth);
            }

            // 메서드 감지
            var methodMatch = MethodPattern().Match(line);
            if (methodMatch.Success && !IsControlFlow(line))
            {
                var methodName = methodMatch.Groups[1].Value;
                var startLine = i + 1; // 1-based
                var endLine = FindMethodEnd(lines, i);

                methods.Add(new MethodEntry
                {
                    ClassName = currentClass,
                    MethodName = methodName,
                    StartLine = startLine,
                    EndLine = endLine
                });
            }

            // 중괄호 추적
            braceDepth += CountChar(line, '{') - CountChar(line, '}');

            // 클래스 스코프 종료
            if (classBraceDepth.Count > 0 && braceDepth <= classBraceDepth.Peek())
            {
                classBraceDepth.Pop();
                classStack.Pop();
                currentClass = classStack.Count > 0 ? classStack.Peek() : "Global";
            }
        }

        return methods;
    }

    private static int FindMethodEnd(string[] lines, int startIndex)
    {
        var depth = 0;
        var foundOpen = false;

        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var ch in line)
            {
                if (ch == '{') { depth++; foundOpen = true; }
                if (ch == '}') depth--;

                if (foundOpen && depth == 0)
                    return i + 1; // 1-based
            }
        }

        // expression-bodied (=>) 메서드: 세미콜론까지
        for (int i = startIndex; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd().EndsWith(';'))
                return i + 1;
        }

        return startIndex + 1;
    }

    private static bool IsControlFlow(string line)
    {
        // if, else, for, foreach, while, switch, lock, using, catch, finally 등과 구분
        return ControlFlowPattern().IsMatch(line);
    }

    private static int CountChar(string s, char c)
    {
        var count = 0;
        foreach (var ch in s)
            if (ch == c) count++;
        return count;
    }

    // 클래스/구조체/레코드/인터페이스 선언
    [GeneratedRegex(@"(?:class|struct|record|interface)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ClassPattern();

    // 메서드 선언: 접근제어자 + 리턴타입 + 메서드명(
    [GeneratedRegex(@"(?:public|private|protected|internal|static|virtual|override|abstract|async|sealed|partial|\s)+\s+\S+\s+(\w+)\s*(?:<[^>]*>)?\s*\(", RegexOptions.Compiled)]
    private static partial Regex MethodPattern();

    // 제어문 키워드
    [GeneratedRegex(@"^\s*(?:if|else|for|foreach|while|switch|lock|using|catch|finally|do)\s*[\(\{]?", RegexOptions.Compiled)]
    private static partial Regex ControlFlowPattern();
}
