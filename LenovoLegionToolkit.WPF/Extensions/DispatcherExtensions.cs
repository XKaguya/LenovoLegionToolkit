using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace LenovoLegionToolkit.WPF.Extensions;

public static class DispatcherExtensions
{
    public static void InvokeTask(this Dispatcher dispatcher, Func<Task> action) => dispatcher.Invoke(async () => await action());

    public static async Task InvokeIfRequired(this DispatcherObject dispatcherObject, Action action)
    {
        if (dispatcherObject.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            await dispatcherObject.Dispatcher.InvokeAsync(action);
        }
    }

    public static async Task<T> InvokeIfRequired<T>(this DispatcherObject dispatcherObject, Func<T> func)
    {
        if (dispatcherObject.Dispatcher.CheckAccess())
        {
            return func();
        }
        else
        {
            return await dispatcherObject.Dispatcher.InvokeAsync(func);
        }
    }
}
