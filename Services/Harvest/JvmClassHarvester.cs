using CodeScan.Models;

namespace CodeScan.Services.Harvest;

/// <summary>One class harvested from JVM bytecode. Names are internal binary
/// form ("helloworld/Person"); <see cref="Simple"/> gives the last segment.</summary>
public sealed record HarvestedClass(string Name, string? SuperName, IReadOnlyList<string> Interfaces)
{
    public static string Simple(string internalName)
    {
        var slash = internalName.LastIndexOf('/');
        return slash >= 0 ? internalName[(slash + 1)..] : internalName;
    }
}

/// <summary>
/// Reads resolved inheritance out of a compiled JVM <c>.class</c> file (Java and
/// Kotlin both emit these) WITHOUT any JVM toolchain — a small, AOT-safe binary
/// parser over the classfile format (magic → constant pool → this/super/
/// interfaces, per JVMS §4). This is the build-artifact harvest path for the JVM
/// ecosystem: the regex analyzers can't resolve a cross-file base type, but the
/// compiler already recorded it exactly in the bytecode. Freshness (class mtime
/// vs source mtime) is the caller's responsibility — see the testsample-build skill.
/// </summary>
public static class JvmClassHarvester
{
    private const uint Magic = 0xCAFEBABE;
    private const string ObjectName = "java/lang/Object";

    public static HarvestedClass? ReadFile(string path)
    {
        try { return Read(File.ReadAllBytes(path)); }
        catch { return null; }
    }

    /// <summary>Parse classfile bytes. Returns null if not a valid classfile.</summary>
    public static HarvestedClass? Read(byte[] b)
    {
        try
        {
            var p = 0;
            if (ReadU32(b, ref p) != Magic) return null;
            p += 4; // minor(2) + major(2)

            var cpCount = ReadU16(b, ref p);
            // Two parallel tables indexed by constant-pool slot.
            var utf8 = new string?[cpCount];
            var classNameIndex = new int[cpCount];

            for (var i = 1; i < cpCount; i++)
            {
                var tag = b[p++];
                switch (tag)
                {
                    case 1: // Utf8
                        var len = ReadU16(b, ref p);
                        utf8[i] = System.Text.Encoding.UTF8.GetString(b, p, len);
                        p += len;
                        break;
                    case 7: // Class → name_index
                        classNameIndex[i] = ReadU16(b, ref p);
                        break;
                    case 5 or 6: // Long / Double occupy two slots
                        p += 8;
                        i++;
                        break;
                    case 15: p += 3; break;                 // MethodHandle
                    case 16 or 8 or 19 or 20: p += 2; break; // MethodType/String/Module/Package
                    case 3 or 4 or 9 or 10 or 11 or 12 or 17 or 18: p += 4; break; // Int/Float/*ref/NameAndType/*Dynamic
                    default: return null;                    // unknown tag → bail
                }
            }

            string? ClassName(int classIdx)
            {
                if (classIdx <= 0 || classIdx >= cpCount) return null;
                var nameIdx = classNameIndex[classIdx];
                return nameIdx > 0 && nameIdx < cpCount ? utf8[nameIdx] : null;
            }

            p += 2; // access_flags
            var thisName = ClassName(ReadU16(b, ref p));
            if (thisName is null) return null;
            var superName = ClassName(ReadU16(b, ref p));

            var ifaceCount = ReadU16(b, ref p);
            var interfaces = new List<string>(ifaceCount);
            for (var k = 0; k < ifaceCount; k++)
            {
                var name = ClassName(ReadU16(b, ref p));
                if (name is not null) interfaces.Add(name);
            }

            return new HarvestedClass(thisName, superName, interfaces);
        }
        catch
        {
            return null; // malformed / truncated
        }
    }

    /// <summary>
    /// Adapt a harvested class into CodeScan dependency edges
    /// (<c>class -[inherits_or_implements]-> type</c>), ready to merge into the
    /// graph. <c>java/lang/Object</c> is skipped as noise.
    /// </summary>
    public static List<SourceDependency> ToDependencies(HarvestedClass harvested)
    {
        var deps = new List<SourceDependency>();
        var from = HarvestedClass.Simple(harvested.Name);

        void Add(string internalTarget)
        {
            deps.Add(new SourceDependency
            {
                FromKind = "class",
                FromName = from,
                EdgeKind = EdgeKinds.InheritsOrImplements,
                ToKind = "type",
                ToName = HarvestedClass.Simple(internalTarget),
                Strategy = "jvm-class",
                Detail = internalTarget.Replace('/', '.'),
                Line = 0
            });
        }

        if (harvested.SuperName is { } s && s != ObjectName) Add(s);
        foreach (var i in harvested.Interfaces) Add(i);
        return deps;
    }

    private static ushort ReadU16(byte[] b, ref int p)
    {
        var v = (ushort)((b[p] << 8) | b[p + 1]);
        p += 2;
        return v;
    }

    private static uint ReadU32(byte[] b, ref int p)
    {
        var v = (uint)((b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]);
        p += 4;
        return v;
    }
}
