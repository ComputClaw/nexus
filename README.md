# Nexus

Integration service for [OpenClaw](https://github.com/openclaw/openclaw) agents.

## Architecture

Nexus has two sides: a **Function App** running in the cloud and a **Worker** running locally on the OpenClaw host.

```
                          Cloud                              Local (OpenClaw host)
                 ┌─────────────────────┐            ┌──────────────────────────┐
External         │   Function App      │            │        Worker            │
Services ──────▶ │   (Azure Functions)  │            │                          │
                 │                      │◀───────────│  session_upload job      │
                 │   Blob + Table       │   POST     │  (future jobs...)        │
                 │   Storage            │            │                          │
                 └─────────────────────┘            └──────────────────────────┘
```

### Function App

C# .NET 8 Azure Functions that receive data via HTTP and store it in Azure Blob + Table Storage. This is the API layer — it accepts webhooks and session uploads, validates them, and writes to storage.

- **Runtime:** C# .NET 8, Azure Functions v4
- **Storage:** Azure Blob Storage (transcripts) + Table Storage (metadata)
- **Source:** `src/function-app/`

### Worker

Python process running on the OpenClaw host. Runs one or more jobs on a schedule. Each job has its own type, interval, and configuration defined in `config.json`.

- **Runtime:** Python 3, invoked as `python -m worker` from `src/`
- **Source:** `src/worker/`

**CLI:**

```
python -m worker                              # run scheduler loop
python -m worker --job session-upload          # run one job and exit
python -m worker --config /path/to/config.json # custom config path
python -m worker --verbose                     # enable debug logging
```

**How jobs work:** The scheduler loops every 60 seconds, checking each enabled job. If a job's `intervalMinutes` has elapsed since its last run, it executes. Each job extends the `Job` base class and implements `run()`, which returns a `JobResult` with success/failure, message, and error details. Jobs are registered by type in `scheduler.py`.

**Current jobs:**

| Type | Description |
|------|-------------|
| `session_upload` | Finds completed session transcripts, uploads them to Nexus, and archives the files |

## Status

- **Sessions** — Live
- **Worker** — Implemented (pending deployment)
- **Webhooks** — Designed

## Project Structure

```
nexus/
├── src/function-app/      # Azure Functions (C# .NET 8)
├── src/worker/            # Worker + jobs (Python)
├── specs/                 # Development specifications
└── README.md
```

## Documentation

- **[specs/](specs/)** — Development specifications

## License

MIT
