# Session Transcripts

OpenClaw session transcript storage for analytics and archival.

## Overview

Nexus stores raw session transcripts from OpenClaw agents for future analytics, cost tracking, and debugging. No processing is done during ingestion — transcripts are stored as-is.

## Status

✅ **Implemented** — Endpoint live and accepting data

## Endpoint

```
POST /api/sessions?code={functionKey}
Content-Type: application/json
```

### Request

```json
{
  "agentId": "main",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b",
  "transcript": "raw JSONL content as string..."
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `agentId` | string | Yes | Agent identifier (main, flickclaw, etc.) |
| `sessionId` | string | Yes | Session UUID (36 characters) |
| `transcript` | string | Yes | Raw JSONL file content |

### Responses

**Success (200 OK):**
```json
{
  "status": "ok",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b",
  "stored": "2026-02-06T15:48:00Z"
}
```

**Bad Request (400):**
```json
{
  "error": "Missing required fields: agentId, sessionId, transcript"
}
```

```json
{
  "error": "Invalid sessionId format — expected UUID"
}
```

**Conflict (409):**
```json
{
  "error": "Session already exists",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b"
}
```

**Payload Too Large (413):**
```json
{
  "error": "Transcript exceeds 1 MB limit"
}
```

## Storage

### Sessions Table

| Field | Type | Description |
|-------|------|-------------|
| **PartitionKey** | string | `yyyy-MM-dd` (date received, for cleanup) |
| **RowKey** | string | `sessionId` (36-character UUID) |
| **Timestamp** | datetime | When received |
| **AgentId** | string | Agent identifier |
| **RawData** | string | Complete JSONL transcript |

**Example entity:**
```json
{
  "PartitionKey": "2026-02-06",
  "RowKey": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b",
  "Timestamp": "2026-02-06T15:48:23.145Z",
  "AgentId": "main",
  "RawData": "{\"type\":\"session\",\"version\":1,...}\n{\"type\":\"message\",...}\n..."
}
```

### Storage Constraints

- **Max entity size:** 1MB (Table Storage limit)
- **Large sessions:** Rejected with 413 Payload Too Large
- **Deduplication:** 409 Conflict if sessionId already exists
- **Cleanup:** Partition by date enables easy deletion of old data

## Processing Logic

1. **Validate request** — check required fields present
2. **Validate sessionId** — must be 36-character UUID format
3. **Validate size** — transcript must be ≤ 1MB
4. **Check duplicates** — reject if RowKey exists
5. **Store raw data** — insert with current date as PartitionKey
6. **Return confirmation** — sessionId and timestamp

**No parsing or analysis** — store raw transcript as-is for later processing.

## Worker Integration

The Nexus worker uploads completed sessions (see [worker specs](../worker/)):

1. **Scan agents** — Check session directories for all configured agents
2. **Find completed** — Sessions not in `sessions.json` are complete
3. **Upload raw** — Send entire `.jsonl` file content
4. **Archive locally** — Move uploaded files to archive folder

**Session identification from filename:**
- **Filename:** `0fca86c2-49d4-4985-b7ea-2ea80fa8556b.jsonl.deleted.2026-02-02T20-29-47.344Z`
- **Session ID:** First 36 characters = `0fca86c2-49d4-4985-b7ea-2ea80fa8556b`

## Future Analytics

Raw transcript storage enables:
- **Token usage tracking** — Parse usage fields from messages
- **Cost analysis** — Sum cost fields across sessions
- **Performance metrics** — Response times, context efficiency
- **Model usage** — Which models used by which agents
- **Debugging** — Full session replay for issues

## Data Lifecycle

**Retention:** TBD (90 days? 1 year?)

**Cleanup:** Partition by date enables batch deletion:
```
Delete all entities where PartitionKey < '2025-11-01'
```

**Access patterns:**
- Session lookup by ID (RowKey)
- Date range queries (PartitionKey)
- Analytics processing (scan all or date range)
- AgentId filtering (requires scanning RawData or using AgentId field)