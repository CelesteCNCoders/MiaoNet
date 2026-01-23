using Celeste.Mod.MiaoNet;

namespace MiaoNet.UnitTest;

[TestClass]
public class UniqueMatchTests
{
    private class Item
    {
        public string Name { get; set; } = "";

        public List<string> Tags { get; set; } = new();
    }

    [TestMethod]
    public void Test_ExactMatch_ReturnsUnique()
    {
        var items = new List<Item> { new() { Name = "Apple" }, new() { Name = "App" } };
        var result = UniqueMatcher.MatchBy(items, x => x.Name, "App");
        Assert.AreEqual("App", result?.Name);
    }

    [TestMethod]
    public void Test_StartsWith_ReturnsUnique()
    {
        var items = new List<Item> { new() { Name = "Apple" }, new() { Name = "Banana" } };
        var result = UniqueMatcher.MatchBy(items, x => x.Name, "App");
        Assert.AreEqual("Apple", result?.Name);
    }

    [TestMethod]
    public void Test_Contains_ReturnsUnique()
    {
        var items = new List<Item> { new() { Name = "Pineapple" }, new() { Name = "Banana" } };
        var result = UniqueMatcher.MatchBy(items, x => x.Name, "apple");
        Assert.AreEqual("Pineapple", result?.Name);
    }

    [TestMethod]
    public void Test_AmbiguousMatch_TrulyReturnsNull()
    {
        var items = new List<Item>
        {
            new() { Name = "ApplePie" },
            new() { Name = "AppleJuice" }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Name, "apple");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Test_AmbiguousContains_ReturnsNull()
    {
        var items = new List<Item>
        {
            new() { Name = "Pineapple" },
            new() { Name = "GreenApple" }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Name, "apple");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Test_Priority_StartsWith_Suppresses_GeneralContains()
    {
        var items = new List<Item>
        {
            new() { Name = "Apple" },  
            new() { Name = "Pineapple" }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Name, "App");
        Assert.AreEqual("Apple", result?.Name);
    }

    [TestMethod]
    public void Test_CaseInsensitive_Works()
    {
        var items = new List<Item> { new() { Name = "APPLE" } };
        var result = UniqueMatcher.MatchBy(items, x => x.Name, "apple");
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void Test_Enumerable_MultipleTags_ReturnsCorrectItem()
    {
        var items = new List<Item>
        {
            new() { Name = "Item1", Tags = new() { "Red", "Blue" } },
            new() { Name = "Item2", Tags = new() { "Green", "Yellow" } }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Tags, "Red");
        Assert.AreEqual("Item1", result?.Name);
    }

    [TestMethod]
    public void Test_Enumerable_CrossItemAmbiguity_ReturnsNull()
    {
        var items = new List<Item>
        {
            new() { Name = "Item1", Tags = new() { "FireRed" } },
            new() { Name = "Item2", Tags = new() { "BloodRed" } }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Tags, "Red");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Test_Enumerable_InternalRedundancy_DoesNotBreakUniqueness()
    {
        var items = new List<Item>
        {
            new() { Name = "Item1", Tags = new() { "Apple", "ApplePie" } },
            new() { Name = "Item2", Tags = new() { "Banana" } }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Tags, "Apple");
        Assert.AreEqual("Item1", result?.Name);
    }

    [TestMethod]
    public void Test_Priority_EqualsWinsOverMultipleContains()
    {
        var items = new List<Item>
        {
            new() { Name = "User" },
            new() { Name = "UserGroup" },
            new() { Name = "AdminUser" }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Name, "User");

        Assert.AreEqual("User", result?.Name);
    }

    [TestMethod]
    public void Test_Priority_StartsWithWinsOverMultipleContains()
    {
        var items = new List<Item>
        {
            new() { Name = "Logger" },
            new() { Name = "Unlog" }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Name, "Log");

        Assert.AreEqual("Logger", result?.Name);
    }

    [TestMethod]
    public void Test_Enumerable_OverlappingTags_AcrossItems()
    {
        var items = new List<Item>
        {
            new() { Name = "Item1", Tags = new() { "Dev", "Development" } },
            new() { Name = "Item2", Tags = new() { "Dev" } }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Tags, "Dev");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Test_Enumerable_OneItemHasHigherPriorityThanAnother()
    {
        var items = new List<Item>
        {
            new() { Name = "Item1", Tags = new() { "Archive_2023" } },
            new() { Name = "Item2", Tags = new() { "2023_Report" } }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Tags, "2023");

        Assert.AreEqual("Item2", result?.Name);
    }

    [TestMethod]
    public void Test_SubsetMismatch()
    {
        var items = new List<Item>
        {
            new() { Name = "ChatRoom" },
            new() { Name = "WeChat" }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Name, "Chat");

        Assert.AreEqual("ChatRoom", result?.Name);
    }

    [TestMethod]
    public void Test_IdenticalStringsInDifferentItems_ReturnsNull()
    {
        var items = new List<Item>
        {
            new() { Name = "Duplicate" },
            new() { Name = "Duplicate" }
        };

        var result = UniqueMatcher.MatchBy(items, x => x.Name, "Duplicate");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Test_EmptyCollection_ReturnsNull()
    {
        var items = new List<Item>();
        var result = UniqueMatcher.MatchBy(items, x => x.Name, "Any");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Test_NoMatch_ReturnsNull()
    {
        var items = new List<Item> { new() { Name = "A" }, new() { Name = "B" } };
        var result = UniqueMatcher.MatchBy(items, x => x.Name, "C");
        Assert.IsNull(result);
    }
}