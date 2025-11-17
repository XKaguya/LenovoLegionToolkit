using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class TaskExtensions
{
    public static ValueTask AsValueTask(this Task task) => new(task);

    public static async Task<T?> OrNullIfException<T>(this Task<T> task) where T : struct
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            // Catch the exception to avoid rethrow to be Unobserved Exception.
            return null;
        }
    }
}
