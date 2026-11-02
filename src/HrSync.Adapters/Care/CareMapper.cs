using HrSync.Core.Models;
using Riok.Mapperly.Abstractions;

namespace HrSync.Adapters.Care;

// Source-generated vendor DTO <-> Core mapping; property names match one to one.
[Mapper]
public static partial class CareMapper
{
    public static partial CareWorker ToCareWorker(CareWorkerDto dto);

    public static partial CareWorkerDto ToDto(CareWorker worker);
}
