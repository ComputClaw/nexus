# Nexus Worker

Local Python service that syncs data between OpenClaw agents and Nexus.

## What It Does

- **Uploads session transcripts** — Finds completed sessions, uploads to Nexus
- **Delivers webhook data** — Fetches webhook items, writes to agent inboxes
- **Runs on schedule** — Configurable intervals per job type

## Quick Start

1. **Copy configuration:**
   ```bash
   cp config.example.json config.json
   # Edit with your Nexus URL and API key
   ```

2. **Run once (testing):**
   ```bash
   python -m nexus_worker --job main-sessions --verbose
   ```

3. **Run as daemon:**
   ```bash
   python -m nexus_worker
   ```

## Specifications

All specs are in the [specs/client/](../specs/client/) folder:

| Spec | Description |
|------|-------------|
| [worker.md](../specs/client/worker.md) | Worker architecture, config, CLI |
| [session-upload.md](../specs/client/jobs/session-upload.md) | Session upload job |
| [webhook-pull.md](../specs/client/jobs/webhook-pull.md) | Webhook pull job |

## Status

📝 **Designed** — Specifications complete, ready for implementation