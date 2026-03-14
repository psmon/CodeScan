using System.Text;
using CodeScan.Models;

namespace CodeScan.Services;

public static class TreeFormatter
{
    public static string Format(string rootPath, List<FileEntry> entries, bool showStats)
    {
        var sb = new StringBuilder();
        sb.AppendLine(rootPath);

        if (entries.Count == 0)
        {
            sb.AppendLine("  (empty)");
            return sb.ToString();
        }

        // depth별 그룹화를 위해 부모 경로 기준으로 구성
        var tree = BuildTree(entries);
        PrintNode(sb, tree, "", true);

        if (showStats)
        {
            sb.AppendLine();
            AppendStats(sb, entries);
        }

        return sb.ToString();
    }

    public static string FormatFlat(string rootPath, List<FileEntry> entries, bool showStats)
    {
        var sb = new StringBuilder();

        foreach (var e in entries.Where(e => !e.IsDirectory))
        {
            sb.Append(e.RelativePath);
            sb.Append("  (");
            sb.Append(FormatSize(e.Size));
            sb.AppendLine(")");
        }

        if (showStats)
        {
            sb.AppendLine();
            AppendStats(sb, entries);
        }

        return sb.ToString();
    }

    private static TreeNode BuildTree(List<FileEntry> entries)
    {
        var root = new TreeNode(".", isDir: true);

        foreach (var entry in entries)
        {
            var parts = entry.RelativePath.Replace('\\', '/').Split('/');
            var current = root;

            for (int i = 0; i < parts.Length; i++)
            {
                var isLast = i == parts.Length - 1;
                var existing = current.Children.Find(c => c.Name == parts[i]);

                if (existing != null)
                {
                    current = existing;
                }
                else
                {
                    var node = new TreeNode(parts[i], isLast ? entry.IsDirectory : true)
                    {
                        Size = isLast ? entry.Size : 0,
                        Methods = isLast ? entry.Methods : []
                    };
                    current.Children.Add(node);
                    current = node;
                }
            }
        }

        return root;
    }

    private static void PrintNode(StringBuilder sb, TreeNode node, string indent, bool isRoot)
    {
        if (isRoot)
        {
            foreach (var child in node.Children)
            {
                var isLast = child == node.Children[^1];
                PrintChild(sb, child, "", isLast);
            }
            return;
        }
    }

    private static void PrintChild(StringBuilder sb, TreeNode node, string indent, bool isLast)
    {
        var connector = isLast ? "└── " : "├── ";
        var name = node.IsDir ? node.Name + "/" : node.Name;

        sb.Append(indent);
        sb.Append(connector);
        sb.Append(name);

        if (!node.IsDir)
        {
            sb.Append("  (");
            sb.Append(FormatSize(node.Size));
            sb.Append(')');
        }
        sb.AppendLine();

        var childIndent = indent + (isLast ? "    " : "│   ");

        // 메서드가 있으면 파일 하위에 클래스:함수 트리 출력
        var hasChildren = node.Children.Count > 0;
        if (node.Methods.Count > 0)
        {
            var grouped = node.Methods
                .GroupBy(m => m.ClassName)
                .OrderBy(g => g.Key)
                .ToList();

            for (int g = 0; g < grouped.Count; g++)
            {
                var group = grouped[g];
                var isLastGroup = g == grouped.Count - 1 && !hasChildren;
                var groupConnector = isLastGroup ? "└── " : "├── ";

                sb.Append(childIndent);
                sb.Append(groupConnector);
                sb.Append('[');
                sb.Append(group.Key);
                sb.AppendLine("]");

                var groupIndent = childIndent + (isLastGroup ? "    " : "│   ");
                var methodList = group.OrderBy(m => m.StartLine).ToList();

                for (int m = 0; m < methodList.Count; m++)
                {
                    var method = methodList[m];
                    var isLastMethod = m == methodList.Count - 1;
                    var mConnector = isLastMethod ? "└── " : "├── ";

                    sb.Append(groupIndent);
                    sb.Append(mConnector);
                    sb.Append(method.MethodName);
                    sb.Append("()  L");
                    sb.Append(method.StartLine);
                    sb.Append('-');
                    sb.Append(method.EndLine);

                    if (method.LastAuthor != null)
                    {
                        sb.Append("  [");
                        sb.Append(method.LastDate);
                        sb.Append(' ');
                        sb.Append(method.LastAuthor);
                        sb.Append("] ");
                        sb.Append(method.LastCommitSummary);
                    }
                    sb.AppendLine();
                }
            }
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            PrintChild(sb, node.Children[i], childIndent, i == node.Children.Count - 1);
        }
    }

    private static void AppendStats(StringBuilder sb, List<FileEntry> entries)
    {
        var files = entries.Where(e => !e.IsDirectory).ToList();
        var totalSize = files.Sum(f => f.Size);
        var extGroups = files.GroupBy(f => f.Extension)
                             .OrderByDescending(g => g.Count())
                             .Select(g => $"{g.Key}({g.Count()})");

        var dirs = entries.Count(e => e.IsDirectory);

        sb.Append($"Dirs: {dirs} | Files: {files.Count} | Total: {FormatSize(totalSize)}");
        sb.Append($" | Extensions: {string.Join(", ", extGroups)}");
        sb.AppendLine();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    private sealed class TreeNode(string name, bool isDir)
    {
        public string Name { get; } = name;
        public bool IsDir { get; } = isDir;
        public long Size { get; set; }
        public List<TreeNode> Children { get; } = [];
        public List<MethodEntry> Methods { get; set; } = [];
    }
}
