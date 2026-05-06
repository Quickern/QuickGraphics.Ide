using System.Reflection;
using System.Text.Json;

namespace QuickGraphics.Ide;

public class CodeFile : IDisposable
{
    private static string ApplicationDataDirectory => field ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Assembly.GetEntryAssembly()?.GetName().Name ?? "QuickGraphics.Ide");

    private static string StateFile => field ??= Path.Combine(ApplicationDataDirectory, "ide_state.json");
    private static string IdCacheFile => field ??= Path.Combine(ApplicationDataDirectory, "id_cache.json");

    private static Mutex _mutex = new Mutex(false, "QuickGraphics.Ide.CodeFile.Cache");

    public event Action<string>? FileNameChanged;

    public string FileName
    {
        get => field ??= "NewProject.cs";
        private set;
    }

    public string Guid { get => field ?? throw new NotImplementedException(); private set; }

    public async Task<string> OpenAsync(string? file = null)
    {
        _mutex.WaitOne();

        try
        {
            State state = await LoadCacheAsync<State>(StateFile);

            if (file != null && File.Exists(file))
            {
                IdCache cache = await LoadCacheAsync<IdCache>(IdCacheFile);

                ReadOnlySpan<string> ids = [];
                string? guid = cache.Ids.SingleOrDefault(x => x.Value == file).Key;
            }

            if (file == null && state.LastFileId != null)
            {
                IdCache cache = await LoadCacheAsync<IdCache>(IdCacheFile);
            }
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        _mutex.Dispose();
    }

    private static string NewGuid() => System.Guid.NewGuid().ToString("N");

    private static async Task<T> LoadCacheAsync<T>(string filePath) where T : new()
    {
        if (!File.Exists(filePath))
        {
            return new T();
        }

        try
        {
            using Stream stream = File.OpenRead(IdCacheFile);
            return await JsonSerializer.DeserializeAsync<T>(stream) ?? new T();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);

            return new T();
        }
    }

    private class State
    {
        public string? LastFileId { get; set; }
        public string? LastUnsavedId { get; set; }
    }

    private record class IdCache(Dictionary<string, string> Ids)
    {
        public IdCache() : this(new Dictionary<string, string>()) { }
    }
}
