using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassScreenLock.ViewModels;

namespace ClassScreenLock.Views;

public partial class Sidebar : UserControl
{
    private bool _isUpdatingSelection;

    public Sidebar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SidebarViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            // 初始同步选中态
            UpdateListBoxSelection(vm.CurrentTarget);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SidebarViewModel.CurrentTarget) &&
            sender is SidebarViewModel vm)
        {
            UpdateListBoxSelection(vm.CurrentTarget);
        }
    }

    private void UpdateListBoxSelection(string target)
    {
        _isUpdatingSelection = true;
        try
        {
            // 在主菜单中查找
            var menuItem = MenuListBox.ItemsSource?.Cast<MenuItemViewModel>()
                .FirstOrDefault(i => i.Target == target);
            if (menuItem != null)
            {
                MenuListBox.SelectedItem = menuItem;
                BottomMenuListBox.SelectedIndex = -1;
                return;
            }

            // 在底部菜单中查找
            var bottomItem = BottomMenuListBox.ItemsSource?.Cast<MenuItemViewModel>()
                .FirstOrDefault(i => i.Target == target);
            if (bottomItem != null)
            {
                BottomMenuListBox.SelectedItem = bottomItem;
                MenuListBox.SelectedIndex = -1;
                return;
            }

            // 都没找到，清除选中
            MenuListBox.SelectedIndex = -1;
            BottomMenuListBox.SelectedIndex = -1;
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void MenuListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection) return;
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is MenuItemViewModel item)
        {
            // 清除底部菜单选中
            _isUpdatingSelection = true;
            BottomMenuListBox.SelectedIndex = -1;
            _isUpdatingSelection = false;

            // 执行导航命令
            if (item.Command != null && item.Command.CanExecute(item.Target))
            {
                item.Command.Execute(item.Target);
            }
        }
    }

    private void BottomMenuListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection) return;
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is MenuItemViewModel item)
        {
            // 清除主菜单选中
            _isUpdatingSelection = true;
            MenuListBox.SelectedIndex = -1;
            _isUpdatingSelection = false;

            // 执行导航命令
            if (item.Command != null && item.Command.CanExecute(item.Target))
            {
                item.Command.Execute(item.Target);
            }
        }
    }
}
