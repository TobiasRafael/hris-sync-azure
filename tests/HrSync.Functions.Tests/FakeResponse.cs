using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;

namespace HrSync.Functions.Tests;

// Minimal Azure.Response for building Response<T> values in fakes.
internal sealed class FakeResponse(int status = 200) : Response
{
    public override int Status => status;
    public override string ReasonPhrase => "";
    public override Stream? ContentStream { get; set; }
    public override string ClientRequestId { get; set; } = "";

    public override void Dispose() { }

    protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return false;
    }

    protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
    {
        values = null;
        return false;
    }

    protected override bool ContainsHeader(string name) => false;

    protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
}
