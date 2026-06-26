using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Models;
using System.Linq;

namespace ClassScreenLock.Views;

public partial class AutomationView : UserControl
{
    public AutomationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnTriggerSuggestionTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c) return;
        var suggestion = c.DataContext as AutomationViewModel.ProcessSuggestion;
        if (suggestion == null) return;

        var trig = c.Tag as AutomationTrigger;

        if (DataContext is AutomationViewModel vm && trig != null)
        {
            vm.ProcessFilterText = suggestion.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(suggestion.Path))
            {
                trig.FilePath = suggestion.Path;
            }
            vm.UseFilterTextForTriggerCommand.Execute(trig);
            e.Handled = true;
        }
    }

    private void OnConditionSuggestionTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c) return;
        var suggestion = c.DataContext as AutomationViewModel.ProcessSuggestion;
        if (suggestion == null) return;

        var cond = c.Tag as AutomationCondition;

        if (DataContext is AutomationViewModel vm && cond != null)
        {
            vm.ProcessFilterText = suggestion.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(suggestion.Path))
            {
                cond.FilePath = suggestion.Path;
            }
            vm.UseFilterTextForConditionCommand.Execute(cond);
            e.Handled = true;
        }
    }

    private void OnTriggerSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        var lb = sender as ListBox;
        var suggestion = lb?.SelectedItem as AutomationViewModel.ProcessSuggestion;
        var trig = lb?.Tag as AutomationTrigger;
        if (suggestion == null || trig == null) return;
        if (DataContext is AutomationViewModel vm)
        {
            vm.ProcessFilterText = suggestion.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(suggestion.Path) && trig != null)
            {
                trig.FilePath = suggestion.Path;
            }
            vm.UseFilterTextForTriggerCommand.Execute(trig);
        }
        lb!.SelectedItem = null;
        e.Handled = true;
    }

    private void OnConditionSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        var lb = sender as ListBox;
        var suggestion = lb?.SelectedItem as AutomationViewModel.ProcessSuggestion;
        var cond = lb?.Tag as AutomationCondition;
        if (suggestion == null || cond == null) return;
        if (DataContext is AutomationViewModel vm)
        {
            vm.ProcessFilterText = suggestion.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(suggestion.Path) && cond != null)
            {
                cond.FilePath = suggestion.Path;
            }
            vm.UseFilterTextForConditionCommand.Execute(cond);
        }
        lb!.SelectedItem = null;
        e.Handled = true;
    }
}
