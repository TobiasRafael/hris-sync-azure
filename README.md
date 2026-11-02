# hris-sync-azure

[![CI](https://github.com/TobiasRafael/hris-sync-azure/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/TobiasRafael/hris-sync-azure/actions/workflows/ci.yml)

Webhook-driven sync of new hires from an HRIS into a care/scheduling platform, on Azure Functions (.NET 10).

## CI

Every push and pull request to `main` builds in Release on `ubuntu-latest` and runs all tests except the
local-only E2E category (it needs Azure Functions Core Tools). Integration tests run against Azurite via
Testcontainers. Same command locally:

```bash
dotnet test HrSync.slnx -c Release --filter "Category!=E2E"
```
