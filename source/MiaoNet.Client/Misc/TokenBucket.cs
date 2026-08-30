namespace Celeste.Mod.MiaoNet;

public sealed class TokenBucket
{
    private readonly float capacity;
    private readonly float refillInterval;

    private float tokens;

    public TokenBucket(float refillInterval, int capacity = 1)
    {
        if (refillInterval <= 0f)
            throw new ArgumentOutOfRangeException(nameof(refillInterval));

        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        this.refillInterval = refillInterval;
        this.capacity = capacity;
        tokens = capacity;
    }

    public void Update(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        tokens = MathF.Min(
            capacity,
            tokens + deltaTime / refillInterval
        );
    }

    public bool TryConsume()
    {
        if (tokens < 1f)
            return false;

        tokens -= 1f;
        return true;
    }
}