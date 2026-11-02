using HrSync.Core.Models;
using Riok.Mapperly.Abstractions;

namespace HrSync.Adapters.Hris;

// Source-generated vendor DTO -> Core mapping. (FieldMapper is different: runtime, config-driven, in Core.)
[Mapper]
public static partial class HrisMapper
{
    [MapProperty(nameof(HrisEmployeeDto.EmployeeId), nameof(Employee.Id))]
    public static partial Employee ToEmployee(HrisEmployeeDto dto);
}
