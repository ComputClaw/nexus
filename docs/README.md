# Nexus Documentation

User guide for the Nexus data ingestion service.

## What is Nexus?

Nexus collects data from external services and stores it for OpenClaw agents.

## Data Sources

| Type | Source | Description |
|------|--------|-------------|
| **Sessions** | OpenClaw | Agent session transcripts for analytics |
| **Webhooks** | External services | Generic webhook data (put.io, GitHub, etc.) |

## Sessions API

Store session transcripts for analytics and archival.

```bash
POST /api/sessions?code=<function-key>
Content-Type: application/json

{
  "agentId": "main",
  "sessionId": "uuid-here",
  "transcript": "raw JSONL content..."
}
```

See [API Reference](api-reference.md) for complete documentation.

## Worker

The Nexus worker runs on the OpenClaw host and:
- Uploads completed session transcripts to Nexus
- Delivers webhook items to agent inboxes

See [worker/](../worker/) for specifications.

## Documentation

| Topic | Description |
|-------|-------------|
| [API Reference](api-reference.md) | Complete API documentation |