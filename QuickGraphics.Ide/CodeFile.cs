using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace QuickGraphics.Ide;

public class CodeFile(Visual visual) : IDisposable
{
    private Visual _visual = visual;

    private static readonly FilePickerFileType s_csFileType = new FilePickerFileType("C#")
    {
        Patterns = [ "*.cs" ],
        AppleUniformTypeIdentifiers = [ "com.microsoft.csharp-source" ],
        MimeTypes = [ "text/x-csharp" ]
    };

    private static string ApplicationDataDirectory => field ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Assembly.GetEntryAssembly()?.GetName().Name ?? "QuickGraphics.Ide");

    private static string StateFile => field ??= Path.Combine(ApplicationDataDirectory, "ide_state.json");
    private static string IdCacheFile => field ??= Path.Combine(ApplicationDataDirectory, "id_cache.json");

    private static Mutex _mutex = new Mutex(false, "QuickGraphics.Ide.CodeFile.Cache");

    public event Action<string>? FileNameChanged;

    public string? FileName
    {
        get;
        private set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            Dispatcher.UIThread.Post(name => FileNameChanged?.Invoke((string)name!), field);
        }
    }

    public string Guid { get => field ?? throw new NotImplementedException(); private set; }
    public string? FilePath
    {
        get;
        private set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            FileName = field != null ? Path.GetFileName(field) : "NewProject.cs";
        }
    }

    public Task<string> LoadAsync(string? file = null)
    {
        return Task.Run(() => Load(file));
    }

    public async Task<string> OpenAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(_visual);
        Debug.Assert(topLevel != null, "Top level is null. Possibly closed?");

        IReadOnlyList<IStorageFile> result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [ s_csFileType ]
        });

        if (result.Count < 1)
        {
            throw new FileNotFoundException("Can't open file");
        }

        return await Task.Run(() => Open(result[0].Path.AbsolutePath));
    }

    public async Task SaveAsync(string text)
    {
        if (FilePath != null)
        {
            await File.WriteAllTextAsync(FilePath, text);
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(_visual);
        if (topLevel == null)
        {
            Console.WriteLine("Top level is null. Possibly closed?");
            return;
        }

        using IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            FileTypeChoices = [ s_csFileType ]
        });

        if (file == null)
        {
            return;
        }

        await Task.Run(() => Save(file.Path.AbsolutePath, text));
    }

    public string Load(string? file = null)
    {
        using MutexLock mutexLock = _mutex.Acquire();

        State state = LoadCache<State>(StateFile);

        if (file != null && TryOpenFile(file, state, out string? text))
        {
            return text;
        }

        if (state.LastFileId != null)
        {
            IdCache cache = LoadCache<IdCache>(IdCacheFile);

            if (cache.Ids.TryGetValue(state.LastFileId, out file) && File.Exists(file))
            {
                text = File.ReadAllText(file);

                Guid = state.LastFileId;
                FilePath = file;

                return text;
            }
        }

        if (state.LastUnsavedId != null)
        {
            Guid = state.LastUnsavedId;
        }
        else
        {
            state.LastUnsavedId = Guid = NewGuid();
            SaveCache(StateFile, state);
        }

        FilePath = null;

        return $"await ForCanvas(640, 480);{Environment.NewLine}{Environment.NewLine}";
    }

    public void Dispose()
    {
        _mutex.Dispose();
    }

    private string Open(string file)
    {
        using MutexLock mutexLock = _mutex.Acquire();

        State state = LoadCache<State>(StateFile);
        if (TryOpenFile(file, state, out string? text))
        {
            return text;
        }

        throw new FileNotFoundException("Can't open file!", file);
    }

    private void Save(string file, string text)
    {
        using MutexLock mutexLock = _mutex.Acquire();

        File.WriteAllText(file, text);

        FilePath = file;

        IdCache cache = LoadCache<IdCache>(IdCacheFile);
        cache.Ids[Guid] = file;
        SaveCache(IdCacheFile, cache);

        State state = LoadCache<State>(StateFile);
        state.LastFileId = Guid;
        state.LastUnsavedId = null;
        SaveCache(StateFile, state);
    }

    private bool TryOpenFile(string file, State state, [NotNullWhen(true)] out string? text)
    {
        if (!File.Exists(file))
        {
            text = null;
            return false;
        }

        IdCache cache = LoadCache<IdCache>(IdCacheFile);

        string guid;

        ReadOnlySpan<string> ids = [..cache.Ids.Where(x => x.Value == file).Select(x => x.Key)];
        if (!ids.IsEmpty)
        {
            if (ids.Length > 1)
            {
                foreach (string key in ids[1..])
                {
                    cache.Ids.Remove(key);
                }

                SaveCache(IdCacheFile, cache);
            }

            guid = ids[0];
        }
        else
        {
            guid = NewGuid();
        }

        text = File.ReadAllText(file);

        Guid = guid;
        FilePath = file;

        if (state.LastFileId != Guid)
        {
            state.LastFileId = Guid;
            SaveCache(StateFile, state);
        }

        return true;
    }

    private static string NewGuid() => System.Guid.NewGuid().ToString("N");

    private static void SaveCache<T>(string filePath, T value)
    {
        string dir = Path.GetDirectoryName(filePath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(filePath);
        }

        using Stream stream = File.OpenWrite(filePath);
        JsonSerializer.Serialize<T>(stream, value);
    }

    private static T LoadCache<T>(string filePath) where T : new()
    {
        if (!File.Exists(filePath))
        {
            return new T();
        }

        try
        {
            using Stream stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<T>(stream) ?? new T();
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
