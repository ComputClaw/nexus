# Nexus

A digital assistant ingestion service for [OpenClaw](https://github.com/openclaw/openclaw) agents — email, calendar, and meeting data from Microsoft Graph and Fireflies.ai.

## What It Does

Nexus ingests data from your Microsoft 365 environment and meeting transcription services, stages it in Azure Table Storage, and exposes a simple Items API for an agent to consume. The agent handles the intelligence — Nexus just makes sure the data is there.

**Sources:**
- 📧 **Email** — Inbox and sent items via Microsoft Graph webhooks
- 📅 **Calendar** — Events (create, update, cancel) via Microsoft Graph webhooks
- 🎤 **Meetings** — Transcripts, summaries, and action items via Fireflies.ai webhooks

**Key Features:**
- **Smart email filtering** — Dual-level whitelist (manual domain entries + auto-populated email addresses from outbound mail, calendar events, and meetings). No newsletter noise.
- **Queue-based processing** — Thin HTTP endpoints enqueue; queue-triggered functions do the heavy lifting. Fast, resilient, independently scalable.
- **Deduplication** — Stores only new email content (`uniqueBody`), full thread history available in blob storage on demand.
- **Meeting intelligence** — Summaries and action items in table storage, full transcripts in blob storage. Fractions of a cent per month.
- **Zero data loss** — Non-whitelisted emails are parked (not dropped). When a sender gets whitelisted, historical emails are automatically promoted.
- **Sync consumer** — A lightweight Node.js script pulls pending items, writes them as markdown, and deletes them from the backend. The agent processes at its own pace.

## Architecture

```
Microsoft Graph ──webhook──▶ /api/notifications ──▶ email-ingest queue ──▶ EmailProcessor
                                                  ──▶ calendar-ingest queue ──▶ CalendarProcessor

Fireflies.ai ──webhook──▶ /api/fireflies ──▶ meeting-ingest queue ──▶ MeetingProcessor

                                        ┌─────────────────┐
                                        │  Table Storage   │
All processors write to ───────────────▶│  Items           │◀── GET /api/items (list pending)
                                        │  PendingEmails   │    DELETE /api/items (after sync)
                                        │  Whitelist       │    GET /api/items/body (full content)
                                        │  Subscriptions   │
                                        └─────────────────┘
                                        ┌─────────────────┐
Full content stored in ────────────────▶│  Blob Storage    │◀── Agent fetches on demand
                                        │  email-bodies/   │
                                        │  transcripts/    │
                                        └─────────────────┘

Agent (sync consumer) ──── GET items ──▶ write markdown ──▶ DELETE items ──▶ process locally
```

**Whitelist model:**
- `domain` partition — manually added via API (e.g., "example.com")
- `email` partition — auto-populated from outbound emails (TO + CC recipients; use BCC to avoid), calendar attendees, and meeting participants
- Inbound check: sender's full email OR sender's domain — either match passes
- Non-whitelisted inbound emails parked in `PendingEmails`, promoted when the sender gets whitelisted

## Tech Stack

- **Runtime:** C# .NET 8, Azure Functions v4 (isolated worker model)
- **Storage:** Azure Table Storage (staging) + Azure Blob Storage (full content)
- **APIs:** Microsoft Graph SDK, Fireflies.ai GraphQL
- **Auth:** Azure AD app-only (client credentials), Fireflies bearer token
- **Infrastructure:** Azure Function App (`nexusassistant`), dedicated storage account (`nexusassistantstorage`)
- **Sync consumer:** Node.js script (zero dependencies) — see [`scripts/nexus-sync.js`](scripts/nexus-sync.js)

## Status

✅ **In production** — email and calendar ingestion live. Meeting ingestion (Fireflies) and outbound email pending.

## Documentation

- **[API Reference](docs/api-reference.md)** — All endpoints, auth model, table schemas, and whitelist logic
- **[Sync Consumer](docs/sync-consumer.md)** — Agent-side sync script, directory layout, processing workflow, and cron schedules

## Project Structure

```
nexus/
├── src/Nexus.Ingest/       # Azure Functions app (C# .NET 8)
├── scripts/
│   ├── nexus-sync.js       # Sync consumer script
│   └── .nexus-config.example.json
├── docs/
│   ├── api-reference.md    # Full API documentation
│   └── sync-consumer.md    # Agent-side integration guide
├── nexus.sln
└── README.md
```

## Getting Started

### Backend (Azure Functions)

1. Clone the repo
2. Copy `src/Nexus.Ingest/local.settings.example.json` to `local.settings.json` and fill in your credentials
3. Run with `func start` or deploy to Azure

### Sync Consumer

1. Copy `scripts/.nexus-config.example.json` to `scripts/.nexus-config.json`
2. Add your Function App URL and keys
3. Run: `node scripts/nexus-sync.js --with-body`

See [docs/sync-consumer.md](docs/sync-consumer.md) for full details.

## License

MIT
