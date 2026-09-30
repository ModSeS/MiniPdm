using MiniPdm.Core;

namespace MiniPdm.Desktop;

public sealed class MainViewModel : ObservableObject
{
    private readonly IPdmStore store;
    private readonly ImportService importer;
    private readonly BomService bom;
    private readonly IDesktopDialogs dialogs;
    private readonly List<AsyncCommand> commands = [];
    private IReadOnlyList<ObjectCard> allObjects = [];
    private IReadOnlyList<TreeRow> loadedTree = [];
    private CancellationTokenSource? importCancellation;
    private bool busy;
    private string search = "";
    private string status = "Импортируйте папку cad-export, чтобы начать работу.";
    private string massResult = "";
    private ObjectCard? selectedCatalogObject;
    private ObjectCard? selectedObject;
    private int selectedTab;

    public MainViewModel(IPdmStore store, ImportService importer, BomService bom, IDesktopDialogs dialogs)
    {
        this.store = store; this.importer = importer; this.bom = bom; this.dialogs = dialogs;
        RefreshCommand = Command(RefreshAsync);
        ImportCommand = Command(ImportAsync);
        ApproveCommand = Command(() => ChangeStateAsync(VersionState.Approved), () => SelectedObject?.State == VersionState.Draft);
        CancelVersionCommand = Command(() => ChangeStateAsync(VersionState.Cancelled), () => SelectedObject?.VersionId is not null);
        CalculateCommand = Command(CalculateAsync, () => SelectedObject?.Type == ObjectType.Assembly && SelectedObject.VersionId is not null);
        ExportCommand = Command(() => { dialogs.ExportSpecification(Specification); return Task.CompletedTask; }, () => Specification.Count != 0);
        CancelImportCommand = new(() => { importCancellation?.Cancel(); return Task.CompletedTask; }, () => importCancellation is not null, ShowError);
    }

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand ApproveCommand { get; }
    public AsyncCommand CancelVersionCommand { get; }
    public AsyncCommand CalculateCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public AsyncCommand CancelImportCommand { get; }
    public bool IsBusy { get => busy; private set { Set(ref busy, value); Notify(nameof(IsIdle)); UpdateCommands(); } }
    public bool IsIdle => !IsBusy;
    public string Status { get => status; private set => Set(ref status, value); }
    public string MassResult { get => massResult; private set => Set(ref massResult, value); }
    public string Search { get => search; set { if (Set(ref search, value)) Notify(nameof(Objects)); } }
    public IEnumerable<ObjectCard> Objects => allObjects.Where(o => o.Caption.Contains(Search, StringComparison.OrdinalIgnoreCase));
    public IReadOnlyList<TreeNodeViewModel> Roots { get; private set; } = [];
    public IReadOnlyList<TreeRow> DirectChildren { get; private set; } = [];
    public IReadOnlyList<SpecificationRow> Specification { get; private set; } = [];
    public IReadOnlyList<ImportRow> ImportRows { get; private set; } = [];
    public string ImportSummary { get; private set; } = "Импорт ещё не выполнялся";
    public int SelectedTab { get => selectedTab; set => Set(ref selectedTab, value); }
    public ObjectCard? SelectedObject { get => selectedObject; private set { Set(ref selectedObject, value); UpdateCommands(); } }
    public ObjectCard? SelectedCatalogObject
    {
        get => selectedCatalogObject;
        set
        {
            if (!Set(ref selectedCatalogObject, value) || value is null || IsBusy) return;
            // AsyncCommand централизованно обрабатывает ошибки, в setter нет async void.
            new AsyncCommand(() => RunBusyAsync(() => LoadTreeAsync(value.Id)), () => !IsBusy, ShowError).Execute(null);
        }
    }

    private AsyncCommand Command(Func<Task> action, Func<bool>? available = null)
    {
        var command = new AsyncCommand(() => RunBusyAsync(action), () => !IsBusy && (available?.Invoke() ?? true), ShowError);
        commands.Add(command);
        return command;
    }
    private async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        try { await action(); }
        finally { IsBusy = false; }
    }
    private void UpdateCommands() { foreach (var command in commands) command.RaiseCanExecuteChanged(); }
    private void ShowError(Exception ex) => Status = ex is OperationCanceledException
        ? "Импорт отменён. Изменения не сохранены." : "Ошибка: " + ex.Message;

    private async Task RefreshAsync()
    {
        long? rootId = selectedCatalogObject?.Id;
        allObjects = await Task.Run(() => store.GetObjects());
        Notify(nameof(Objects));
        selectedCatalogObject = allObjects.FirstOrDefault(o => o.Id == rootId) ?? allObjects.FirstOrDefault();
        Notify(nameof(SelectedCatalogObject));
        if (selectedCatalogObject is not null) await LoadTreeAsync(selectedCatalogObject.Id);
        Status = $"Объектов в базе: {allObjects.Count}. SQLite • данные сохраняются между запусками.";
    }
    private async Task LoadTreeAsync(long id)
    {
        loadedTree = await Task.Run(() => store.GetTree(id));
        var nodes = loadedTree.ToDictionary(r => r.Path, r => new TreeNodeViewModel(r, SelectObject));
        foreach (var node in nodes.Values)
            if (node.Row.ParentPath != "" && nodes.TryGetValue(node.Row.ParentPath, out var parent)) parent.Children.Add(node);
        Roots = nodes.Values.Where(n => n.Row.ParentPath == "").ToArray();
        Notify(nameof(Roots));
        SelectObject(id);
    }
    private void SelectObject(long id)
    {
        SelectedObject = allObjects.FirstOrDefault(o => o.Id == id);
        var occurrence = loadedTree.FirstOrDefault(r => r.ObjectId == id);
        DirectChildren = loadedTree.Where(r => occurrence is not null && r.ParentPath == occurrence.Path).ToArray();
        Notify(nameof(DirectChildren));
        Specification = [];
        Notify(nameof(Specification));
        MassResult = SelectedObject?.Type == ObjectType.Assembly ? "Нажмите «Рассчитать состав»" : "";
        UpdateCommands();
    }
    private async Task ImportAsync()
    {
        string? folder = dialogs.SelectFolder();
        if (folder is null) return;
        using var cancellation = new CancellationTokenSource();
        importCancellation = cancellation;
        CancelImportCommand.RaiseCanExecuteChanged();
        try
        {
            var progress = new Progress<string>(message => Status = message);
            var report = await Task.Run(() => importer.ImportAsync(folder, progress, cancellation.Token));
            await RefreshAsync();
            // Ошибки показываем первыми, чтобы их не пришлось искать среди принятых файлов.
            ImportRows = report.Rows.OrderBy(r => r.Outcome == ImportOutcome.Rejected ? 0 : r.HasWarning ? 1 : 2)
                .ThenBy(r => r.FileName, StringComparer.Ordinal).ToArray();
            ImportSummary = report.Summary;
            Notify(nameof(ImportRows)); Notify(nameof(ImportSummary));
            SelectedTab = 2;
            Status = report.Summary;
        }
        finally { importCancellation = null; CancelImportCommand.RaiseCanExecuteChanged(); }
    }
    private async Task ChangeStateAsync(VersionState state)
    {
        var selected = SelectedObject!;
        await Task.Run(() => store.ChangeState(selected.VersionId!.Value, state));
        await RefreshAsync();
        SelectObject(selected.Id);
        Status = "Состояние изменено. Текущая версия и состав обновлены.";
    }
    private async Task CalculateAsync()
    {
        long id = SelectedObject!.Id;
        Specification = await Task.Run(() => bom.GetSpecification(id));
        Notify(nameof(Specification));
        SelectedTab = 1;
        // Спецификация остаётся видимой даже при неизвестной массе детали.
        var missing = Specification.Where(r => r.UnitMassKg is null).Select(r => r.Name).ToArray();
        MassResult = missing.Length == 0
            ? $"Масса сборки: {Specification.Sum(r => r.TotalMassKg!.Value):0.######} кг"
            : "Не указана масса: " + string.Join(", ", missing);
        Status = MassResult;
    }
}
