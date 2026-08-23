using CommunityToolkit.Mvvm.ComponentModel;

namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 表示侧边栏导航中的一个标签页。
/// </summary>
public class TabItem : ObservableObject
{
    private string _id = string.Empty;
    private string _title = string.Empty;
    private bool _isEnabled = true;
    private bool _isSelected;
    private object? _content;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public object? Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }
}
