using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
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

        string sourceText = "await ForCanvas(640, 480);";

        string usings = @"global using QuickGraphics;
global using static QuickGraphics.StaticCanvas;
global using static QuickGraphics.Colors;
";

        _editor = await Editor.Create(sourceText, preSource: usings, references: references, compilationOptions: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        Grid.SetRow(_editor, 1);

        MainGrid.Children.Add(_editor);
    }

    private CanvasView? _canvasView;

    private async void RunButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_editor == null)
        {
            return;
        }

        if (_canvasView != null)
        {
            MainGrid.Children.Remove(_canvasView);
            MainGrid.ColumnDefinitions = new ColumnDefinitions("*");
            _canvasView = null;
            return;
        }

        Assembly assembly = (await _editor.Compile(_editor.SynchronousBreak, _editor.AsynchronousBreak)).Assembly;

        if (assembly != null)
        {
            _canvasView = await CanvasView.RunProgram(() => assembly.EntryPoint.Invoke(null, new object[assembly.EntryPoint.GetParameters().Length]));
            Grid.SetColumn(_canvasView, 2);
            MainGrid.ColumnDefinitions = new ColumnDefinitions("2*,5,*");
            Splitter.IsVisible = true;
            MainGrid.Children.Add(_canvasView);
        }
    }
}
