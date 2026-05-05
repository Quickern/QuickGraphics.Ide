using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CSharpEditor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickGraphics.Avalonia.Common;

namespace QuickGraphics.Ide;

public partial class MainWindow : Window
{
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
        using (Stream? stream = typeof(StaticCanvas).Assembly.GetManifestResourceStream("QuickGraphics.Globals.cs"))
        {
            if (stream != null)
            {
                using StreamReader reader = new StreamReader(stream);
                usings = await reader.ReadToEndAsync();
            }
        }

        _editor = await Editor.Create(sourceText, preSource: usings, references: references, compilationOptions: new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));

        MainGrid.Children.Add(_editor);
    }

    private async void RunButton_Click(object? sender, RoutedEventArgs e) => _ = RunOrStopAsync();

    private async Task RunOrStopAsync()
    {
        if (_editor == null)
        {
            return;
        }

        if (_canvasView != null || _logView != null)
        {
            await StopAsync();
            return;
        }

        await RunAsync();
    }

    private async Task RunAsync()
    {
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

        MainGrid.ColumnDefinitions = new ColumnDefinitions("2*,0,0");
    }
}
