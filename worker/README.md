# Nexus Worker

Local Python service that syncs data between OpenClaw agents and Nexus.

## What It Does

- **Uploads session transcripts** - Finds completed sessions, uploads to Nexus for analytics
- **Delivers webhook data** - Fetches webhook items from Nexus, writes to agent inboxes
- **Runs on schedule** - Configurable intervals per job type
- **Notifies agents** - Optional task spawning when jobs complete

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

## Configuration

Jobs are configured in `config.json`. Each job specifies:
- **type** - What kind of work (`session_upload`, `webhook_pull`)
- **args** - Job-specific parameters (paths, agent IDs)
- **intervalMinutes** - How often to run
- **notifyAgentId** - Optional agent to notify on completion

See [config.example.json](config.example.json) for examples.

## Job Types

| Type | Purpose | Specification |
|------|---------|---------------|
| **session_upload** | Upload completed session transcripts | [📄](jobs/session-upload-spec.md) |
| **webhook_pull** | Deliver webhook items to agent inboxes | [📄](jobs/webhook-pull-spec.md) |

## Documentation

- **[Worker Specification](worker-spec.md)** - Complete technical spec
- **[Job Specifications](jobs/)** - Individual job type specs

## Status

📝 **Designed** - Specifications complete, ready for implementation