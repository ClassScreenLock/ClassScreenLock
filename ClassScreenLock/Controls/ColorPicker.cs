using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace ClassScreenLock.Controls
{
    public partial class ColorPicker : UserControl
    {
        // 控件引用
        private Slider? _redSlider;
        private Slider? _greenSlider;
        private Slider? _blueSlider;
        private TextBlock? _redValue;
        private TextBlock? _greenValue;
        private TextBlock? _blueValue;
        private TextBox? _hexTextBox;
        private Border? _colorPreview;
        private TextBlock? _previewText;
        private Button? _confirmButton;
        
        private Color _pendingColor;
        private string _pendingColorHex = string.Empty;

        public static readonly StyledProperty<Color> SelectedColorProperty = 
            AvaloniaProperty.Register<ColorPicker, Color>(nameof(SelectedColor), Colors.DodgerBlue, defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<string> SelectedColorHexProperty = 
            AvaloniaProperty.Register<ColorPicker, string>(nameof(SelectedColorHex), "#0078D4", defaultBindingMode: BindingMode.TwoWay);

        public Color SelectedColor
        {
            get => GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public string SelectedColorHex
        {
            get => GetValue(SelectedColorHexProperty);
            set => SetValue(SelectedColorHexProperty, value);
        }

        // 重写OnPropertyChanged方法来处理属性变化
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            
            if (change.Property == SelectedColorProperty && change.NewValue is Color newColor)
            {
                // 避免无限循环
                if (!_updatingColor)
                {
                    _updatingColor = true;
                    try
                    {
                        var newHex = string.Format("#{0:X2}{1:X2}{2:X2}", newColor.R, newColor.G, newColor.B);
                        if (SelectedColorHex != newHex)
                        {
                            SelectedColorHex = newHex;
                        }
                        _pendingColor = newColor;
                        _pendingColorHex = newHex;
                        UpdateRGBSliders(newColor);
                    }
                    finally
                    {
                        _updatingColor = false;
                    }
                }
            }
            else if (change.Property == SelectedColorHexProperty && change.NewValue is string newHex)
            {
                // 避免无限循环
                if (!_updatingColor)
                {
                    _updatingColor = true;
                    try
                    {
                        if (Color.TryParse(newHex, out var parsedColor))
                        {
                            if (SelectedColor != parsedColor)
                            {
                                SelectedColor = parsedColor;
                            }
                            _pendingColor = parsedColor;
                            _pendingColorHex = newHex;
                            // 确保十六进制变化也同步更新滑块与预览
                            UpdateRGBSliders(parsedColor);
                        }
                    }
                    finally
                    {
                        _updatingColor = false;
                    }
                }
            }
        }

        // 用于避免属性变化时的无限循环
        private bool _updatingColor = false;

        public ColorPicker()
        {
            InitializeComponent();
            
            // 在控件加载后设置事件处理
            this.AttachedToVisualTree += OnAttachedToVisualTree;
        }

        private void InitializeComponent()
        {
            // 预设颜色数组
            var presetColors = new string[]
            {
                "#FF0000", "#00FF00", "#0000FF", "#FFFF00", "#FF00FF", "#00FFFF",
                "#FFA500", "#800080", "#008000", "#800000", "#008080", "#000080"
            };
            
            var initialColor = SelectedColor;
            var initialHex = SelectedColorHex;
            
            _pendingColor = initialColor;
            _pendingColorHex = initialHex;
            
            // 创建控件引用
            _previewText = new TextBlock
            {
                Text = initialHex,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(ContrastColor(initialColor))
            };

            _colorPreview = new Border
            {
                CornerRadius = new CornerRadius(4),
                Height = 60,
                Background = new SolidColorBrush(initialColor),
                Name = "ColorPreview",
                Margin = new Thickness(0, 0, 0, 10),
                Child = _previewText
            };

            _redSlider = new Slider
            {
                Name = "RedSlider",
                Minimum = 0,
                Maximum = 255,
                Value = initialColor.R,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 10, 0)
            };

            _redValue = new TextBlock
            {
                Name = "RedValue",
                Text = initialColor.R.ToString(),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                [Grid.ColumnProperty] = 1
            };

            _greenSlider = new Slider
            {
                Name = "GreenSlider",
                Minimum = 0,
                Maximum = 255,
                Value = initialColor.G,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 10, 0)
            };

            _greenValue = new TextBlock
            {
                Name = "GreenValue",
                Text = initialColor.G.ToString(),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                [Grid.ColumnProperty] = 1
            };

            _blueSlider = new Slider
            {
                Name = "BlueSlider",
                Minimum = 0,
                Maximum = 255,
                Value = initialColor.B,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 10, 0)
            };

            _blueValue = new TextBlock
            {
                Name = "BlueValue",
                Text = initialColor.B.ToString(),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                [Grid.ColumnProperty] = 1
            };

            _hexTextBox = new TextBox
            {
                Name = "HexTextBox",
                Text = initialHex,
                FontWeight = FontWeight.Bold,
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent)
            };

            _confirmButton = new Button
            {
                Content = "应用颜色修改",
                Classes = { "accent" },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 10, 0, 0)
            };

            _confirmButton.Click += (s, e) =>
            {
                SelectedColor = _pendingColor;
                SelectedColorHex = _pendingColorHex;
            };

            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 15,
                    Children =
                    {
                        // 颜色预览
                        _colorPreview,
                        
                        // RGB滑块区域
                        new Border
                        {
                            CornerRadius = new CornerRadius(4),
                            Background = new SolidColorBrush(Colors.Transparent),
                            Padding = new Thickness(10),
                            BorderBrush = new SolidColorBrush(Colors.Gray),
                            BorderThickness = new Thickness(1),
                            Child = new StackPanel
                            {
                                Spacing = 10,
                                Children =
                                {
                                    // 红色滑块
                                    new StackPanel
                                    {
                                        Spacing = 5,
                                        Children =
                                        {
                                            new TextBlock { Text = "红色 (R)" },
                                            new Grid
                                            {
                                                ColumnDefinitions = new ColumnDefinitions("*,40"),
                                                Children = { _redSlider, _redValue }
                                            }
                                        }
                                    },
                                    
                                    // 绿色滑块
                                    new StackPanel
                                    {
                                        Spacing = 5,
                                        Children =
                                        {
                                            new TextBlock { Text = "绿色 (G)" },
                                            new Grid
                                            {
                                                ColumnDefinitions = new ColumnDefinitions("*,40"),
                                                Children = { _greenSlider, _greenValue }
                                            }
                                        }
                                    },
                                    
                                    // 蓝色滑块
                                    new StackPanel
                                    {
                                        Spacing = 5,
                                        Children =
                                        {
                                            new TextBlock { Text = "蓝色 (B)" },
                                            new Grid
                                            {
                                                ColumnDefinitions = new ColumnDefinitions("*,40"),
                                                Children = { _blueSlider, _blueValue }
                                            }
                                        }
                                    },
                                    
                                    // 十六进制颜色值显示
                                    new StackPanel
                                    {
                                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                                        Spacing = 10,
                                        Children =
                                        {
                                            new TextBlock { Text = "十六进制:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                                            new Border
                                            {
                                                CornerRadius = new CornerRadius(4),
                                                Padding = new Thickness(8, 4),
                                                Background = new SolidColorBrush(Colors.Transparent),
                                                BorderBrush = new SolidColorBrush(Colors.Gray),
                                                BorderThickness = new Thickness(1),
                                                Child = _hexTextBox
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        
                        // 预设颜色
                        new StackPanel
                        {
                            Spacing = 10,
                            Children =
                            {
                                new TextBlock { Text = "预设颜色" },
                                new ItemsControl
                                {
                                    Name = "PresetColors",
                                    ItemsSource = presetColors,
                                    ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal }),
                                    ItemTemplate = new FuncDataTemplate<string>((color, _) =>
                                    {
                                        var button = new Button
                                        {
                                            Width = 30,
                                            Height = 30,
                                            CornerRadius = new CornerRadius(15),
                                            Margin = new Thickness(3),
                                            Background = new SolidColorBrush(Color.Parse(color)),
                                            BorderThickness = new Thickness(1),
                                            BorderBrush = new SolidColorBrush(Colors.Gray)
                                        };
                                        
                                        button.Click += (s, e) =>
                                        {
                                            try
                                            {
                                                var parsedColor = Color.Parse(color);
                                                _pendingColor = parsedColor;
                                                _pendingColorHex = color;
                                                
                                                // 更新滑块和显示值
                                                UpdateRGBSliders(parsedColor);
                                                UpdateRGBValues();
                                                
                                                // 更新预览
                                                UpdateColorPreview(parsedColor, color);
                                                
                                                // 更新十六进制值
                                                if (_hexTextBox != null)
                                                {
                                                    _hexTextBox.Text = color;
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"解析颜色失败: {ex.Message}");
                                            }
                                        };
                                        
                                        return button;
                                    })
                                }
                            }
                        },

                        // 确认按钮
                        _confirmButton
                    }
                }
            };
        }
        
        private bool _eventHandlersSet = false;

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (!_eventHandlersSet)
            {
                SetupEventHandlers();
                _eventHandlersSet = true;
            }
            else
            {
                // 如果已经设置过，确保当前 UI 状态与属性一致
                UpdateRGBSliders(SelectedColor);
            }
        }
        
        private void SetupEventHandlers()
        {
            // 订阅滑块值变化事件
            if (_redSlider != null) _redSlider.PropertyChanged += OnSliderPropertyChanged;
            if (_greenSlider != null) _greenSlider.PropertyChanged += OnSliderPropertyChanged;
            if (_blueSlider != null) _blueSlider.PropertyChanged += OnSliderPropertyChanged;
            
            // 设置十六进制文本框事件
            if (_hexTextBox != null)
            {
                _hexTextBox.KeyUp += (s, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        UpdateColorFromHex();
                    }
                };
                
                _hexTextBox.LostFocus += (s, e) =>
                {
                    UpdateColorFromHex();
                };
            }
            
            // 初始同步一次
            UpdateRGBSliders(SelectedColor);
        }

        private void OnSliderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Slider.ValueProperty && !_updatingColor)
            {
                UpdateColorFromRGB();
            }
        }
        
        // 从十六进制文本框更新颜色
        private void UpdateColorFromHex()
        {
            if (_hexTextBox != null && !_updatingColor)
            {
                _updatingColor = true;
                try
                {
                    var hexText = _hexTextBox.Text ?? string.Empty;
                    if (Color.TryParse(hexText, out var color))
                    {
                        _pendingColor = color;
                        _pendingColorHex = hexText;
                        UpdateRGBSliders(color);
                    }
                }
                catch (Exception ex)
                {
                    // 如果解析失败，恢复原来的值
                    _hexTextBox.Text = SelectedColorHex;
                    Console.WriteLine($"解析颜色失败: {ex.Message}");
                }
                finally
                {
                    _updatingColor = false;
                }
            }
        }
        
        // 从RGB滑块更新颜色
        private void UpdateColorFromRGB()
        {
            if (_redSlider != null && _greenSlider != null && _blueSlider != null && !_updatingColor)
            {
                _updatingColor = true;
                try
                {
                    var r = (byte)_redSlider.Value;
                    var g = (byte)_greenSlider.Value;
                    var b = (byte)_blueSlider.Value;
                    
                    var color = Color.FromRgb(r, g, b);
                    var hex = $"#{r:X2}{g:X2}{b:X2}";
                    
                    // 更新待定值
                    _pendingColor = color;
                    _pendingColorHex = hex;
                    
                    // 更新显示值
                    UpdateRGBValues();
                    
                    // 更新预览
                    UpdateColorPreview(color, hex);
                    
                    // 更新十六进制值
                    if (_hexTextBox != null)
                    {
                        _hexTextBox.Text = hex;
                    }
                }
                finally
                {
                    _updatingColor = false;
                }
            }
        }
        
        // 更新颜色预览
        private void UpdateColorPreview(Color color, string hex)
        {
            if (_colorPreview != null)
            {
                _colorPreview.Background = new SolidColorBrush(color);
                if (_colorPreview.Child is TextBlock textBlock)
                {
                    textBlock.Text = hex;
                    textBlock.Foreground = new SolidColorBrush(ContrastColor(color));
                }
            }
        }
        
        // 更新RGB值显示
        private void UpdateRGBValues()
        {
            if (_redSlider != null && _greenSlider != null && _blueSlider != null)
            {
                if (_redValue != null)
                {
                    _redValue.Text = $"{(int)_redSlider.Value}";
                }
                
                if (_greenValue != null)
                {
                    _greenValue.Text = $"{(int)_greenSlider.Value}";
                }
                
                if (_blueValue != null)
                {
                    _blueValue.Text = $"{(int)_blueSlider.Value}";
                }
            }
        }
        
        // 根据颜色值更新RGB滑块
        private void UpdateRGBSliders(Color color)
        {
            if (_redSlider != null && _greenSlider != null && _blueSlider != null)
            {
                _redSlider.Value = color.R;
                _greenSlider.Value = color.G;
                _blueSlider.Value = color.B;
                
                UpdateRGBValues();
                
                // 计算十六进制值
                var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                
                // 更新预览
                UpdateColorPreview(color, hex);
                
                // 更新十六进制值
                if (_hexTextBox != null)
                {
                    _hexTextBox.Text = hex;
                }
            }
        }
        
        // 根据颜色值更新RGB滑块（字符串版本）
        private void UpdateRGBSliders(string colorHex)
        {
            try
            {
                var color = Color.Parse(colorHex);
                UpdateRGBSliders(color);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解析颜色失败: {ex.Message}");
            }
        }
        
        // 计算对比色（黑色或白色）
        private Color ContrastColor(Color color)
        {
            // 计算相对亮度
            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return luminance > 0.5 ? Colors.Black : Colors.White;
        }
    }
}
