using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.MiaoNet;

/// <summary>Completes a command by its name or any alias.</summary>
/// <remarks>
/// A command contributes at most one candidate: its best-matching target decides the inserted
/// spelling, while the display lists the name plus every alias that matched.
/// </remarks>
public static class CommandNameCompletion
{
    public static IReadOnlyList<Completion> Complete(
        IReadOnlyCollection<MiaoNetCommand> commands, string fragment
    )
    {
        List<(Completion Completion, int Rank)> matches = [];
        foreach (MiaoNetCommand command in commands)
        {
            if (TryMatch(command, fragment, out CommandNameMatch match))
                matches.Add((Build(command, match.Spelling, fragment), match.Rank));
        }

        // OrderBy is stable, so equal ranks keep declaration order.
        return matches
            .OrderBy(m => m.Rank)
            .Select(m => m.Completion)
            .ToList();
    }

    private static bool TryMatch(
        MiaoNetCommand command, string fragment, out CommandNameMatch best)
    {
        best = default;
        bool found = false;

        if (TryMatchTarget(command.Name, fragment, out int quality))
        {
            best = new CommandNameMatch(command.Name, Rank(quality, isAlias: false));
            found = true;
        }

        if (command.Aliases is not null)
        {
            foreach (string alias in command.Aliases)
            {
                if (!TryMatchTarget(alias, fragment, out quality))
                    continue;
                int rank = Rank(quality, isAlias: true);
                if (!found || rank < best.Rank)
                {
                    best = new CommandNameMatch(alias, rank);
                    found = true;
                }
            }
        }

        return found;
    }

    // Prefix beats inner, and the name beats an alias; the scan keeps the first best match, so
    // aliases keep declaration order.
    private static int Rank(int quality, bool isAlias) => (quality * 2) + (isAlias ? 1 : 0);

    private static bool TryMatchTarget(string target, string fragment, out int quality)
    {
        int index = target.IndexOf(fragment, StringComparison.OrdinalIgnoreCase);
        quality = index == 0 ? 0 : 1;
        return index >= 0;
    }

    private static Completion Build(MiaoNetCommand command, string content, string fragment)
        => new(content, Display(command, fragment), fragment.Length);

    private static string Display(MiaoNetCommand command, string fragment)
    {
        // Show the aliases that matched (prefix or inner), so "/w" offers "unwatch (uw, uwt)".
        List<string> matchedAliases = [];
        if (fragment.Length > 0 && command.Aliases is not null)
        {
            foreach (string alias in command.Aliases)
            {
                if (alias.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    matchedAliases.Add(alias);
            }
        }

        return matchedAliases.Count == 0
            ? command.Name
            : $"{command.Name} ({string.Join(", ", matchedAliases)})";
    }

    private readonly record struct CommandNameMatch(string Spelling, int Rank);
}
