# Nexus

Data ingestion service for [OpenClaw](https://github.com/openclaw/openclaw) agents.

## What It Does

Nexus ingests data from external services and stores it for agent consumption.

**Current Sources:**
- 🔗 **Webhooks** — Generic endpoint for external services
- 📝 **Sessions** — OpenClaw session transcripts for analytics

## Architecture

```
External Services ──webhook──▶ Azure Functions ──▶ Table Storage ──▶ Agents
```

**Components:**
- **HTTP endpoints** — Receive webhooks and data
- **Table Storage** — Structured data storage
- **Local worker** — Syncs data between host and Nexus

## Tech Stack

- **Runtime:** C# .NET 8, Azure Functions v4
- **Storage:** Azure Table Storage
- **Worker:** Python (local process on OpenClaw host)

## Status

✅ **Sessions** — Live  
📝 **Webhooks** — Designed  
📝 **Worker** — Designed

## Documentation

- **[docs/](docs/)** — User documentation
- **[specs/](specs/)** — Development specifications
- **[worker/](worker/)** — Worker specifications

## Project Structure

```
nexus/
├── src/Nexus.Ingest/       # Azure Functions (C# .NET 8)
├── worker/                 # Local worker (Python)
├── docs/                   # User documentation
├── specs/                  # Development specifications
└── README.md
```

## License

MIT