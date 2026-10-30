using HrSync.Core.Mapping;
using HrSync.Core.Mappings;
using Microsoft.Extensions.Options;

namespace HrSync.Functions.NewHires;

// Reuses FieldMapper's own fail-fast checks so a bad FieldMapping config stops the host at startup.
public sealed class FieldMappingValidator : IValidateOptions<FieldMappingOptions>
{
    public ValidateOptionsResult Validate(string? name, FieldMappingOptions options)
    {
        try
        {
            _ = new FieldMapper(options);
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail($"FieldMapping config is invalid: {ex.Message}");
        }
    }
}
