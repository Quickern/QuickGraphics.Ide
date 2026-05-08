using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CSharpEditor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickGraphics.Avalonia.Common;

namespace QuickGraphics.Ide;

public partial class MainWindow : Window
{
    private readonly CodeFile _file;

    private Editor? _editor;

    private CanvasView? _canvasView;
    private LogView? _logView;

    private readonly string? _fileToOpen;

    public MainWindow()
    {
        InitializeComponent();

        _file = new CodeFile(this);
        _file.FileNameChanged += name => Title.Text = name ?? "Loading...";
    }

    public MainWindow(string fileToOpen) : this()
    {
        _fileToOpen = fileToOpen;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _ = ReloadEditor(_fileToOpen);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        _file.Dispose();
    }

    private async Task ReloadEditor(string? filePath = null, bool createNew = false)
    {
        await StopAsync();

        DisableButtons();

        if (_editor != null)
        {
            MainGrid.Children.Remove(_editor);
            _editor = null;
        }

        Assembly assembly = typeof(StaticCanvas).Assembly;
        CachedMetadataReference[] references = [
            ..GetReferences(assembly)
        ];

        string? sourceText = createNew ? await _file.CreateNewAsync() : await _file.LoadAsync(filePath);
        string guid = _file.Guid;

        string usings = string.Empty;
        using (Stream? stream = typeof(StaticCanvas).Assembly.GetManifestResourceStream("QuickGraphics.QgImplicitUsings.cs"))
        {
            if (stream != null)
            {
                using StreamReader reader = new StreamReader(stream);
                usings = await reader.ReadToEndAsync();
            }
        }

        _editor = await Editor.Create(sourceText, guid: guid, preSource: usings, references: references, compilationOptions: new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        _editor.SaveRequested += OnSave;

        MainGrid.Children.Add(_editor);

        SetButtonState(false);

        return;

        static IEnumerable<CachedMetadataReference> GetReferences(Assembly assembly)
        {
            Dictionary<string, CachedMetadataReference> assemblies = new Dictionary<string, CachedMetadataReference>();
            GetReferences(assembly, assemblies);
            return assemblies.Values;

            static void GetReferences(Assembly assembly, Dictionary<string, CachedMetadataReference> assemblies)
            {
                string? fullName = assembly.FullName;
                if (fullName != null && assemblies.ContainsKey(fullName))
                    return;

                if (fullName != null)
                {
                    assemblies.Add(fullName, CachedMetadataReference.CreateFromFile(assembly.Location));
                }

                foreach (AssemblyName name in assembly.GetReferencedAssemblies())
                {
                    if (assemblies.ContainsKey(name.FullName))
                        continue;

                    Assembly a = Assembly.Load(name);
                    GetReferences(a, assemblies);
                }
            }
        }
    }

    private async void OnSave(object? sender, SaveEventArgs args) => _ = _file.SaveAsync(args.Text);

    private void RunButton_Click(object? sender, RoutedEventArgs e) => _ = RestartAsync();
    private void StopButton_Click(object? sender, RoutedEventArgs e) => _ = StopAsync();

    private void MenuNew_Click(object? sender, RoutedEventArgs e) => _ = ReloadEditor(createNew: true);

    private async void MenuOpen_Click(object? sender, RoutedEventArgs e)
    {
        string? fileName = await _file.GetFileToOpenAsync();
        if (fileName == null)
        {
            return;
        }

        await ReloadEditor(fileName);
    }

    private void MenuNewWindow_Click(object? sender, RoutedEventArgs e) => new MainWindow().Show();
    private void MenuExit_Click(object? sender, RoutedEventArgs e) => Close();
    private void MenuPublish_Click(object? sender, RoutedEventArgs e) => throw new NotImplementedException();


    private async Task RestartAsync()
    {
        if (_editor == null)
        {
            return;
        }

        if (_canvasView != null || _logView != null)
        {
            await StopAsync();
        }

        await RunAsync();
    }

    private async Task RunAsync()
    {
        if (_editor == null)
        {
            return;
        }

        DisableButtons();

        Assembly assembly = (await _editor.Compile(_editor.SynchronousBreak, _editor.AsynchronousBreak)).Assembly;
        if (assembly == null)
        {
            // TODO: Show error
            SetButtonState(false);
            return;
        }

        MethodInfo? entry = assembly.EntryPoint;
        if (entry == null)
        {
            // TODO: Show error
            SetButtonState(false);
            return;
        }

        QgAvaloniaProgram program = new QgAvaloniaProgram(() => entry.Invoke(null, new object[entry.GetParameters().Length]));

        await Task.WhenAll(program.RunAsync(), GetCanvasViewAsync(program), GetLogViewAsync(program));

        return;

        void TurnOn()
        {
            MainGrid.ColumnDefinitions = new ColumnDefinitions("2*,5,*");

            RunnerSplitter.IsVisible = true;
            RunnerGrid.IsVisible = true;

            SetButtonState(true);
        }

        async Task GetCanvasViewAsync(QgAvaloniaProgram program)
        {
            _canvasView = await program.GetCanvasViewAsync();

            TurnOn();

            RunnerGrid.Children.Add(_canvasView);
        }

        async Task GetLogViewAsync(QgAvaloniaProgram program)
        {
            _logView = await program.GetLogViewAsync();
            Grid.SetRow(_logView, 2);

            TurnOn();

            RunnerGrid.Children.Add(_logView);
        }
    }

    private async Task StopAsync()
    {
        DisableButtons();

        if (_canvasView != null)
        {
            RunnerGrid.Children.Remove(_canvasView);

            _canvasView = null;
        }

        if (_logView != null)
        {
            RunnerGrid.Children.Remove(_logView);

            _logView = null;
        }

        RunnerSplitter.IsVisible = false;
        RunnerGrid.IsVisible = false;

        SetButtonState(false);

        MainGrid.ColumnDefinitions = new ColumnDefinitions("2*,0,0");
    }

    private void DisableButtons()
    {
        RunButton.IsEnabled = false;
        RestartButton.IsEnabled = false;
        StopButton.IsEnabled = false;

        RunMenu.IsEnabled = false;
        StopMenu.IsEnabled = false;
    }

    private void SetButtonState(bool isPlaying)
    {
        RunButton.IsVisible = !isPlaying;
        RunButton.IsEnabled = true;
        RunMenu.IsEnabled = true;

        RestartButton.IsVisible = isPlaying;
        RestartButton.IsEnabled = true;

        StopButton.IsEnabled = isPlaying;
        if (this.TryFindResource(isPlaying ? "Icon.Stop" : "Icon.Stop.Disabled", out object? icon))
        {
            StopButtonImage.Source = (IImage)icon!;
        }
        StopMenu.IsEnabled = isPlaying;
    }
}
