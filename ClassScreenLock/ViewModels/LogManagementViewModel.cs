using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Services;
using System.Linq;

namespace ClassScreenLock.ViewModels;

public partial class LogManagementViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<LogEntry> _logs = new();

    public LogManagementViewModel()
    {
        RefreshLogs();
    }

    [RelayCommand]
    private void RefreshLogs()
    {
        var logList = LogService.Instance.LoadLogs();
        Logs = new ObservableCollection<LogEntry>(logList);
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogService.Instance.ClearLogs();
        RefreshLogs();
    }
}
