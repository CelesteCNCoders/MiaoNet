using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class DynamicTypeIndexTests
{
    [TestMethod]
    public void ResetBuildsAssignableBucketsInSourceOrder()
    {
        ItemA first = new();
        ItemB second = new();
        DerivedItemA third = new();
        DynamicTypeIndex<Item> index = new();

        index.Reset([first, second, third]);

        CollectionAssert.AreEqual(new Item[] { first, second, third }, index.Get<Item>().ToArray());
        CollectionAssert.AreEqual(new ItemA[] { first, third }, index.Get<ItemA>().ToArray());
        CollectionAssert.AreEqual(new DerivedItemA[] { third }, index.Get<DerivedItemA>().ToArray());
    }

    [TestMethod]
    public void CachedBucketsTrackAddsAndRemoves()
    {
        ItemA first = new();
        ItemB second = new();
        DerivedItemA third = new();
        DynamicTypeIndex<Item> index = new();
        index.Reset([first, second]);
        IReadOnlyList<ItemA> itemAs = index.Get<ItemA>();

        Assert.IsTrue(index.Add(third));
        Assert.IsFalse(index.Add(third));
        Assert.IsTrue(index.Remove(first));
        Assert.IsFalse(index.Remove(first));

        CollectionAssert.AreEqual(new ItemA[] { third }, itemAs.ToArray());
        CollectionAssert.AreEqual(new Item[] { second, third }, index.Get<Item>().ToArray());
        Assert.AreEqual(2, index.Count);
    }

    [TestMethod]
    public void ResetInvalidatesPreviouslyCachedMembership()
    {
        ItemA oldItem = new();
        ItemA newItem = new();
        DynamicTypeIndex<Item> index = new();
        index.Reset([oldItem]);
        IReadOnlyList<ItemA> cached = index.Get<ItemA>();
        Assert.AreSame(oldItem, cached.Single());

        index.Reset([newItem]);

        Assert.AreSame(cached, index.Get<ItemA>());
        Assert.AreSame(newItem, cached.Single());
    }

    [TestMethod]
    public void NullItemsAreIgnoredAtEveryMutationBoundary()
    {
        ItemA item = new();
        DynamicTypeIndex<Item> index = new();

        index.Reset([null!, item, null!]);

        Assert.AreEqual(1, index.Count);
        Assert.AreSame(item, index.Get<Item>().Single());
        Assert.IsFalse(index.Add(null!));
        Assert.IsFalse(index.Remove(null!));
        Assert.AreEqual(1, index.Count);
    }

    private abstract class Item { }
    private class ItemA : Item { }
    private sealed class DerivedItemA : ItemA { }
    private sealed class ItemB : Item { }
}
