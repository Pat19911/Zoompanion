using System.Drawing;
using System.Text;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;

namespace ZoombiniHelper.UI.Rendering;

/// <summary>
/// Helper for "Pizza-Trolle" (pizza.mhk). Lists the wanted topping positions
/// per active troll. Slot index N maps to "(N+1)-th topping from left" on the
/// on-screen pizza — see <see cref="PizzaToppings"/> for the off-by-one
/// derivation.
/// </summary>
public sealed class PizzaRenderer : IPuzzleRenderer
{
    public PuzzleId Handles => PuzzleId.PizzaPass;

    public void Render(IPuzzleDetector detector, PuzzleDetection detection,
                       IMemoryReader mem, IReadOnlyList<PoolMember> pool, OverlayLabels labels)
    {
        var pizza = PizzaState.Read(mem);
        int active = pizza.ActiveTrolls.Count;
        string trollWord = Loc.T(active == 1 ? "pizza.troll.one" : "pizza.troll.many");
        labels.Title = Loc.T("pizza.title", pizza.Difficulty, active, trollWord);
        labels.TitleColor = Color.FromArgb(230, 180, 120);
        labels.Body = BuildBody(pizza);
    }

    private static string BuildBody(PizzaState pizza)
    {
        var sb = new StringBuilder();
        var active = pizza.ActiveTrolls;
        if (active.Count == 0)
        {
            sb.AppendLine(Loc.T("pizza.waiting"));
            return sb.ToString();
        }

        sb.AppendLine(Loc.T("pizza.wantExactly"));
        sb.AppendLine();
        foreach (var troll in active)
        {
            var toppings = troll.WantedSlots
                .Select(s => PizzaToppings.Label(pizza.Difficulty, s));
            sb.AppendLine(Loc.T("pizza.troll.line", troll.Name));
            foreach (var l in toppings) sb.AppendLine(Loc.T("pizza.topping.bullet", l));
            sb.AppendLine();
        }

        // Nudge: which slots being asked for don't have a display name yet?
        var unnamedHere = PizzaToppings.UnnamedSlots(pizza.Difficulty).ToHashSet();
        var unnamedWanted = active
            .SelectMany(t => t.WantedSlots)
            .Where(unnamedHere.Contains)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        if (unnamedWanted.Count > 0)
        {
            sb.AppendLine(Loc.T("pizza.unnamed.header"));
            sb.AppendLine(Loc.T("pizza.unnamed.line", pizza.Difficulty,
                string.Join(", ", unnamedWanted.Select(s => PizzaToppings.Label(pizza.Difficulty, s)))));
        }
        return sb.ToString();
    }
}
