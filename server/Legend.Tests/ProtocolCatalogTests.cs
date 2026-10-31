using System.Globalization;
using System.Text.RegularExpressions;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Xunit;

namespace Legend.Tests;

/// <summary>
/// E6: lock MsgId ↔ proto message names, and ProtocolErrorCode uniqueness / non-recycle.
/// Reads legend_protocol.proto source (not generated comments).
/// </summary>
public class ProtocolCatalogTests
{
    private static readonly Regex ProtoEnumEntry = new(
        @"^\s*([A-Z][A-Z0-9_]*)\s*=\s*(\d+)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ProtoMessage = new(
        @"^message\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void ProtocolVersion_Stays_1_21_0()
    {
        Assert.Equal("1.21.0", ProtocolConstants.CurrentVersion);
        string versionPath = Path.Combine(FindRepoRoot(), "protocol", "VERSION");
        Assert.Equal("1.21.0", File.ReadAllText(versionPath).Trim());
    }

    [Fact]
    public void MsgId_Values_AreUnique_AndNotRecycled()
    {
        var entries = Enum.GetValues<MsgId>()
            .Select(id => (Name: id.ToString(), Value: (ushort)id))
            .ToList();

        var byValue = entries.GroupBy(e => e.Value).Where(g => g.Count() > 1).ToList();
        Assert.True(byValue.Count == 0,
            "Duplicate MsgId values: " + string.Join(", ", byValue.Select(g =>
                $"{g.Key}=[{string.Join(",", g.Select(x => x.Name))}]")));

        // Frozen snapshot: renaming or reclaiming a numeric id fails this test.
        var frozen = new Dictionary<string, ushort>
        {
            ["HandshakeReq"] = 1001,
            ["HandshakeResp"] = 1002,
            ["MoveReq"] = 2003,
            ["MoveSync"] = 2004,
            ["AttackReq"] = 2101,
            ["AttackResp"] = 2102,
            ["LeaveWorldReq"] = 3971,
            ["LeaveWorldResp"] = 3972,
            ["ErrMoveTooFast_IsNotMsgId"] = 0 // placeholder removed below
        };
        frozen.Remove("ErrMoveTooFast_IsNotMsgId");

        foreach (var (name, value) in frozen)
        {
            Assert.True(Enum.TryParse<MsgId>(name, out var id), $"Missing MsgId.{name}");
            Assert.Equal(value, (ushort)id);
        }

        Assert.True(entries.Count >= 100, $"Unexpectedly few MsgIds: {entries.Count}");
    }

    [Fact]
    public void MsgId_EachHasMatchingProtoMessage()
    {
        string proto = File.ReadAllText(Path.Combine(FindRepoRoot(), "protocol", "legend_protocol.proto"));
        var messages = ProtoMessage.Matches(proto)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (MsgId id in Enum.GetValues<MsgId>())
        {
            string name = id.ToString();
            if (!messages.Contains(name))
                missing.Add(name);
        }

        Assert.True(missing.Count == 0,
            "MsgId without matching proto message: " + string.Join(", ", missing));
    }

    [Fact]
    public void ProtocolErrorCode_ProtoValues_AreUnique_AndNotRecycled()
    {
        string proto = File.ReadAllText(Path.Combine(FindRepoRoot(), "protocol", "legend_protocol.proto"));
        var block = ExtractEnumBlock(proto, "ProtocolErrorCode");
        var parsed = ParseProtoEnum(block);

        var dupes = parsed.GroupBy(e => e.Value).Where(g => g.Count() > 1).ToList();
        Assert.True(dupes.Count == 0,
            "Duplicate ProtocolErrorCode values in proto: " + string.Join(", ", dupes.Select(g =>
                $"{g.Key}=[{string.Join(",", g.Select(x => x.Name))}]")));

        // Generated C# must match proto numbers for the frozen combat / move set.
        Assert.Equal(0, (int)ProtocolErrorCode.ErrNone);
        Assert.Equal(1005, (int)ProtocolErrorCode.ErrMoveBlocked);
        Assert.Equal(1012, (int)ProtocolErrorCode.ErrInvalidSkill);
        Assert.Equal(1013, (int)ProtocolErrorCode.ErrSkillCooldown);
        Assert.Equal(1014, (int)ProtocolErrorCode.ErrOutOfRange);
        Assert.Equal(1015, (int)ProtocolErrorCode.ErrSafeZone);
        Assert.Equal(1016, (int)ProtocolErrorCode.ErrInvalidTarget);
        Assert.Equal(1018, (int)ProtocolErrorCode.ErrDead);
        Assert.Equal(1051, (int)ProtocolErrorCode.ErrBuffDenied);
        Assert.Equal(1073, (int)ProtocolErrorCode.ErrMoveTooFast);
        Assert.Equal(1999, (int)ProtocolErrorCode.ErrInternal);

        Assert.Equal(1005, parsed.Single(e => e.Name == "ERR_MOVE_BLOCKED").Value);
        Assert.Equal(1015, parsed.Single(e => e.Name == "ERR_SAFE_ZONE").Value);
        Assert.Equal(1073, parsed.Single(e => e.Name == "ERR_MOVE_TOO_FAST").Value);
        Assert.Equal(1999, parsed.Single(e => e.Name == "ERR_INTERNAL").Value);

        // Every proto entry must still exist in generated enum (no recycle / drop).
        foreach (var (name, value) in parsed)
        {
            string csharp = ToPascalErrorName(name);
            Assert.True(Enum.TryParse(csharp, out ProtocolErrorCode code),
                $"Proto {name} missing as ProtocolErrorCode.{csharp}");
            Assert.Equal(value, (int)code);
        }
    }

    [Fact]
    public void ProtocolNotes_StillListsDeferredCapabilitiesAsTbd()
    {
        string notes = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "PROTOCOL_NOTES.md"));
        Assert.Contains("仍缺", notes, StringComparison.Ordinal);
        Assert.Contains("1.22.0", notes, StringComparison.Ordinal);
        // Do not invent MsgIds in the deferred section — keep TBD wording elsewhere in site docs.
        Assert.DoesNotContain("LeaveWorldReq = 9999", notes, StringComparison.Ordinal);
    }

    private static string ExtractEnumBlock(string proto, string enumName)
    {
        int start = proto.IndexOf($"enum {enumName}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"enum {enumName} not found in proto");
        int brace = proto.IndexOf('{', start);
        int depth = 0;
        for (int i = brace; i < proto.Length; i++)
        {
            if (proto[i] == '{') depth++;
            else if (proto[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return proto.Substring(brace + 1, i - brace - 1);
            }
        }
        throw new InvalidOperationException($"Unclosed enum {enumName}");
    }

    private static List<(string Name, int Value)> ParseProtoEnum(string block)
    {
        var list = new List<(string, int)>();
        foreach (Match m in ProtoEnumEntry.Matches(block))
        {
            string name = m.Groups[1].Value;
            int value = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            list.Add((name, value));
        }
        Assert.True(list.Count > 0, "No enum entries parsed");
        return list;
    }

    private static string ToPascalErrorName(string protoName)
    {
        // ERR_SAFE_ZONE -> ErrSafeZone
        var parts = protoName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p =>
            char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "server", "LegendPlatform.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found from the test output directory.");
    }
}
