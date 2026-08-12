namespace MiaoNet.Server;

[TestClass]
public sealed class ReloadableResourceTests
{
    [TestMethod]
    public void ReplaceKeepsLeasedResourceAliveUntilLeaseIsReleased()
    {
        DisposableResource first = new();
        DisposableResource second = new();
        using ReloadableResource<DisposableResource> resources = new(first);
        var lease = resources.Acquire();

        resources.Replace(second);

        Assert.IsFalse(first.IsDisposed);
        Assert.AreSame(first, lease.Value);

        lease.Dispose();

        Assert.IsTrue(first.IsDisposed);
        Assert.IsFalse(second.IsDisposed);
    }

    [TestMethod]
    public void ReplaceImmediatelyDisposesUnleasedResource()
    {
        DisposableResource first = new();
        DisposableResource second = new();
        using ReloadableResource<DisposableResource> resources = new(first);

        resources.Replace(second);

        Assert.IsTrue(first.IsDisposed);
        Assert.IsFalse(second.IsDisposed);
    }

    [TestMethod]
    public void RetiredResourceWaitsForEveryOutstandingLease()
    {
        DisposableResource first = new();
        DisposableResource second = new();
        using ReloadableResource<DisposableResource> resources = new(first);
        var firstLease = resources.Acquire();
        var secondLease = resources.Acquire();

        resources.Replace(second);
        firstLease.Dispose();

        Assert.IsFalse(first.IsDisposed);

        secondLease.Dispose();

        Assert.IsTrue(first.IsDisposed);
        Assert.AreEqual(1, first.DisposeCount);
    }

    [TestMethod]
    public void StoreDisposalWaitsForOutstandingLeaseAndRejectsNewOnes()
    {
        DisposableResource resource = new();
        ReloadableResource<DisposableResource> resources = new(resource);
        var lease = resources.Acquire();

        resources.Dispose();

        Assert.IsFalse(resource.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => resources.Acquire());

        lease.Dispose();
        lease.Dispose();

        Assert.IsTrue(resource.IsDisposed);
        Assert.AreEqual(1, resource.DisposeCount);
    }

    private sealed class DisposableResource : IDisposable
    {
        public int DisposeCount { get; private set; }
        public bool IsDisposed => DisposeCount != 0;

        public void Dispose()
            => DisposeCount++;
    }
}
