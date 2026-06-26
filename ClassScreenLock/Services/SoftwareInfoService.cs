using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ClassScreenLock.Services;

[SupportedOSPlatform("windows")]
public class SoftwareInfoService
{
    private static SoftwareInfoService? _instance;
    public static SoftwareInfoService Instance => _instance ??= new SoftwareInfoService();

    public class SoftwareInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? Publisher { get; set; }
        public string? Version { get; set; }
        public string? InstallDate { get; set; }
        public string? InstallLocation { get; set; }
        public string? EstimatedSize { get; set; }
        public string? UninstallString { get; set; }
        public bool IsSystemSoftware { get; set; }
    }

    public List<SoftwareInfo> GetInstalledSoftware(bool includeSystemSoftware = false)
    {
        var softwareList = new List<SoftwareInfo>();
        var softwareDict = new Dictionary<string, SoftwareInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var uninstallKeys = new[]
            {
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var rootKey in uninstallKeys)
            {
                if (rootKey == null) continue;

                foreach (var subKeyName in rootKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = rootKey.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var name = subKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var key = $"{name}_{subKey.GetValue("Publisher")}_{subKey.GetValue("DisplayVersion")}";
                        if (softwareDict.ContainsKey(key)) continue;

                        var isSystem = IsSystemSoftware(subKey);
                        if (!includeSystemSoftware && isSystem) continue;

                        var software = new SoftwareInfo
                        {
                            Name = name.Trim(),
                            Publisher = (subKey.GetValue("Publisher") as string)?.Trim(),
                            Version = (subKey.GetValue("DisplayVersion") as string)?.Trim(),
                            InstallDate = FormatInstallDate(subKey.GetValue("InstallDate") as string),
                            InstallLocation = (subKey.GetValue("InstallLocation") as string)?.Trim(),
                            EstimatedSize = FormatSize(subKey.GetValue("EstimatedSize")),
                            UninstallString = (subKey.GetValue("UninstallString") as string)?.Trim(),
                            IsSystemSoftware = isSystem
                        };

                        softwareDict[key] = software;
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "SoftwareInfo", "GetInstalledSoftware", ex.Message);
        }

        softwareList = softwareDict.Values
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return softwareList;
    }

    private bool IsSystemSoftware(RegistryKey key)
    {
        var systemComponent = key.GetValue("SystemComponent");
        if (systemComponent is int systemInt && systemInt == 1)
            return true;

        var parentKeyName = key.GetValue("ParentKeyName") as string;
        if (!string.IsNullOrEmpty(parentKeyName))
            return true;

        var releaseType = key.GetValue("ReleaseType") as string;
        if (!string.IsNullOrEmpty(releaseType) && 
            (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
             releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
             releaseType.Contains("Security Update", StringComparison.OrdinalIgnoreCase) ||
             releaseType.Contains("Service Pack", StringComparison.OrdinalIgnoreCase)))
            return true;

        var name = key.GetValue("DisplayName") as string;
        if (!string.IsNullOrEmpty(name))
        {
            var systemPatterns = new[]
            {
                "Microsoft Visual C++",
                "Microsoft .NET",
                "Windows SDK",
                "Microsoft .NET Framework",
                "Microsoft ASP.NET",
                "Microsoft Edge",
                "Microsoft OneDrive",
                "Windows Driver Package",
                "Intel",
                "NVIDIA",
                "AMD",
                "Realtek",
                "Microsoft Visual Studio Tools",
                "Microsoft Visual Studio Installer",
                "Microsoft Update Health Tools"
            };

            foreach (var pattern in systemPatterns)
            {
                if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private string? FormatInstallDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;

        if (dateStr.Length == 8 && int.TryParse(dateStr, out _))
        {
            try
            {
                var year = dateStr.Substring(0, 4);
                var month = dateStr.Substring(4, 2);
                var day = dateStr.Substring(6, 2);
                return $"{year}-{month}-{day}";
            }
            catch
            {
                return dateStr;
            }
        }

        return dateStr;
    }

    private string? FormatSize(object? sizeValue)
    {
        if (sizeValue == null) return null;

        try
        {
            long sizeInKB = 0;

            if (sizeValue is int intVal)
                sizeInKB = intVal;
            else if (sizeValue is long longVal)
                sizeInKB = longVal;
            else if (sizeValue is string strVal && long.TryParse(strVal, out var parsedVal))
                sizeInKB = parsedVal;

            if (sizeInKB <= 0) return null;

            if (sizeInKB < 1024)
                return $"{sizeInKB} KB";
            else if (sizeInKB < 1024 * 1024)
                return $"{sizeInKB / 1024.0:F1} MB";
            else
                return $"{sizeInKB / (1024.0 * 1024.0):F2} GB";
        }
        catch
        {
            return null;
        }
    }
}
