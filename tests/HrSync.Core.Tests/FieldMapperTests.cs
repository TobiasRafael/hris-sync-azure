using HrSync.Core.Mapping;
using HrSync.Core.Mappings;
using HrSync.Core.Models;

namespace HrSync.Core.Tests
{
    public class FieldMapperTests
    {
        private static readonly Employee Alice = new()
        {
            Id = "E-1001",
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@contoso.test",
            PersonalEmail = "alice@home.test",
            MobilePhone = "+1-555-0100",
            JobTitle = "Caregiver",
            HireDate = new DateOnly(2026, 11, 2),
        };

        private static FieldMappingOptions DefaultOptions() => new()
        {
            Fields = new()
            {
                ["GivenName"] = "FirstName",
                ["FamilyName"] = "LastName",
                ["Email"] = "WorkEmail",
                ["Phone"] = "MobilePhone",
                ["Role"] = "JobTitle",
                ["StartDate"] = "HireDate",
            }
        };

        [Fact]
        public void Map_copies_configured_fields()
        {
            var worker = new FieldMapper(DefaultOptions()).Map(Alice);

            Assert.Equal("Alice", worker.GivenName);
            Assert.Equal("Smith", worker.FamilyName);
            Assert.Equal("alice@contoso.test", worker.Email);
            Assert.Equal("+1-555-0100", worker.Phone);
            Assert.Equal("Caregiver", worker.Role);
            Assert.Equal(new DateOnly(2026, 11, 2), worker.StartDate);
        }

        [Fact]
        public void Map_always_uses_employee_id_as_external_id()
        {
            var worker = new FieldMapper(DefaultOptions()).Map(Alice);
            Assert.Equal("E-1001", worker.ExternalId);
        }

        [Fact]
        public void Unmapped_fields_stay_null()
        {
            var options = new FieldMappingOptions { Fields = new() { ["GivenName"] = "FirstName" } };
            var worker = new FieldMapper(options).Map(Alice);

            Assert.Equal("Alice", worker.GivenName);
            Assert.Null(worker.Email);
            Assert.Null(worker.StartDate);
        }

        [Fact]
        public void Changing_a_source_field_is_config_only()
        {
            var options = DefaultOptions();
            options.Fields["Email"] = "PersonalEmail";

            var worker = new FieldMapper(options).Map(Alice);
            Assert.Equal("alice@home.test", worker.Email);
        }

        [Fact]
        public void Empty_config_fails_fast()
        {
            Assert.Throws<InvalidOperationException>(() => new FieldMapper(new FieldMappingOptions()));
        }

        [Theory]
        [InlineData("Nickname", "FirstName")]   // unknown target field
        [InlineData("GivenName", "MiddleName")] // unknown source field
        [InlineData("StartDate", "FirstName")]  // type mismatch
        [InlineData("ExternalId", "WorkEmail")] // upsert key is not configurable
        public void Invalid_config_fails_fast(string target, string source)
        {
            var options = new FieldMappingOptions { Fields = new() { [target] = source } };
            Assert.Throws<InvalidOperationException>(() => new FieldMapper(options));
        }
    }
}
