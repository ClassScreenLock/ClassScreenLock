using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace ClassScreenLock.ViewModels;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    private bool _disposed = false;

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
                // 释放托管资源
            }

            _disposed = true;
        }
    }
}