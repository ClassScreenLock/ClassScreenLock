namespace ClassScreenLock.Models;

/// <summary>
/// 预设强调色选项（用于设置页色板展示与选中状态）。
/// </summary>
public class AccentColorOption : ViewModels.ViewModelBase
{
    public string Hex { get; init; } = "#0067C0";

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }
}
