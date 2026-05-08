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

    private static readonly string s_defaultCode = $"await ForCanvas(640, 480);{Environment.NewLine}{Environment.NewLine}";

    private static string ApplicationDataDirectory => field ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Assembly.GetEntryAssembly()?.GetName().Name ?? "QuickGraphics.Ide");

    private static string StateFile => field ??= Path.Combine(ApplicationDataDirectory, "ide_state.json");
    private static string IdCacheFile => field ??= Path.Combine(ApplicationDataDirectory, "id_cache.json");

    private readonly Mutex _cacheMutex = new Mutex(false, "QuickGraphics.Ide.CodeFile.Cache");
    private Mutex? _fileMutex;

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

    public string Guid
    {
        get => field ?? throw new NotImplementedException();
        private set;
    }

    public string? FilePath
    {
        get;
        private set
        {
            field = value;

            FileName = field != null ? Path.GetFileName(field) : "New.cs";
        }
    }

    public async Task<string> LoadAsync(string? file = null)
    {
        string text = await Task.Run(() => Load(file));
        LockFile();
        return text;
    }

    public async Task<string> CreateNewAsync()
    {
        string text = await Task.Run(() =>
        {
            using MutexLock mutexLock = _cacheMutex.Acquire();

            State state = LoadCache<State>(StateFile);
            return CreateNew(state);
        });

        LockFile();

        return text;
    }

    public async Task<string?> GetFileToOpenAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(_visual);
        Debug.Assert(topLevel != null, "Top level is null. Possibly closed?");

        IReadOnlyList<IStorageFile> result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [ s_csFileType ],
            SuggestedFileType = s_csFileType
        });

        if (result.Count < 1)
        {
            // throw new FileNotFoundException("Can't open file");
            // TODO: Show error?
            Console.WriteLine("Can't open file");
            return null;
        }

        return await Task.Run(() => GetFileToOpen(result[0].Path.AbsolutePath));
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
            FileTypeChoices = [ s_csFileType ],
            SuggestedFileType = s_csFileType,
            SuggestedFileName = "New.cs"
        });

        if (file == null)
        {
            return;
        }

        await Task.Run(() => Save(file.Path.AbsolutePath, text));
    }

    private string Load(string? file = null)
    {
        using MutexLock mutexLock = _cacheMutex.Acquire();

        State state = LoadCache<State>(StateFile);

        if (file != null && TryOpenFile(file, state, out string? text))
        {
            return text;
        }

        if (state.LastFileId == null)
        {
            return CreateNew(state);
        }

        if (IsFileLocked(state.LastFileId))
        {
            return CreateNew(state);
        }

        IdCache cache = LoadCache<IdCache>(IdCacheFile);

        if (cache.Ids.TryGetValue(state.LastFileId, out file))
        {
            if (!File.Exists(file))
            {
                return CreateNew(state);
            }

            text = File.ReadAllText(file);

            Guid = state.LastFileId;
            FilePath = file;

            return text;
        }

        Guid = state.LastFileId;
        FilePath = null;
        return s_defaultCode;
    }

    private string CreateNew(State state)
    {
        state.LastFileId = Guid = NewGuid();
        SaveCache(StateFile, state);

        FilePath = null;

        return s_defaultCode;
    }

    public void Dispose()
    {
        _cacheMutex.Dispose();

        _fileMutex?.ReleaseMutex();
        _fileMutex?.Dispose();
        _fileMutex = null;
    }

    private static Mutex GetFileMutex(string guid) => new Mutex(false, $"QuickGraphics.Ide.CodeFile.{guid}");
    private static bool IsFileLocked(string guid)
    {
        using Mutex mutex = GetFileMutex(guid);
        return mutex.IsLocked;
    }
    private void LockFile()
    {
        _fileMutex?.ReleaseMutex();
        _fileMutex?.Dispose();

        _fileMutex = GetFileMutex(Guid);
        _fileMutex.WaitOne();
    }

    private string? GetFileToOpen(string file)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        using MutexLock mutexLock = _cacheMutex.Acquire();

        IdCache cache = LoadCache<IdCache>(IdCacheFile);

        ReadOnlySpan<string> ids = [..cache.Ids.Where(x => x.Value == file).Select(x => x.Key)];
        if (ids.IsEmpty)
        {
            return file;
        }

        if (ids.Length > 1)
        {
            foreach (string key in ids[1..])
            {
                cache.Ids.Remove(key);
            }

            SaveCache(IdCacheFile, cache);
        }

        string guid = ids[0];
        if (IsFileLocked(guid))
        {
            return null;
        }

        return file;
    }

    private void Save(string file, string text)
    {
        using MutexLock mutexLock = _cacheMutex.Acquire();

        File.WriteAllText(file, text);

        FilePath = file;

        IdCache cache = LoadCache<IdCache>(IdCacheFile);
        cache.Ids[Guid] = file;
        SaveCache(IdCacheFile, cache);

        State state = LoadCache<State>(StateFile);
        state.LastFileId = Guid;
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

            if (IsFileLocked(guid))
            {
                text = null;
                return false;
            }
        }
        else
        {
            guid = NewGuid();

            cache.Ids[guid] = file;
            SaveCache(IdCacheFile, cache);
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

        using Stream stream = File.Open(filePath, FileMode.Create, FileAccess.Write);
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
    }

    private record class IdCache(Dictionary<string, string> Ids)
    {
        public IdCache() : this(new Dictionary<string, string>()) { }
    }
}
