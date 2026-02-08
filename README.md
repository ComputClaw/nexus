# Nexus

Webhook relay service for OpenClaw agents. Receives webhooks from external systems, enriches data, and stores for agent consumption.

## Architecture

```
External Systems                 Nexus (Azure Functions)              Agents
─────────────────               ─────────────────────────            ────────
Microsoft Graph ──────────────▶ POST /api/webhook/{agent}/graph/email
                                POST /api/webhook/{agent}/graph/calendar
                                                                      
put.io          ──────────────▶ POST /api/webhook/{agent}/putio/download
                                                                      
Fireflies       ──────────────▶ POST /api/webhook/{agent}/fireflies/meeting
                                        │
                                        ▼
                                ┌───────────────┐
                                │ Items Table   │◀──── Agents poll here
                                │ (+ Blob refs) │
                                └───────────────┘
```

## Endpoint

```
POST /api/webhook/{agentName}/{source}/{type}?code={functionKey}
```

**Path parameters:**
- `agentName` — Target agent (e.g., `stewardclaw`, `flickclaw`)
- `source` — External system (e.g., `graph`, `putio`, `fireflies`)
- `type` — Data type (e.g., `email`, `calendar`, `download`, `meeting`)

**Supported combinations:**
| Source | Type | Description |
|--------|------|-------------|
| `graph` | `email` | Microsoft Graph inbox/sent items |
| `graph` | `calendar` | Microsoft Graph calendar events |
| `putio` | `download` | put.io download completions |
| `fireflies` | `meeting` | Meeting transcripts |

## Storage

**Items Table** — Structured metadata with agent routing:
- `PartitionKey` — Type (e.g., `email`, `calendar`, `download`)
- `RowKey` — Unique item ID
- `AgentName` — Target agent
- `SourceType` — e.g., `graph-email`, `putio-download`
- Type-specific fields (subject, from, to, etc.)

**Blob Storage** — Large content (email bodies, transcripts)

## Infrastructure

- **Function App:** `nexusassistant.azurewebsites.net`
- **Storage:** `nexusassistantstorage`
- **Resource Group:** `OpenClaw`
- **Region:** West Europe

## Graph Subscriptions

Graph webhooks require active subscriptions. The `SubscriptionRenewal` timer runs daily at 08:00 UTC to renew tracked subscriptions before expiry.

Subscriptions are stored in the `Subscriptions` table for lifecycle management.

## Deployment

Push to `main` triggers GitHub Actions deployment.

```bash
git push origin main
```

Or trigger manually via workflow dispatch.

## License

MIT
