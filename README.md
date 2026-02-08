# Nexus

Webhook relay service for OpenClaw agents. Receives webhooks from external systems, processes data, and stores for agent consumption.

## Endpoint

```
POST /api/webhook/{agentName}/{source}/{type}?code={functionKey}
```

**Path parameters:**
- `agentName` — Target agent
- `source` — External system identifier
- `type` — Data type

## Storage

**Items Table** — Structured metadata with agent routing:
- `PartitionKey` — Type
- `RowKey` — Unique item ID
- `AgentName` — Target agent
- `SourceType` — Source and type combined
- Type-specific fields

**Blob Storage** — Large content (email bodies, documents, transcripts)

## Deployment

Push to `main` triggers GitHub Actions deployment.

## License

MIT
