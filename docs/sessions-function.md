# SessionsFunction

Receives and stores raw session transcripts from OpenClaw agents.

## Endpoint

```
POST /api/sessions?code={functionKey}
```

## Purpose

Accepts completed session transcripts from the Nexus worker for storage and future analytics.

## Authentication

- **Function Key** — Required as `code` query parameter
- **Application Key** — Optional `X-Api-Key` header

## Request

```json
{
  "agentId": "main",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b",
  "transcript": "raw JSONL content as string..."
}
```

### Request Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `agentId` | string | Yes | Agent identifier (main, flickclaw, etc.) |
| `sessionId` | string | Yes | Session UUID (36 characters) |
| `transcript` | string | Yes | Raw JSONL file content |

## Response

**Success (200 OK):**
```json
{
  "status": "ok",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b",
  "stored": "2026-02-06T15:48:00Z"
}
```

**Duplicate (409 Conflict):**
```json
{
  "error": "Session already exists",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b"
}
```

**Too Large (413 Payload Too Large):**
```json
{
  "error": "Transcript too large",
  "maxSize": "1MB"
}
```

## Storage

### Sessions Table

| Field | Type | Description |
|-------|------|-------------|
| **PartitionKey** | string | `yyyy-MM-dd` (date received) |
| **RowKey** | string | `sessionId` (36-character UUID) |
| **Timestamp** | datetime | When received |
| **RawData** | string | Complete JSONL transcript |

## Processing

1. **Validate request** — Check required fields and sessionId format
2. **Check for duplicates** — Query for existing RowKey
3. **Check size limit** — Reject if transcript > 1MB
4. **Store raw data** — Insert with current date as PartitionKey
5. **Return confirmation** — Session ID and timestamp

**No parsing or analysis** — stores raw transcript for future processing.

## Error Handling

- Invalid JSON → 400 Bad Request
- Missing fields → 400 Bad Request
- Duplicate sessionId → 409 Conflict
- Transcript too large → 413 Payload Too Large
- Storage failure → 500 Internal Server Error