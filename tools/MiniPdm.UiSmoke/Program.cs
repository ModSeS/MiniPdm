using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using MiniPdm.Core;
using MiniPdm.Desktop;
using MiniPdm.Infrastructure;

namespace MiniPdm.UiSmoke;

// Проверка MVVM-команд и рендеринг настоящего WPF-экрана без показа окна на рабочем столе.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string samples = Path.GetFullPath(args.Length > 0 ? args[0] : "samples");
        string output = Path.GetFullPath(args.Length > 1 ? args[1] : "docs");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.Source = new Uri("/MiniPdm.Desktop;component/Styles.xaml", UriKind.Relative);
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        int result = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                string cs = "Data Source=ui-smoke;Mode=Memory;Cache=Shared";
                using var keeper = new SqliteConnection(cs);
                keeper.Open();
                var store = new SqlitePdmStore(cs);
                store.Initialize();
                var reader = new JsonCadAdapter();
                var dialogs = new TestDialogs { Folder = Path.Combine(samples, "cad-export") };
                var vm = new MainViewModel(store, new(reader, reader, store), new(store), dialogs);
                var trace = new BindingErrors();
                PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
                var window = new MainWindow { DataContext = vm };
                await Run(vm.ImportCommand, vm);
                Require(vm.ImportRows.Count == 45 && vm.ImportRows.Count(r => r.Outcome == ImportOutcome.Rejected) == 10, "Import report");
                await Render(window, Path.Combine(output, "import-report.png"));
                vm.Search = "редуктор цилиндрический";
                Require(vm.Objects.Count() == 1, "Cyrillic search");
                vm.SelectedCatalogObject = vm.Objects.Single();
                await Idle(vm);
                await Run(vm.CalculateCommand, vm);
                Require(vm.Specification.Count == 25, "Specification");
                Require(vm.MassResult.Contains("98,804") || vm.MassResult.Contains("98.804"), "Initial mass");
                await Render(window, Path.Combine(output, "main-window.png"));

                // Выбор узла через привязку IsSelected должен обновлять карточку и команды.
                vm.Roots[0].Children[0].IsSelected = true;
                Require(vm.SelectedObject?.Id == vm.Roots[0].Children[0].Row.ObjectId, "Tree selection");
                var cover = store.GetObjects().Single(o => o.Designation == "РДЦЛ.304112.500");
                vm.Search = "";
                vm.SelectedCatalogObject = vm.Objects.Single(o => o.Id == cover.Id);
                await Idle(vm);
                await Run(vm.ApproveCommand, vm);
                Require(vm.SelectedObject?.State == VersionState.Approved, "Approve");
                dialogs.Folder = Path.Combine(samples, "cad-export-v2");
                await Run(vm.ImportCommand, vm);
                Require(store.GetObjects().Single(o => o.Id == cover.Id).VersionNumber == 2, "New version from UI import");
                await Run(vm.CancelVersionCommand, vm);
                Require(vm.SelectedObject?.State == VersionState.Approved && vm.SelectedObject.VersionNumber == 1, "Cancel and fallback");
                var oil = vm.Objects.Single(o => o.Designation == "РДЦЛ.304112.900");
                vm.SelectedCatalogObject = oil;
                await Idle(vm);
                await Run(vm.CalculateCommand, vm);
                Require(vm.MassResult.Contains("Прокладка маслоуказателя") && vm.Specification.Any(r => r.UnitMassKg is null), "Missing mass feedback");
                Require(trace.Errors.Count == 0, "Binding errors: " + string.Join("; ", trace.Errors));
                Console.WriteLine("UI smoke passed: import, search, tree, card, calculation, states, v2, missing mass, XAML bindings.");
                window.Close();
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Shutdown(result); }
        };
        app.Run();
        return result;
    }

    private static async Task Run(AsyncCommand command, MainViewModel vm)
    {
        Require(command.CanExecute(null), "Command unavailable");
        command.Execute(null);
        await Idle(vm);
        Require(!vm.Status.StartsWith("Ошибка:"), vm.Status);
    }
    private static async Task Idle(MainViewModel vm)
    {
        var timer = Stopwatch.StartNew();
        while (vm.IsBusy)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("UI operation timeout");
            await Task.Delay(10);
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task Render(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1320, 800));
        content.Arrange(new Rect(0, 0, 1320, 800));
        content.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        content.Measure(new Size(1320, 800));
        content.Arrange(new Rect(0, 0, 1320, 800));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1320, 800, 96, 96, PixelFormats.Pbgra32);
        // У Content нет фона внешних полей Window: дорисовываем тот же фон перед рендерингом.
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 1320, 800));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    private sealed class TestDialogs : IDesktopDialogs
    {
        public string? Folder { get; set; }
        public string? SelectFolder() => Folder;
        public void ExportSpecification(IReadOnlyList<SpecificationRow> rows) { }
    }
    private sealed class BindingErrors : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (message is not null) Errors.Add(message); }
        public override void WriteLine(string? message) { if (message is not null) Errors.Add(message); }
    }
}
