using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ClassScreenLock.Models;
using ClassScreenLock.Services;

namespace ClassScreenLock.ViewModels;

/// <summary>
/// UI 绑定的快速操作项
/// Label / Description / Category 通过 LocalizationService 解析为本地化文本
/// </summary>
public partial class QuickActionItemViewModel : ObservableObject
{
    private string _labelKey = string.Empty;
    private string _descriptionKey = string.Empty;
    private string _categoryKey = string.Empty;

    [ObservableProperty]
    private string _id = string.Empty;

    private string _label = string.Empty;

    /// <summary>
    /// 已解析的本地化显示文本（来自 LabelKey）
    /// </summary>
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    [ObservableProperty]
    private string _iconName = string.Empty;

    [ObservableProperty]
    private string _accentColor = "#0078D4";

    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>
    /// 已解析的分类显示文本（来自 Category）
    /// </summary>
    public string Category
    {
        get => _categoryKey;
        set => SetProperty(ref _categoryKey, value);
    }

    [ObservableProperty]
    private bool _isCommand;

    [ObservableProperty]
    private string _targetId = string.Empty;

    public QuickActionItemViewModel() { }

    public QuickActionItemViewModel(QuickActionDefinition def)
    {
        Id = def.Id;
        _labelKey = def.LabelKey;
        _descriptionKey = def.DescriptionKey;
        _categoryKey = def.Category;
        IconName = def.IconName;
        AccentColor = def.AccentColor;
        IsCommand = def.IsCommand;
        TargetId = def.TargetId;
        ResolveLocalized();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        ResolveLocalized();
    }

    private void ResolveLocalized()
    {
        Label = Resolve(_labelKey);
        Description = Resolve(_descriptionKey);
        Category = Resolve(_categoryKey);
    }

    private static string Resolve(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        try
        {
            return LocalizationService.Instance.GetString(key);
        }
        catch
        {
            return key;
        }
    }

    public static QuickActionItemViewModel FromDefinition(QuickActionDefinition def)
        => new(def);
}
