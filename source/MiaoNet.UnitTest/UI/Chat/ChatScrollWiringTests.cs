using System;
using System.Collections.Generic;
using System.IO;

namespace MiaoNet.UnitTest;

// the content height, the clamp and the rendered window all have to describe the same list: the
// displayed one, not the full log. if the host syncs the count from somewhere else they drift
// apart — switch to a tab with fewer messages and the old content height sticks around, so the
// scroll range is too big and the bottom-anchored offset is wrong. no behavioural test catches
// that (the class can't be built without the game), so check the source: SetMessages pushes the
// count, this just makes sure the host doesn't take it back.
[TestClass]
public sealed class ChatScrollWiringTests
{
    // the host of the chat UI, which shouldn't sync the message count itself
    private const string HostFile = "source/MiaoNet.Client/Components/UIComponent.cs";

    // only the message list node may push the count, it owns the displayed list
    private const string OwnerFile = "source/MiaoNet.Client/UI/Chat/ChatMessageListNode.cs";

    [TestMethod]
    public void TheHostDoesNotSyncTheMessageCount()
    {
        string? root = FindRepoRoot();
        if (root is null)
        {
            Assert.Inconclusive($"repository root not found above {AppContext.BaseDirectory}");
            return;
        }

        string host = Path.Combine(root, HostFile);
        Assert.IsTrue(File.Exists(host), $"not found: {host}");

        var offenders = new List<string>();
        string[] lines = File.ReadAllLines(host);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimStart();
            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains("MessageCount", StringComparison.Ordinal)
                && line.Contains('=', StringComparison.Ordinal))
            {
                offenders.Add($"{HostFile}:{i + 1}: {line}");
            }
        }

        Assert.HasCount(
            0,
            offenders,
            "the chat message count must be pushed by ChatMessageListNode.SetMessages from the "
            + "displayed list; syncing it in the host lets the clamp and the rendered list disagree:\n"
            + string.Join("\n", offenders));
    }

    [TestMethod]
    public void TheMessageListNodePushesTheMessageCount()
    {
        string? root = FindRepoRoot();
        if (root is null)
        {
            Assert.Inconclusive($"repository root not found above {AppContext.BaseDirectory}");
            return;
        }

        string owner = Path.Combine(root, OwnerFile);
        Assert.IsTrue(File.Exists(owner), $"not found: {owner}");

        Assert.Contains(
            "controller.MessageCount = MessageCount",
            File.ReadAllText(owner),
            "SetMessages must push the displayed count, or the host has to sync it again");
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
