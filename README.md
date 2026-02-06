# Nexus

Data ingestion service for [OpenClaw](https://github.com/openclaw/openclaw) agents.

## What It Does

Nexus ingests data from external services and stores it for agent consumption. Sources include email, calendar, meetings, webhooks, and session transcripts.

**Current Sources:**
- 📧 **Email & Calendar** — Microsoft Graph webhooks
- 🎤 **Meetings** — Fireflies.ai transcripts and summaries  
- 🔗 **Webhooks** — Generic endpoint for external services
- 📝 **Sessions** — OpenClaw session transcripts for analytics

## Architecture

```
External Services ──webhook──▶ Azure Functions ──▶ Table Storage ──▶ Agents
                                                 ──▶ Blob Storage
```

**Components:**
- **HTTP endpoints** — Receive webhooks and data from external sources
- **Queue processing** — Async processing for reliability and scale
- **Table Storage** — Structured data and metadata
- **Blob Storage** — Full content (transcripts, email bodies)
- **Local worker** — Delivers webhook items to agent inboxes

## Tech Stack

- **Runtime:** C# .NET 8, Azure Functions v4
- **Storage:** Azure Table Storage + Blob Storage
- **Worker:** Python (local process on OpenClaw host)

## Status

✅ **Email/Calendar** — Live  
📝 **Sessions** — Designed  
📝 **Webhooks** — Designed  
⬜ **Meetings** — Pending API key

## Documentation

- **[API Reference](docs/api-reference.md)** — Endpoints and schemas
- **[Sync Consumer](docs/sync-consumer.md)** — Agent integration
- **[Worker Spec](worker/SPEC.md)** — Local worker design

## Project Structure

```
nexus/
├── src/Nexus.Ingest/       # Azure Functions (C# .NET 8)
├── worker/                 # Local worker (Python)
├── scripts/                # Sync utilities
├── docs/                   # Documentation
└── README.md
```

## License

MIT