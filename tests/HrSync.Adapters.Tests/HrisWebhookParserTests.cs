using HrSync.Adapters.Hris;

namespace HrSync.Adapters.Tests;

public class HrisWebhookParserTests
{
    private const string HirePayload = """
        {
          "event_id": "0b6f7c3e-2a51-4c1e-9d55-7f1d2f0c9a10",
          "event_type": "employee.hired",
          "employee_id": "E-1001",
          "occurred_at": "2026-11-02T08:00:00Z"
        }
        """;

    [Fact]
    public void Hire_event_maps_to_new_hire_event()
    {
        var result = HrisWebhookParser.Parse(HirePayload);

        Assert.Equal(WebhookParseStatus.Hire, result.Status);
        Assert.NotNull(result.Event);
        Assert.Equal("0b6f7c3e-2a51-4c1e-9d55-7f1d2f0c9a10", result.Event.EventId);
        Assert.Equal("E-1001", result.Event.EmployeeId);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero), result.Event.OccurredAt);
        Assert.Equal("employee.hired", result.EventType);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        var result = HrisWebhookParser.Parse(HirePayload.Replace("\"event_id\"", "\"tenant\": \"t-1\", \"event_id\""));
        Assert.Equal(WebhookParseStatus.Hire, result.Status);
    }

    [Theory]
    [InlineData("employee.updated")]
    [InlineData("employee.terminated")]
    [InlineData("EMPLOYEE.HIRED")]
    public void Other_event_types_are_ignored_not_invalid(string eventType)
    {
        var result = HrisWebhookParser.Parse(HirePayload.Replace("employee.hired", eventType));

        Assert.Equal(WebhookParseStatus.Ignored, result.Status);
        Assert.Null(result.Event);
        Assert.Equal(eventType, result.EventType);
        Assert.Equal("0b6f7c3e-2a51-4c1e-9d55-7f1d2f0c9a10", result.EventId);
    }

    [Fact]
    public void Non_hire_event_needs_no_employee_fields()
    {
        var result = HrisWebhookParser.Parse("""{"event_id":"evt-9","event_type":"payroll.closed"}""");
        Assert.Equal(WebhookParseStatus.Ignored, result.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public void Unparseable_payload_is_invalid(string body)
    {
        var result = HrisWebhookParser.Parse(body);

        Assert.Equal(WebhookParseStatus.Invalid, result.Status);
        Assert.Null(result.Event);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("\"event_id\": \"0b6f7c3e-2a51-4c1e-9d55-7f1d2f0c9a10\",", "")]
    [InlineData("\"event_id\": \"0b6f7c3e-2a51-4c1e-9d55-7f1d2f0c9a10\"", "\"event_id\": \"  \"")]
    [InlineData("\"event_type\": \"employee.hired\",", "")]
    [InlineData("\"employee_id\": \"E-1001\",", "")]
    [InlineData("\"employee_id\": \"E-1001\"", "\"employee_id\": \"\"")]
    [InlineData(",\n  \"occurred_at\": \"2026-11-02T08:00:00Z\"", "")]
    [InlineData("\"2026-11-02T08:00:00Z\"", "\"yesterday\"")]
    [InlineData("\"E-1001\"", "1001")]
    public void Missing_or_malformed_fields_are_invalid(string find, string replace)
    {
        var body = HirePayload.ReplaceLineEndings("\n");
        Assert.Contains(find, body);

        var result = HrisWebhookParser.Parse(body.Replace(find, replace));

        Assert.Equal(WebhookParseStatus.Invalid, result.Status);
        Assert.Null(result.Event);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
