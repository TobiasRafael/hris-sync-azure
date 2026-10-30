using System.Reflection;
using HrSync.Core.Models;

namespace HrSync.Core.Rules;
public static class CareWorkerMerge
{
    private static readonly PropertyInfo[] Props =
        typeof(CareWorker).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                          .Where(p => p.CanWrite)
                          .ToArray();

    public static CareWorker Apply(CareWorker existing, CareWorker incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        var result = existing with { };
        foreach (var p in Props)
        {
            var value = p.GetValue(incoming);
            if (value is null || value is string s && string.IsNullOrWhiteSpace(s))
                continue;
            p.SetValue(result, value);
        }
        return result;
    }
}