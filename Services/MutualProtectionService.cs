using System;

namespace ClassScreenLock.Services;

public class MutualProtectionService
{
    private static readonly Lazy<MutualProtectionService> _instance = new(() => new MutualProtectionService());
    public static MutualProtectionService Instance => _instance.Value;

    private MutualProtectionService()
    {
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }
}
