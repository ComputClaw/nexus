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
✅ **Worker** — Implemented (pending deployment)
📝 **Webhooks** — Designed

## Documentation

- **[specs/](specs/)** — Development specifications

## Project Structure

```
nexus/
├── src/function-app/      # Azure Functions (C# .NET 8)
├── src/worker/            # Worker core (Python)
├── src/jobs/              # Job implementations (Python)
├── specs/                 # Development specifications
└── README.md
```

## License

MIT
