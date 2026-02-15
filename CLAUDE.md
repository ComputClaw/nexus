# Claude Code Instructions

> **Memory**: Save project learnings, patterns, and decisions to `memory.md` in the repo root.
> This file is checked into git so context persists across sessions and machines.

## Project

Nexus is an Azure Functions (C# / .NET 8 / isolated worker) webhook relay service.
It receives webhooks from external systems, processes data, and stores structured items for agent consumption.

## Architecture

```
POST /api/webhook/{agentName}/{source}/{type}?code={functionKey}
     │
     ▼
WebhookRelayFunction  ──►  Source-specific relay (Graph, GitHub, Fireflies, Generic)
     │                          validates signature, builds WebhookMessage
     ▼
Azure Queue ("webhook-messages")
     │
     ▼
WebhookProcessorFunction  ──►  Type-specific processor (Email, Calendar, Meeting, Release, Generic)
     │                              fetches full data, writes to Items table + Blob storage
     ▼
Azure Table Storage (Items) + Blob Storage
```

### Domain folders under `src/`

| Folder | Purpose |
|---|---|
| `Webhooks/Relays/` | HTTP-triggered: receive, validate, queue |
| `Webhooks/Processors/` | Queue-triggered: process, store |
| `Feeds/` | Atom/RSS feed polling + management |
| `Graph/` | Microsoft Graph subscriptions (email/calendar) |
| `Whitelist/` | Sender whitelist for email filtering |
| `Services/` | Shared: blob storage, ingestion, queue factory, Fireflies API |
| `Models/` | Data models |
| `Helpers/` | Utilities |

## Infrastructure

- **Azure Function App**: `nexusrelay` (Linux, .NET 8 isolated, Consumption plan)
- **Custom domain**: `nexus.comput.sh` (Cloudflare DNS, Azure managed SSL)
- **Storage**: Azure Table Storage + Blob Storage (connection via `AzureWebJobsStorage`)
- **Deployment**: Push to `main` triggers `.github/workflows/deploy.yml`
- **Git remote**: `https://github.com/mbundgaard/nexus.git` (redirects to `ComputClaw/nexus`)

## Function Keys

Named keys per external service (manage via `az functionapp keys`):

| Key | Service |
|---|---|
| `graph` | Microsoft Graph notification callbacks |
| `github` | GitHub webhook deliveries |
| `fireflies` | Fireflies webhook deliveries |
| `default` | Fallback |

## App Settings (Azure)

| Setting | Purpose |
|---|---|
| `AzureWebJobsStorage` | Storage connection string |
| `Graph__TenantId` | Azure AD tenant |
| `Graph__ClientId` | NexusClaw app registration |
| `Graph__ClientSecret` | App secret |
| `Graph__UserId` | Target user (mb@muneris.dk) |
| `Graph__ClientState` | Shared secret for Graph notification validation |
| `Nexus__PublicBaseUrl` | Public URL for callbacks (https://nexus.comput.sh) |
| `FunctionKey` | Graph function key (used by SubscriptionService for callback URLs) |

## Graph Subscriptions

- Managed by `SubscriptionService`, tracked in `Subscriptions` table
- `SubscriptionRenewal` timer runs daily at 08:00 UTC to renew/recreate
- `Create()` takes `agentName` and `webhookType` to build correct callback URLs
- Lifecycle events handled by `LifecycleNotificationFunction` at `/api/lifecycle`

## Conventions

- Agent names are free-form (no hardcoded validation)
- Source/type validation comes from registered `IWebhookRelay` implementations
- Webhook signature validation is optional per source (skipped if secret not configured)
- Use `dotnet build src/Nexus.Ingest.csproj` to build locally
- Shell is bash on Windows (use Unix syntax, not PowerShell)
