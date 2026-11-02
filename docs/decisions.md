# Design decisions

Lightweight ADRs: the context, what was decided, and what it costs. Paths are relative to the repo root.

- [1. Thin webhook, then fetch the full record](#1-thin-webhook-then-fetch-the-full-record)
- [2. A queue between the receiver and the processor](#2-a-queue-between-the-receiver-and-the-processor)
- [3. Idempotency with a Table Storage claim and ETags](#3-idempotency-with-a-table-storage-claim-and-etags)
- [4. Two levels of retries](#4-two-levels-of-retries)
- [5. Permanent vs transient failures](#5-permanent-vs-transient-failures)
- [6. GET + merge + PUT, never overwrite with empty values](#6-get--merge--put-never-overwrite-with-empty-values)
- [7. Runtime field mapping from config, compile-time DTO mapping](#7-runtime-field-mapping-from-config-compile-time-dto-mapping)
- [8. The mocks don't reference Core](#8-the-mocks-dont-reference-core)
- [9. Aspire and OpenTelemetry instead of direct Application Insights](#9-aspire-and-opentelemetry-instead-of-direct-application-insights)
- [10. Library choices](#10-library-choices)

---

## 1. Thin webhook, then fetch the full record

**Context.** HR systems typically send a small event (`event_id`, `event_type`, `employee_id`, `occurred_at`) rather than the whole employee, and
events can arrive late, twice, or out of order.

**Decision.** The webhook is treated as a notification only. The processor fetches the current employee from the HRIS API
([`IHrisClient`](../src/HrSync.Core/Ports/IHrisClient.cs)) and syncs that.

**Consequences.** The synced data is always the latest version, and a resent or reordered event can't push stale data. It costs
one extra HRIS call per event, and the HRIS must be reachable when the message is processed (handled by the retries in #4).

## 2. A queue between the receiver and the processor

**Context.** Webhook senders expect a quick 2xx and often retry or disable the subscription after timeouts. The downstream
work (HRIS call, care API calls) can be slow or failing.

**Decision.** [`WebhookReceiver`](../HrSync.Functions/Webhooks/WebhookReceiver.cs) only verifies the signature, parses the payload
and returns 202 together with a `NewHireMessage` for the `new-hires` Storage Queue (a queue output binding). It makes no outbound
calls.
[`ProcessNewHire`](../HrSync.Functions/NewHires/ProcessNewHire.cs) consumes the queue.

**Consequences.** The ack is fast and doesn't depend on the target system's health, and a backlog is buffered. Processing is
asynchronous, so the sender can't learn the sync result from the webhook response. Storage Queues give at-least-once delivery,
which is why #3 exists.

## 3. Idempotency with a Table Storage claim and ETags

**Context.** At-least-once delivery, vendor resends and concurrent workers mean the same event can be processed more than once.

**Decision.** [`TableIdempotencyStore`](../HrSync.Functions/Idempotency/TableIdempotencyStore.cs) keeps one row per `EventId` in
table `ProcessedEvents`. A claim is an insert (`AddEntity`; a 409 means someone has it). A `Completed` row means skip. A
`Processing` row younger than 5 minutes means "in progress", so the message is retried later. A stale or `Failed` row is taken over
with an ETag-conditional replace, so only one worker wins. After a transient failure the claim is released (ETag-checked delete).

**Consequences.** No lease service or distributed lock is needed, only the storage account that Functions already uses. It is not
exactly-once: a crash between the upsert and `MarkCompleted` leaves a `Processing` row that is taken over after 5 minutes, and the
upsert runs again. That is safe because the target write is a full-replace PUT keyed by `ExternalId`, so it is idempotent.

## 4. Two levels of retries

**Context.** Most vendor failures are short (a 503 blip, a 429), but some outages last minutes.

**Decision.**
1. In process: the standard resilience handler on each vendor client
   ([`AddHrSyncAdapters`](../src/HrSync.Adapters/DependencyInjection/AdaptersServiceCollectionExtensions.cs)) does 3 retries with
   exponential backoff and jitter, honours `Retry-After`, and has a 10 s per-attempt and 30 s total timeout.
2. Queue: if the call still fails, the exception propagates, the message becomes visible again after 30 s, and it is retried up to
   5 dequeues ([`host.json`](../HrSync.Functions/host.json)) before the runtime moves it to `new-hires-poison`.

**Consequences.** Blips are absorbed within one invocation, and longer outages are covered by the queue without holding a worker.
Host-wide default resilience (Aspire ServiceDefaults) is removed from the vendor clients, so retries don't multiply (4 × 4); a
test guards this.

## 5. Permanent vs transient failures

**Context.** Retrying a 400 or a missing employee only delays the error and fills the logs.

**Decision.** [`VendorErrors`](../src/HrSync.Adapters/Http/VendorErrors.cs) maps every vendor failure to one of two Core
exceptions. A 4xx (except 408/429), malformed JSON, or an employee that doesn't exist is a `PermanentIntegrationException`. A 5xx,
408, 429, network error, timeout or open circuit is a `TransientIntegrationException`. On a permanent failure, `ProcessNewHire`
writes a `PoisonEnvelope` (original message, reason, time) to `new-hires-poison`, marks the event `Failed` and completes the
message. Transient failures are rethrown for the queue to retry.

**Consequences.** Bad data surfaces in seconds with a readable reason instead of after 5 dequeues. The poison queue holds two
shapes: an envelope (permanent) and the raw message (retries exhausted). Replay is manual for now.

## 6. GET + merge + PUT, never overwrite with empty values

**Context.** The target API's PUT is a full replace. HRIS records are often sparse (no phone yet, empty work email), and a naive
sync would wipe data entered in the target system.

**Decision.** The processor reads the existing worker, then [`CareWorkerMerge`](../src/HrSync.Core/Rules/CareWorkerMerge.cs)
applies the incoming values. A non-null, non-blank incoming value wins; null or whitespace never overwrites. The merge reflects
over all writable `CareWorker` properties, so new fields are covered automatically.

**Consequences.** Sparse updates are safe. A field can't be cleared through this sync on purpose; that would need an explicit rule.
It costs one extra GET per event, and there is a small race window between the GET and the PUT if someone edits the worker at the
same moment.

## 7. Runtime field mapping from config, compile-time DTO mapping

**Context.** Which HRIS field feeds which target field differs per client and changes over time. The vendor DTO shapes are
fixed per vendor.

**Decision.** There are two mapping layers.
- Vendor DTO ↔ Core model: [Riok.Mapperly](https://github.com/riok/mapperly) source generators
  ([`HrisMapper`](../src/HrSync.Adapters/Hris/HrisMapper.cs), [`CareMapper`](../src/HrSync.Adapters/Care/CareMapper.cs)).
  These are compile-time checked and have no reflection cost.
- `Employee` → `CareWorker`: [`FieldMapper`](../src/HrSync.Core/Mappings/FieldMapper.cs), driven by `FieldMapping:Fields`
  (target → source) and validated by reflection when the host starts. Empty config, unknown properties, type mismatches, or
  mapping `ExternalId` stop the host.

**Consequences.** Remapping a field is a config change, not a deploy. Mapping is limited to same-type property copies; a
transformation such as splitting a name needs code.

## 8. The mocks don't reference Core

**Context.** [`HrSync.MockApis`](../src/HrSync.MockApis) stands in for third-party systems during development and demos.

**Decision.** The mocks have their own DTOs, snake_case JSON and their own copy of the HMAC signing code. They reference only
`HrSync.ServiceDefaults` for telemetry and health checks.

**Consequences.** A change to a Core model can't silently change what the "vendor" sends, so contract drift shows up as a failing
adapter, as it would with a real vendor. The cost is a small amount of duplicated code, such as the signature function.

## 9. Aspire and OpenTelemetry instead of direct Application Insights

**Context.** Local development needs storage, two services and a shared secret wired together, and a webhook → queue → worker
flow is hard to debug from separate logs.

**Decision.** An Aspire [AppHost](../src/HrSync.AppHost/AppHost.cs) runs Azurite, the mocks and the Functions host, with service
discovery and a secret parameter. [ServiceDefaults](../src/HrSync.ServiceDefaults/Extensions.cs) export OpenTelemetry over OTLP.
The receiver puts its W3C `traceparent` in the queue message, and the processor continues that trace and links the trigger
invocation span ([`HrSyncTelemetry`](../src/HrSync.Core/Diagnostics/HrSyncTelemetry.cs)).

**Consequences.** One command starts everything, and one trace shows the webhook, the queue hop, each pipeline step and every
vendor call. Exporting to Azure Monitor in the cloud means adding an exporter or collector; that isn't wired yet (see the roadmap).

## 10. Library choices

**Decision.**
- **Refit** for vendor clients, as typed interfaces with source-generated implementations (`AddRefitGeneratedClient`).
- **Riok.Mapperly** for DTO mapping: source generators, no runtime reflection, MIT-licensed.
- **Microsoft.Extensions.Http.Resilience** (Polly v8) for retries, timeouts and circuit breaking.
- Tests: **xUnit** with plain `Assert`, **WireMock.Net** (resilience tested over real HTTP), **Testcontainers.Azurite**,
  `FakeTimeProvider`, and **Aspire.Hosting.Testing** for E2E.

**Avoided.** AutoMapper and MediatR moved to commercial licensing, and FluentAssertions 8 requires a paid license for commercial use.
For a template meant to be reused in client projects, every dependency should be permissively licensed. Moq is avoided as well;
hand-written fakes nested in the test classes are enough here.

**Consequences.** The dependency set is small and permissively licensed. Assertions are a little more verbose than
FluentAssertions.
