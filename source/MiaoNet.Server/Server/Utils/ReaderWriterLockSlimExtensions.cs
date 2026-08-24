namespace MiaoNet.Server;

public static class ReaderWriterLockSlimExtensions
{
    public readonly ref struct ReadLockDisposable : IDisposable
    {
        private readonly ReaderWriterLockSlim readerWriterLockSlim;

        public ReadLockDisposable(ReaderWriterLockSlim readerWriterLockSlim)
        {
            this.readerWriterLockSlim = readerWriterLockSlim;
            readerWriterLockSlim.EnterReadLock();
        }

        public void Dispose()
        {
            readerWriterLockSlim.ExitReadLock();
        }
    }

    public readonly ref struct WriteLockDisposable : IDisposable
    {
        private readonly ReaderWriterLockSlim readerWriterLockSlim;

        public WriteLockDisposable(ReaderWriterLockSlim readerWriterLockSlim)
        {
            this.readerWriterLockSlim = readerWriterLockSlim;
            readerWriterLockSlim.EnterWriteLock();
        }

        public void Dispose()
        {
            readerWriterLockSlim.ExitWriteLock();
        }
    }

    public static ReadLockDisposable AcquireReadLock(this ReaderWriterLockSlim readerWriterLockSlim)
        => new ReadLockDisposable(readerWriterLockSlim);

    public static WriteLockDisposable AcquireWriteLock(this ReaderWriterLockSlim readerWriterLockSlim)
        => new WriteLockDisposable(readerWriterLockSlim);
}
