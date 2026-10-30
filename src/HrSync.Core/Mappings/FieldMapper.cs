using HrSync.Core.Mappings;
using HrSync.Core.Models;
using System.Reflection;

namespace HrSync.Core.Mapping;
public sealed class FieldMapper
{
    private readonly IReadOnlyList<(PropertyInfo Source, PropertyInfo Target)> _maps;

    public FieldMapper(FieldMappingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Fields.Count == 0)
            throw new InvalidOperationException("Field mapping config is empty.");

        var maps = new List<(PropertyInfo, PropertyInfo)>();
        foreach (var (targetName, sourceName) in options.Fields)
        {
            if (targetName == nameof(CareWorker.ExternalId))
                throw new InvalidOperationException("ExternalId is the upsert key and is always Employee.Id; it cannot be mapped.");

            var target = typeof(CareWorker).GetProperty(targetName)
                ?? throw new InvalidOperationException($"Unknown CareWorker field '{targetName}'.");
            var source = typeof(Employee).GetProperty(sourceName)
                ?? throw new InvalidOperationException($"Unknown Employee field '{sourceName}'.");

            if (source.PropertyType != target.PropertyType)
                throw new InvalidOperationException(
                    $"Type mismatch: Employee.{sourceName} ({source.PropertyType.Name}) -> CareWorker.{targetName} ({target.PropertyType.Name}).");

            maps.Add((source, target));
        }
        _maps = maps;
    }

    public CareWorker Map(Employee employee)
    {
        ArgumentNullException.ThrowIfNull(employee);

        var worker = new CareWorker { ExternalId = employee.Id };
        foreach (var (source, target) in _maps)
            target.SetValue(worker, source.GetValue(employee));

        return worker;
    }
}