using HrSync.Core.Security;

namespace HrSync.Core.Tests;

public class WebhookSignatureTests
{
    private const string Secret = "test-secret";
    private const string Body = """{"event_id":"evt-1","event_type":"employee.hired","employee_id":"E-1001"}""";

    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly long NowTs = Now.ToUnixTimeSeconds();
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private static bool Verify(string? timestamp, string? signature, string body = Body, string secret = Secret) =>
        WebhookSignature.Verify(secret, timestamp, signature, body, Now, Tolerance);

    [Fact]
    public void Compute_is_prefixed_lowercase_hex_hmac_of_timestamp_dot_body()
    {
        var signature = WebhookSignature.Compute("key", 1, "x");

        Assert.Equal("sha256=".Length + 64, signature.Length);
        Assert.Equal(signature.ToLowerInvariant(), signature);
        Assert.Equal(
            "sha256=" + Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(
                "key"u8.ToArray(), "1.x"u8.ToArray())),
            signature);
    }

    [Fact]
    public void Valid_signature_verifies()
    {
        var signature = WebhookSignature.Compute(Secret, NowTs, Body);
        Assert.True(Verify(NowTs.ToString(), signature));
    }

    [Fact]
    public void Uppercase_hex_still_verifies()
    {
        var signature = WebhookSignature.Compute(Secret, NowTs, Body);
        Assert.True(Verify(NowTs.ToString(), "sha256=" + signature["sha256=".Length..].ToUpperInvariant()));
    }

    [Fact]
    public void Tampered_body_fails()
    {
        var signature = WebhookSignature.Compute(Secret, NowTs, Body);
        Assert.False(Verify(NowTs.ToString(), signature, body: Body.Replace("E-1001", "E-6666")));
    }

    [Fact]
    public void Wrong_secret_fails()
    {
        var signature = WebhookSignature.Compute("other-secret", NowTs, Body);
        Assert.False(Verify(NowTs.ToString(), signature));
    }

    [Fact]
    public void Signature_bound_to_timestamp()
    {
        var signature = WebhookSignature.Compute(Secret, NowTs, Body);
        Assert.False(Verify((NowTs - 1).ToString(), signature));
    }

    [Theory]
    [InlineData(-300)]
    [InlineData(300)]
    public void Timestamp_at_tolerance_edge_verifies(int offsetSeconds)
    {
        var ts = NowTs + offsetSeconds;
        Assert.True(Verify(ts.ToString(), WebhookSignature.Compute(Secret, ts, Body)));
    }

    [Fact]
    public void Expired_timestamp_fails()
    {
        var ts = NowTs - 301;
        Assert.False(Verify(ts.ToString(), WebhookSignature.Compute(Secret, ts, Body)));
    }

    [Fact]
    public void Future_timestamp_beyond_tolerance_fails()
    {
        var ts = NowTs + 301;
        Assert.False(Verify(ts.ToString(), WebhookSignature.Compute(Secret, ts, Body)));
    }

    [Fact]
    public void Default_tolerance_is_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), WebhookSignature.DefaultTolerance);

        var ts = NowTs - 301;
        Assert.False(WebhookSignature.Verify(Secret, ts.ToString(), WebhookSignature.Compute(Secret, ts, Body), Body, Now));
        Assert.True(WebhookSignature.Verify(Secret, NowTs.ToString(), WebhookSignature.Compute(Secret, NowTs, Body), Body, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("+1798880400")]
    [InlineData("-1")]
    [InlineData("99999999999999999999999")]
    public void Malformed_or_missing_timestamp_fails(string? timestamp)
    {
        var signature = WebhookSignature.Compute(Secret, NowTs, Body);
        Assert.False(Verify(timestamp, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=")]
    [InlineData("sha1=abcd")]
    [InlineData("sha256=zz")]
    [InlineData("sha256=abcd")]
    [InlineData("sha256=gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Malformed_or_missing_signature_fails(string? signature)
    {
        Assert.False(Verify(NowTs.ToString(), signature));
    }

    [Fact]
    public void Signature_without_prefix_fails()
    {
        var signature = WebhookSignature.Compute(Secret, NowTs, Body);
        Assert.False(Verify(NowTs.ToString(), signature["sha256=".Length..]));
    }
}
