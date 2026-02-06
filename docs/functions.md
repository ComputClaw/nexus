# Nexus Functions

Documentation for all Azure Functions in the Nexus ingestion service.

## Entry Point Functions

These functions receive data from external sources:

| Function | Purpose | Documentation |
|----------|---------|---------------|
| **GraphNotificationFunction** | Microsoft Graph webhooks (email/calendar) | [📄](graph-notification-function.md) |
| **FirefliesWebhookFunction** | Fireflies.ai meeting transcripts | [📄](fireflies-webhook-function.md) |
| **SessionsFunction** | OpenClaw session transcripts | [📄](sessions-function.md) |

## Processing Functions

These functions handle queued items asynchronously:

| Function | Purpose | Triggered By |
|----------|---------|--------------|
| **EmailProcessorFunction** | Process email notifications | email-ingest queue |
| **CalendarProcessorFunction** | Process calendar events | calendar-ingest queue |
| **MeetingProcessorFunction** | Process meeting transcripts | meeting-ingest queue |

## API Functions

These functions provide data access for agents:

| Function | Purpose | Documentation |
|----------|---------|---------------|
| **ItemsFunction** | List, fetch, and delete processed items | [📄](items-function.md) |
| **WhitelistFunction** | Manage email filtering whitelist | [📄](whitelist-function.md) |

## Support Functions

These functions handle infrastructure and lifecycle:

| Function | Purpose |
|----------|---------|
| **SubscriptionBootstrapFunction** | Initialize Microsoft Graph subscriptions |
| **SubscriptionTimerFunction** | Renew Graph subscriptions automatically |
| **LifecycleNotificationFunction** | Handle subscription lifecycle events |
| **RepairFunction** | Utility functions for data repair |
| **PoisonQueueHandlers** | Handle failed queue messages |

## Authentication

All functions use:
- **Function Key** — Azure-managed key as `code` query parameter
- **Application Key** — Optional `X-Api-Key` header for additional security

## Data Flow

```
External Source → Entry Point Function → Queue → Processing Function → Table/Blob Storage
                                                                              ↓
Agent ← API Function ←──────────────────────────────────────────────────────────
```