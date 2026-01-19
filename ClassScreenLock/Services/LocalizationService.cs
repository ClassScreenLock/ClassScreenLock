using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace ClassScreenLock.Services
{
    public class LocalizationService : IDisposable
    {
        private static readonly Lazy<LocalizationService> _lazy = new Lazy<LocalizationService>(() => new LocalizationService());
        public static LocalizationService Instance => _lazy.Value;

        // 语言变化事件
        public event EventHandler<string>? LanguageChanged;

        private readonly Dictionary<string, ResourceDictionary> _resourceDictionaries = new();
        private string _currentLanguage = "zh-CN";
        private ResourceDictionary? _currentResourceDictionary;
        private bool _disposed = false;

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    ApplyLanguage(value);
                    // 触发语言变化事件
                    LanguageChanged?.Invoke(this, value);
                }
            }
        }

        private LocalizationService()
        {
            // 不在构造函数中做太多事情，尤其是涉及 Avalonia 资源的操作
        }

        public void Initialize()
        {
            // 加载所有支持的语言资源
            LoadLanguageResource("zh-CN");
            LoadLanguageResource("en-US");
            
            // 默认使用中文
            if (_resourceDictionaries.ContainsKey("zh-CN"))
            {
                _currentResourceDictionary = _resourceDictionaries["zh-CN"];
            }
            _currentLanguage = "zh-CN";
            
            // 如果应用程序已初始化，则立即添加默认语言资源
            if (Application.Current != null && _currentResourceDictionary != null)
            {
                UpdateApplicationResources(_currentResourceDictionary);
            }
        }

        // 加载单个语言资源
        private void LoadLanguageResource(string languageCode)
        {
            try
            {
                if (!_resourceDictionaries.ContainsKey(languageCode))
                {
                    // 加载语言资源字典
                    var uri = new Uri($"avares://ClassScreenLock/Resources/Localization/{languageCode}.axaml", UriKind.Absolute);
                    var resourceDict = AvaloniaXamlLoader.Load(uri) as ResourceDictionary;
                    if (resourceDict != null)
                    {
                        _resourceDictionaries[languageCode] = resourceDict;
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"加载语言资源 {languageCode} 失败: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMsg += $" | Inner: {ex.InnerException.Message}";
                }
                LogService.Instance.Log("Error", "Localization", "LoadResource", errorMsg);
            }
        }

        // 应用指定语言
        private void ApplyLanguage(string languageCode)
        {
            try
            {
                if (_resourceDictionaries.TryGetValue(languageCode, out var resourceDict))
                {
                    _currentResourceDictionary = resourceDict;
                    UpdateApplicationResources(resourceDict);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"应用语言 {languageCode} 失败: {ex.Message}");
            }
        }

        // 更新应用程序资源字典
        private void UpdateApplicationResources(ResourceDictionary resourceDict)
        {
            if (Application.Current == null) return;
            
            // 移除所有语言资源字典
            for (var i = Application.Current.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var dict = Application.Current.Resources.MergedDictionaries[i];
                // 检查是否是语言资源字典（通过检查是否包含特定的本地化键）
                if (dict.TryGetResource("Sidebar_Home", null, out _))
                {
                    Application.Current.Resources.MergedDictionaries.RemoveAt(i);
                }
            }

            // 添加新的语言资源字典
            Application.Current.Resources.MergedDictionaries.Add(resourceDict);
        }

        // 获取本地化字符串
        public string GetString(string key)
        {
            // 首先尝试从当前资源字典获取
            if (_currentResourceDictionary != null && _currentResourceDictionary.TryGetResource(key, null, out var resource))
            {
                return resource as string ?? key;
            }
            
            // 如果失败，尝试从应用程序资源获取
            if (Application.Current != null && Application.Current.Resources.TryGetResource(key, null, out resource))
            {
                return resource as string ?? key;
            }
            
            // 如果仍然失败，返回键名
            return key;
        }

        public IEnumerable<string> GetSupportedLanguages()
        {
            return new[] { "zh-CN", "en-US" };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 清理托管资源
                    LanguageChanged = null;
                    
                    // 清理资源字典
                    foreach (var resourceDict in _resourceDictionaries.Values)
                    {
                        resourceDict?.Clear();
                    }
                    _resourceDictionaries.Clear();
                    
                    _currentResourceDictionary = null;
                }
                
                _disposed = true;
            }
        }

        ~LocalizationService()
        {
            Dispose(false);
        }
    }
}