using MiniPdm.Core;

namespace MiniPdm.Desktop;

public sealed class TreeNodeViewModel(TreeRow row, Action<long> select) : ObservableObject
{
    private bool selected;
    public TreeRow Row { get; } = row;
    public List<TreeNodeViewModel> Children { get; } = [];
    public string Caption => $"{Row.Designation}  {Row.Name}  ×{Row.Quantity}".Trim() + (Row.VersionId is null ? " [нет версии]" : "");
    public bool IsSelected
    {
        get => selected;
        set { if (Set(ref selected, value) && value) select(Row.ObjectId); }
    }
}
