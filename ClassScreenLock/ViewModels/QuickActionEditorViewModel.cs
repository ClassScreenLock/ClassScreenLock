using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassScreenLock.ViewModels;

/// <summary>
/// 快速操作编辑器的 VM
/// 左侧：当前选中的 4-8 个快速操作（可排序、可删除）
/// 右侧：34 个候选功能（按分类、可搜索、可一键加入）
/// </summary>
public partial class QuickActionEditorViewModel : ObservableObject
{
    private const int MaxQuickActions = 8;

    private readonly MainWindowViewModel _mainVM;

    /// <summary>
    /// 当前已选中的快速操作（用户配置）
    /// </summary>
    public ObservableCollection<QuickActionItemViewModel> Selected { get; } = new();

    /// <summary>
    /// 所有可用的候选功能（按分类）
    /// </summary>
    public ObservableCollection<QuickActionCategoryGroup> Categories { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public int MaxCount => MaxQuickActions;

    public int SelectedCount => Selected.Count;

    public QuickActionEditorViewModel(MainWindowViewModel mainVM)
    {
        _mainVM = mainVM;
        LoadSelected();
        LoadCatalog();
    }

    private void LoadSelected()
    {
        Selected.Clear();
        var configured = SettingsService.General.QuickActions;
        if (configured.Count == 0)
        {
            foreach (var id in QuickActionCatalog.DefaultActionIds)
            {
                var def = QuickActionCatalog.FindById(id);
                if (def != null) Selected.Add(QuickActionItemViewModel.FromDefinition(def));
            }
        }
        else
        {
            foreach (var qa in configured.OrderBy(q => q.Order))
            {
                var def = QuickActionCatalog.FindById(qa.Id);
                if (def != null) Selected.Add(QuickActionItemViewModel.FromDefinition(def));
            }
        }
    }

    private void LoadCatalog()
    {
        Categories.Clear();
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        foreach (var grp in QuickActionCatalog.GroupByCategory())
        {
            var items = grp.Select(QuickActionItemViewModel.FromDefinition).ToList();
            var group = new QuickActionCategoryGroup
            {
                CategoryKey = grp.Key,
                Category = ResolveCategory(grp.Key),
                Items = new ObservableCollection<QuickActionItemViewModel>(items)
            };
            Categories.Add(group);
        }
    }

    private static string ResolveCategory(string key)
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

    private void OnLanguageChanged(object? sender, string e)
    {
        // 重新加载目录以刷新分类的本地化文本
        LoadCatalog();
    }

    [RelayCommand]
    private void Add(QuickActionItemViewModel? item)
    {
        if (item == null) return;
        if (Selected.Count >= MaxQuickActions)
        {
            StatusMessage = $"最多添加 {MaxQuickActions} 个";
            return;
        }
        if (Selected.Any(s => s.Id == item.Id))
        {
            StatusMessage = "已在快速操作中";
            return;
        }
        Selected.Add(QuickActionItemViewModel.FromDefinition(
            QuickActionCatalog.FindById(item.Id) ?? new QuickActionDefinition()));
        StatusMessage = $"已添加: {item.Label}";
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void Remove(QuickActionItemViewModel? item)
    {
        if (item == null) return;
        if (Selected.Remove(item))
        {
            StatusMessage = $"已移除: {item.Label}";
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    [RelayCommand]
    private void MoveUp(QuickActionItemViewModel? item)
    {
        if (item == null) return;
        var idx = Selected.IndexOf(item);
        if (idx > 0)
        {
            Selected.Move(idx, idx - 1);
        }
    }

    [RelayCommand]
    private void MoveDown(QuickActionItemViewModel? item)
    {
        if (item == null) return;
        var idx = Selected.IndexOf(item);
        if (idx >= 0 && idx < Selected.Count - 1)
        {
            Selected.Move(idx, idx + 1);
        }
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        Selected.Clear();
        foreach (var id in QuickActionCatalog.DefaultActionIds)
        {
            var def = QuickActionCatalog.FindById(id);
            if (def != null) Selected.Add(QuickActionItemViewModel.FromDefinition(def));
        }
        StatusMessage = "已重置为默认";
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void Save()
    {
        var list = Selected
            .Select((qa, i) => new QuickAction(qa.Id, i, true))
            .ToList();
        SettingsService.UpdateGeneral(g => g.QuickActions = list);
        StatusMessage = "已保存";
    }
}

public class QuickActionCategoryGroup : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string _categoryKey = string.Empty;

    /// <summary>
    /// 分类的本地化键（如 QA_Cat_Navigation）
    /// </summary>
    public string CategoryKey
    {
        get => _categoryKey;
        set
        {
            if (SetProperty(ref _categoryKey, value))
            {
                OnPropertyChanged(nameof(Category));
                Category = Resolve(value);
            }
        }
    }

    private string _category = string.Empty;

    /// <summary>
    /// 已解析的分类显示文本
    /// </summary>
    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public ObservableCollection<QuickActionItemViewModel> Items { get; set; } = new();

    public void RefreshLocalization()
    {
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
}
