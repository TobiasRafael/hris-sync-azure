using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;

namespace HrSync.Functions.Tests;

// In-memory table with real ETag semantics: 409 on duplicate add, 412 on ETag mismatch, 404 when missing.
// Only TableEntity is supported, which is all TableIdempotencyStore uses.
internal sealed class FakeTableClient : TableClient
{
    private readonly Dictionary<(string Pk, string Rk), TableEntity> _rows = new();
    private int _etag;

    public int CreateCalls { get; private set; }

    // Runs right before an UpdateEntityAsync is applied; lets a test simulate a concurrent writer.
    public Action? BeforeUpdate { get; set; }

    public TableEntity? Row(string pk, string rk) => _rows.TryGetValue((pk, rk), out var e) ? Clone(e) : null;

    public void Put(TableEntity entity) => _rows[(entity.PartitionKey, entity.RowKey)] = Stamp(entity);

    // Changes the stored row's ETag, as if another worker had written it.
    public void Touch(string pk, string rk) => _rows[(pk, rk)] = Stamp(_rows[(pk, rk)]);

    public override Task<Response<TableItem>> CreateIfNotExistsAsync(CancellationToken cancellationToken = default)
    {
        CreateCalls++;
        return Task.FromResult(Response.FromValue(TableModelFactory.TableItem("fake"), new FakeResponse()));
    }

    public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
    {
        var e = (TableEntity)(object)entity;
        if (_rows.ContainsKey((e.PartitionKey, e.RowKey)))
            throw new RequestFailedException(409, "EntityAlreadyExists");
        _rows[(e.PartitionKey, e.RowKey)] = Stamp(e);
        return Task.FromResult<Response>(new FakeResponse(204));
    }

    public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(
        string partitionKey, string rowKey, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
    {
        NullableResponse<T> response = _rows.TryGetValue((partitionKey, rowKey), out var e)
            ? Response.FromValue((T)(object)Clone(e), new FakeResponse())
            : new NoValue<T>();
        return Task.FromResult(response);
    }

    public override Task<Response> UpdateEntityAsync<T>(
        T entity, ETag ifMatch, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
    {
        BeforeUpdate?.Invoke();
        var e = (TableEntity)(object)entity;
        if (!_rows.TryGetValue((e.PartitionKey, e.RowKey), out var current))
            throw new RequestFailedException(404, "ResourceNotFound");
        if (ifMatch != ETag.All && ifMatch != current.ETag)
            throw new RequestFailedException(412, "UpdateConditionNotSatisfied");
        Write(e, mode);
        return Task.FromResult<Response>(new FakeResponse(204));
    }

    public override Task<Response> UpsertEntityAsync<T>(
        T entity, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
    {
        Write((TableEntity)(object)entity, mode);
        return Task.FromResult<Response>(new FakeResponse(204));
    }

    public override Task<Response> DeleteEntityAsync(
        string partitionKey, string rowKey, ETag ifMatch = default, CancellationToken cancellationToken = default)
    {
        if (!_rows.TryGetValue((partitionKey, rowKey), out var current))
            throw new RequestFailedException(404, "ResourceNotFound");
        if (ifMatch != default && ifMatch != ETag.All && ifMatch != current.ETag)
            throw new RequestFailedException(412, "UpdateConditionNotSatisfied");
        _rows.Remove((partitionKey, rowKey));
        return Task.FromResult<Response>(new FakeResponse(204));
    }

    private void Write(TableEntity e, TableUpdateMode mode)
    {
        var key = (e.PartitionKey, e.RowKey);
        if (mode == TableUpdateMode.Merge && _rows.TryGetValue(key, out var current))
        {
            var merged = Clone(current);
            foreach (var (k, v) in e)
                if (k is not ("odata.etag" or "Timestamp"))
                    merged[k] = v;
            _rows[key] = Stamp(merged);
        }
        else
        {
            _rows[key] = Stamp(e);
        }
    }

    private TableEntity Stamp(TableEntity e)
    {
        var copy = Clone(e);
        copy.ETag = new ETag($"W/\"{++_etag}\"");
        return copy;
    }

    private static TableEntity Clone(TableEntity e) => new(e) { ETag = e.ETag };

    private sealed class NoValue<T> : NullableResponse<T>
    {
        public override bool HasValue => false;
        public override T? Value => default;
        public override Response GetRawResponse() => new FakeResponse(404);
    }
}
