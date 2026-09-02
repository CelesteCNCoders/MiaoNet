namespace MiaoNet.Shared;

internal sealed class DynamicTypeIndex<TBase> where TBase : class
{
    private interface ITypeBucket
    {
        bool Accepts(Type runtimeType);
        void Clear();
        void Add(TBase item);
        void Remove(TBase item);
    }

    private sealed class TypeBucket<TItem> : ITypeBucket where TItem : TBase
    {
        internal List<TItem> Items { get; }

        internal TypeBucket(IEnumerable<TBase> source)
        {
            Items = [];
            foreach (TBase item in source)
            {
                if (item is TItem typed)
                    Items.Add(typed);
            }
        }

        public void Clear() => Items.Clear();

        public bool Accepts(Type runtimeType)
            => typeof(TItem).IsAssignableFrom(runtimeType);

        public void Add(TBase item)
        {
            if (item is TItem typed)
                Items.Add(typed);
        }

        public void Remove(TBase item)
        {
            if (item is not TItem typed)
                return;

            for (int i = 0; i < Items.Count; i++)
            {
                if (!ReferenceEquals(Items[i], typed))
                    continue;
                Items.RemoveAt(i);
                return;
            }
        }
    }

    private readonly LinkedList<TBase> items = [];
    private readonly Dictionary<TBase, LinkedListNode<TBase>> membership =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, ITypeBucket> buckets = [];
    private readonly Dictionary<Type, ITypeBucket[]> matchingBucketsByRuntimeType = [];

    internal int Count => items.Count;

    internal void Reset(IEnumerable<TBase> source)
    {
        items.Clear();
        membership.Clear();
        foreach (ITypeBucket bucket in buckets.Values)
            bucket.Clear();
        foreach (TBase item in source)
        {
            if (item is not null)
                Add(item);
        }
    }

    internal bool Add(TBase item)
    {
        if (item is null)
            return false;

        if (membership.ContainsKey(item))
            return false;

        LinkedListNode<TBase> node = items.AddLast(item);
        membership.Add(item, node);
        foreach (ITypeBucket bucket in GetMatchingBuckets(item.GetType()))
            bucket.Add(item);
        return true;
    }

    internal bool Remove(TBase item)
    {
        if (item is null)
            return false;

        if (!membership.Remove(item, out LinkedListNode<TBase>? node))
            return false;

        items.Remove(node);
        foreach (ITypeBucket bucket in GetMatchingBuckets(item.GetType()))
            bucket.Remove(item);
        return true;
    }

    internal IReadOnlyList<TItem> Get<TItem>() where TItem : TBase
    {
        Type type = typeof(TItem);
        if (!buckets.TryGetValue(type, out ITypeBucket? existing))
        {
            existing = new TypeBucket<TItem>(items);
            buckets.Add(type, existing);
            foreach (Type runtimeType in matchingBucketsByRuntimeType.Keys.ToArray())
            {
                if (!existing.Accepts(runtimeType))
                    continue;
                ITypeBucket[] previous = matchingBucketsByRuntimeType[runtimeType];
                ITypeBucket[] current = new ITypeBucket[previous.Length + 1];
                previous.CopyTo(current, 0);
                current[^1] = existing;
                matchingBucketsByRuntimeType[runtimeType] = current;
            }
        }
        return ((TypeBucket<TItem>)existing).Items;
    }

    private ITypeBucket[] GetMatchingBuckets(Type runtimeType)
    {
        if (!matchingBucketsByRuntimeType.TryGetValue(runtimeType, out ITypeBucket[]? matching))
        {
            matching = buckets.Values.Where(bucket => bucket.Accepts(runtimeType)).ToArray();
            matchingBucketsByRuntimeType.Add(runtimeType, matching);
        }
        return matching;
    }
}
