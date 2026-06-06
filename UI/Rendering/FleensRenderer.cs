using System.Drawing;
using System.Text;
using ZoombiniHelper.Drag;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Helper for "Fleens!" (fleens.mhk). Reads the current zoombinis + fleens
/// out of the engine object list, brute-forces the round's permutation,
/// and explains it in plain language so the player can deduce which
/// zoombini matches which fleen.
/// </summary>
public sealed class FleensRenderer : IPuzzleRenderer
{
    public PuzzleId Handles => PuzzleId.Fleens;

    /// <summary>Attribute slot index (0..3) → localized attribute name.</summary>
    private static string AttrName(int i) => ZoombiniVariants.AttributeName((byte)(i + 1));

    public void Render(IPuzzleDetector detector, PuzzleDetection detection,
                       IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        var state = FleensState.Read(mem);
        var held  = HeldZoombini.Find(mem);
        labels.TitleColor = Color.FromArgb(180, 220, 180);
        labels.Title = Loc.T("fleens.title", state.Zoombinis.Count, state.Fleens.Count);
        labels.Body  = BuildBody(state, held);
    }

    private static string BuildBody(FleensState s, PoolMember? held)
    {
        var sb = new StringBuilder();

        // Tree-fleens identified via the special-indices → heap-struct lookup
        // (the disasm at 0x413B82 showed [0x4a2818]+0xb83c+di*0x14 = ZB attrs).
        // Then the per-round permutation maps those attrs to the matching
        // fleens. TreeMarker is set on those fleens.
        var treeFleens = s.Fleens.Where(f => f.TreeMarker == 1).ToList();
        bool markerActive = treeFleens.Count >= 1;

        if (held is { } h && s.Permutations.Count == 1)
        {
            var perm = s.Permutations[0];
            byte[] expected = perm.Apply(h.Hair, h.Eyes, h.Nose, h.Feet);
            var matchedFleen = s.Fleens.FirstOrDefault(f =>
                f.A0 == expected[0] && f.A1 == expected[1] &&
                f.A2 == expected[2] && f.A3 == expected[3]);

            sb.AppendLine(Loc.T("fleens.held.line", h.Hair, h.Eyes, h.Nose, h.Feet));
            if (matchedFleen.Address != 0)
            {
                sb.AppendLine(Loc.T("fleens.held.itsFleen", matchedFleen.A0, matchedFleen.A1, matchedFleen.A2, matchedFleen.A3));
                if (markerActive)
                {
                    if (matchedFleen.TreeMarker == 1)
                        sb.AppendLine(Loc.T("fleens.held.hit"));
                    else
                        sb.AppendLine(Loc.T("fleens.held.noHit"));
                }
                else
                {
                    sb.AppendLine(Loc.T("fleens.held.treeUnknown"));
                }
            }
            else
            {
                sb.AppendLine(Loc.T("fleens.held.expectedNotFound", expected[0], expected[1], expected[2], expected[3]));
            }
            sb.AppendLine();
        }

        if (markerActive)
        {
            sb.AppendLine(Loc.T("fleens.tree.header", treeFleens.Count));
            foreach (var f in treeFleens)
                sb.AppendLine(Loc.T("fleens.tree.line", f.A0, f.A1, f.A2, f.A3));
            sb.AppendLine();
        }


        if (!s.IsActive)
        {
            sb.AppendLine(Loc.T("fleens.waiting"));
            return sb.ToString();
        }
        if (s.Zoombinis.Count != s.Fleens.Count)
        {
            sb.AppendLine(Loc.T("fleens.mismatch", s.Zoombinis.Count, s.Fleens.Count));
            sb.AppendLine(Loc.T("fleens.mismatch.hint"));
            return sb.ToString();
        }
        if (s.Permutations.Count == 0)
        {
            sb.AppendLine(Loc.T("fleens.noPerm.1"));
            sb.AppendLine(Loc.T("fleens.noPerm.2"));
            sb.AppendLine(Loc.T("fleens.noPerm.3"));
            return sb.ToString();
        }

        if (s.Permutations.Count == 1)
        {
            sb.AppendLine(Loc.T("fleens.rule.header"));
            sb.AppendLine();
            ExplainPermutation(sb, s.Permutations[0]);
        }
        else
        {
            sb.AppendLine(Loc.T("fleens.ambiguous.1", s.Permutations.Count));
            sb.AppendLine(Loc.T("fleens.ambiguous.2"));
            sb.AppendLine();
            sb.AppendLine(Loc.T("fleens.ambiguous.safe"));
            ExplainCommon(sb, s.Permutations);
        }
        return sb.ToString();
    }

    /// <summary>"Zoombini-Haare → Fleen-Nase" plus value mapping per slot.</summary>
    private static void ExplainPermutation(StringBuilder sb, FleensPermutation p)
    {
        for (int a = 0; a < 4; a++)
        {
            int targetSlot = p.TypeMap[a];
            sb.AppendLine(Loc.T("fleens.map.line", AttrName(a), AttrName(targetSlot)));
            sb.AppendLine(Loc.T("fleens.map.valueLine", ValueMapLabel(p.ValueMap[targetSlot])));
        }
    }

    /// <summary>Show only those facts that hold across ALL candidate
    /// permutations.</summary>
    private static void ExplainCommon(StringBuilder sb, IReadOnlyList<FleensPermutation> perms)
    {
        for (int a = 0; a < 4; a++)
        {
            var targets = perms.Select(p => (int)p.TypeMap[a]).Distinct().ToList();
            string attr = AttrName(a);
            if (targets.Count == 1)
            {
                int t = targets[0];
                var maps = perms.Select(p => string.Join(",", p.ValueMap[t][1..6])).Distinct().ToList();
                if (maps.Count == 1)
                {
                    sb.AppendLine(Loc.T("fleens.map.line", attr, AttrName(t)));
                    sb.AppendLine(Loc.T("fleens.map.valueLine", ValueMapLabel(perms[0].ValueMap[t])));
                }
                else
                {
                    sb.AppendLine(Loc.T("fleens.map.valueAmbiguous", attr, AttrName(t)));
                }
            }
            else
            {
                string opts = string.Join("/", targets.Select(t => AttrName(t)));
                sb.AppendLine(Loc.T("fleens.map.slotAmbiguous", attr, opts));
            }
        }
    }

    /// <summary>"1→3, 2→1, 3→5, 4→4, 5→2" — the explicit value bijection.</summary>
    private static string ValueMapLabel(byte[] valueMap)
    {
        return string.Join(", ", Enumerable.Range(1, 5).Select(v => $"{v}→{valueMap[v]}"));
    }
}
