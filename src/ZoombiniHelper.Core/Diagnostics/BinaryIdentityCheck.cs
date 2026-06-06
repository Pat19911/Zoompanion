namespace ZoombiniHelper.Diagnostics;

/// <summary>
/// Verifies the running binary matches our static analysis by spot-checking
/// known string locations. Any mismatch means the installed EXE has a
/// different layout than the analysis copy in <c>v2_bin/ZoombinisLJ.exe</c> —
/// the helper's hardcoded VAs would then point to the wrong data.
/// </summary>
public static class BinaryIdentityCheck
{
    private static readonly (nint va, string expectedHex, string comment)[] Checks =
    {
        (0x00400000, "4D5A90", "PE/DOS header (MZ)"),
        (0x0048B72C, "55707065722062726964676520616363657074733A00", "\"Upper bridge accepts:\""),
        (0x0048B714, "4C6F7765722062726964676520616363657074733A00", "\"Lower bridge accepts:\""),
        (0x0048B708, "6272696467652E6D686B00", "\"bridge.mhk\""),
        (0x0048F2F0, "41726E6F2020202025642025642025", "Pizza format string prefix"),
        (0x0048C298, "686F74656C2E6D686B00", "\"hotel.mhk\""),
    };

    public static string Run(IMemoryReader mem)
    {
        var lines = new List<string> { "", "=== Binary identity check ===", "" };
        int matches = 0, mismatches = 0;
        foreach (var (va, expectedHex, comment) in Checks)
        {
            int n = expectedHex.Length / 2;
            var actual = mem.ReadBytes(va, n);
            if (actual is null)
            {
                lines.Add($"  ✗ 0x{va:X8} READ FAILED — {comment}");
                mismatches++;
                continue;
            }
            string actualHex = Convert.ToHexString(actual);
            if (string.Equals(actualHex, expectedHex, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"  ✓ MATCH    0x{va:X8} — {comment}");
                matches++;
            }
            else
            {
                lines.Add($"  ✗ DIFFERS  0x{va:X8} — {comment}");
                lines.Add($"               expected: {expectedHex}");
                lines.Add($"               actual  : {actualHex}");
                mismatches++;
            }
        }
        lines.Add("");
        lines.Add(mismatches == 0
            ? $"  ✓ All {matches} checks passed — binary identical to analysis copy."
            : $"  ⚠ {mismatches}/{matches + mismatches} checks failed — different layout!");
        return string.Join("\n", lines);
    }
}
