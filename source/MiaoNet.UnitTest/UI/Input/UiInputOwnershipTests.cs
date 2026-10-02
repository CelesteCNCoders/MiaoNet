using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste.Mod.MiaoNet.UI.Input;

namespace MiaoNet.UnitTest;

// the ownership rule: MiaoNetUIInputAdapter.Poll consumes the settings bindings and runs before
// any component updates, so a component that reads a binding directly always sees it already
// consumed and silently stops working. it's a wiring mistake, not a logic error, so check the
// source instead.
[TestClass]
public sealed class UiInputOwnershipTests
{
    // bindings owned by the input adapter; components shouldn't touch their state
    private static readonly string[] OwnedBindings =
    [
        "ChatButton",
        "ChatCommandButton",
        "PlayerListButton",
        "PlayerListScrollUp",
        "PlayerListScrollDown",
    ];

    // members that expose a binding's state or consume it
    private static readonly string[] ForbiddenMembers = ["Pressed", "Check", "ConsumePress"];

    [TestMethod]
    public void Components_DoNotReadBindingsOwnedByTheInputAdapter()
    {
        string? root = FindRepoRoot();
        if (root is null)
        {
            Assert.Inconclusive($"repository root not found above {AppContext.BaseDirectory}");
            return;
        }

        string clientDirectory = Path.Combine(root, "source", "MiaoNet.Client");
        Assert.IsTrue(Directory.Exists(clientDirectory), $"client directory not found: {clientDirectory}");

        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(clientDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (IsOwner(relative))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (string binding in OwnedBindings)
            {
                foreach (string member in ForbiddenMembers)
                {
                    if (text.Contains($"{binding}.{member}", StringComparison.Ordinal))
                    {
                        offenders.Add($"{relative}: {binding}.{member}");
                    }
                }
            }
        }

        Assert.HasCount(
            0,
            offenders,
            "components must read MiaoNetContext.UIInput through their UIInputHandle "
            + "handle instead of the binding; the adapter has already consumed it:\n" + string.Join("\n", offenders));
    }

    // only the adapter may consume these bindings; everything else has to go through the dispatch
    private static bool IsOwner(string relativePath)
        => relativePath.EndsWith("UIInput/MiaoNetUIInputAdapter.cs", StringComparison.Ordinal);

    // every action in the routing table is claimed by some component. UIInputRouter.Seal is the real
    // check and it runs at startup; this mirrors it at build time because the failure — a routed
    // key nobody reacts to — is otherwise only found by pressing that key in game. it scans the
    // client sources because the components can't be constructed without the game, and it scans all
    // of them because a claim sits either in the component or in its panel host under ui/.
    [TestMethod]
    public void Components_ClaimEveryRoutedAction()
    {
        string? root = FindRepoRoot();
        if (root is null)
        {
            Assert.Inconclusive($"repository root not found above {AppContext.BaseDirectory}");
            return;
        }

        string clientDirectory = Path.Combine(root, "source", "MiaoNet.Client");
        Assert.IsTrue(Directory.Exists(clientDirectory), $"not found: {clientDirectory}");

        var claimed = new HashSet<UIInputAction>();
        foreach (string file in Directory.EnumerateFiles(clientDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (UIInputAction action in Enum.GetValues<UIInputAction>())
            {
                if (text.Contains($".On(UIInputAction.{action},", StringComparison.Ordinal)
                    || text.Contains($".IsHeld(UIInputAction.{action})", StringComparison.Ordinal))
                {
                    claimed.Add(action);
                }
            }
        }

        UIInputAction[] unclaimed = [.. UIInputRouter.Routes.Keys
            .Where(action => !claimed.Contains(action))
            .Distinct()];

        Assert.HasCount(
            0,
            unclaimed,
            "these actions are routed but no component claims them, so the key does nothing: "
            + string.Join(", ", unclaimed));
    }

    private static string? FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MiaoNet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
