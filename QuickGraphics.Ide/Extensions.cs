namespace QuickGraphics.Ide;

public static class Extensions
{
    extension (Mutex mutex)
    {
        public MutexLock Acquire()
        {
            mutex.WaitOne();
            return new MutexLock(mutex);
        }
    }
}

public ref struct MutexLock(Mutex mutex) : IDisposable
{
    public void Dispose() => mutex.ReleaseMutex();
}
