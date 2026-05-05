using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CSharpEditor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickGraphics.Avalonia.Common;

namespace QuickGraphics.Ide;

public partial class MainWindow : Window
{
    private string? _originalPath;

    private Editor? _editor;

    private CanvasView? _canvasView;
    private LogView? _logView;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _ = LoadEditor();
    }

    private async Task LoadEditor()
    {
        static IEnumerable<CachedMetadataReference> GetReferences(Assembly assembly)
        {
            Dictionary<string, CachedMetadataReference> assemblies = new Dictionary<string, CachedMetadataReference>();
            GetReferences(assembly, assemblies);
            return assemblies.Values;

            static void GetReferences(Assembly assembly, Dictionary<string, CachedMetadataReference> assemblies)
            {
                if (assemblies.ContainsKey(assembly.FullName))
                    return;

                assemblies.Add(assembly.FullName, CachedMetadataReference.CreateFromFile(assembly.Location));

                foreach (AssemblyName name in assembly.GetReferencedAssemblies())
                {
                    if (assemblies.ContainsKey(name.FullName))
                        continue;

                    Assembly a = Assembly.Load(name);
                    GetReferences(a, assemblies);
                }
            }
        }

        Assembly assembly = typeof(StaticCanvas).Assembly;
        CachedMetadataReference[] references = [
            ..GetReferences(assembly)
        ];

        string sourceText = "await ForCanvas(640, 480);\n\n";

        string usings = string.Empty;
        using (Stream? stream = typeof(StaticCanvas).Assembly.GetManifestResourceStream("QuickGraphics.QgImplicitUsings.cs"))
        {
            if (stream != null)
            {
                using StreamReader reader = new StreamReader(stream);
                usings = await reader.ReadToEndAsync();
            }
        }

        _editor = await Editor.Create(sourceText, preSource: usings, references: references, compilationOptions: new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        _editor.SaveRequested += OnSave;

        MainGrid.Children.Add(_editor);
    }

    private async void OnSave(object? sender, SaveEventArgs args)
    {
        if (_originalPath == null)
        {
            TopLevel topLevel = TopLevel.GetTopLevel(this);

            IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                FileTypeChoices = [
                    new FilePickerFileType("C#")
                    {
                        Patterns = [ "*.cs" ],
                        AppleUniformTypeIdentifiers = [ "com.microsoft.csharp-source" ],
                        MimeTypes = [ "text/x-csharp" ]
                    }
                ]
            });

            if (file == null)
            {
                return;
            }

            await using Stream stream = await file.OpenWriteAsync();
            await using StreamWriter writer = new StreamWriter(stream);

            await writer.WriteAsync(args.Text);

            Title.Text = file.Name;
            _originalPath = file.Path.AbsolutePath;
        }
        else
        {
            await File.WriteAllTextAsync(_originalPath, args.Text);
        }
    }

    private void RunButton_Click(object? sender, RoutedEventArgs e) => _ = RestartAsync();
    private void StopButton_Click(object? sender, RoutedEventArgs e) => _ = StopAsync();

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
        RunButton.IsEnabled = false;
        RestartButton.IsEnabled = false;
        StopButton.IsEnabled = false;

        Assembly assembly = (await _editor.Compile(_editor.SynchronousBreak, _editor.AsynchronousBreak)).Assembly;

        if (assembly == null)
        {
            return;
        }

        QgAvaloniaProgram program = new QgAvaloniaProgram(() => assembly.EntryPoint.Invoke(null, new object[assembly.EntryPoint.GetParameters().Length]));

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

    private void SetButtonState(bool isPlaying)
    {
        RunButton.IsVisible = !isPlaying;
        RunButton.IsEnabled = true;

        RestartButton.IsVisible = isPlaying;
        RestartButton.IsEnabled = true;

        StopButton.IsEnabled = isPlaying;
        if (this.TryFindResource(isPlaying ? "Icon.Stop" : "Icon.Stop.Disabled", out object? icon))
        {
            StopButtonImage.Source = (IImage)icon!;
        }
    }
}
