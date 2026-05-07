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

        public bool IsLocked
        {
            get
            {
                bool result = mutex.WaitOne(0);
                if (result)
                {
                    mutex.ReleaseMutex();
                }
                return !result;
            }
        }
    }
}

public ref struct MutexLock(Mutex mutex) : IDisposable
{
    public void Dispose() => mutex.ReleaseMutex();
}
