# Nexus

A digital assistant ingestion service for [OpenClaw](https://github.com/openclaw/openclaw) agents — email, calendar, and meeting data from Microsoft Graph and Fireflies.ai.

## What It Does

Nexus ingests data from your Microsoft 365 environment and meeting transcription services, stages it in Azure Table Storage, and makes it available for an OpenClaw agent to sync into its workspace. The agent handles the intelligence — Nexus just makes sure the data is there.

**Sources:**
- 📧 **Email** — Inbox and sent items via Microsoft Graph webhooks
- 📅 **Calendar** — Events (create, update, cancel) via Microsoft Graph webhooks
- 🎤 **Meetings** — Transcripts, summaries, and action items via Fireflies.ai webhooks

**Key Features:**
- **Smart email filtering** — Domain whitelist with auto-population from outbound emails and meeting participants. No newsletter noise.
- **Queue-based processing** — Thin HTTP endpoints enqueue; queue-triggered functions do the heavy lifting. Fast, resilient, independently scalable.
- **Deduplication** — Stores only new email content (`uniqueBody`), full thread history available in blob storage on demand.
- **Meeting intelligence** — Summaries and action items in table storage, full transcripts in blob storage. Fractions of a cent per month.
- **Zero data loss** — Non-whitelisted emails are parked (not dropped). When a domain gets whitelisted, historical emails are automatically promoted.

## Architecture

```
Microsoft Graph ──webhook──▶ /api/notifications ──▶ email-ingest queue ──▶ EmailProcessor
                                                  ──▶ calendar-ingest queue ──▶ CalendarProcessor

Fireflies.ai ──webhook──▶ /api/fireflies ──▶ meeting-ingest queue ──▶ MeetingProcessor

                                        ┌─────────────────┐
                                        │  Table Storage   │
All processors write to ───────────────▶│  Items           │◀── Agent syncs (pending → synced)
                                        │  PendingEmails   │
                                        │  WhitelistedDomains │
                                        └─────────────────┘
                                        ┌─────────────────┐
Full content stored in ────────────────▶│  Blob Storage    │◀── Agent fetches on demand
                                        │  email-bodies/   │
                                        │  transcripts/    │
                                        └─────────────────┘
```

## Tech Stack

- **Runtime:** C# .NET 8, Azure Functions v4 (isolated worker model)
- **Storage:** Azure Table Storage (staging) + Azure Blob Storage (full content)
- **APIs:** Microsoft Graph SDK, Fireflies.ai GraphQL
- **Auth:** Azure AD app-only (client credentials), Fireflies bearer token

## Status

🚧 **Under development** — Not ready for production use yet.

## License

MIT
