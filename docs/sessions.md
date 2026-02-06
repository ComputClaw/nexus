# Session Transcripts  

OpenClaw session transcript storage for analytics and archival.

## Overview

Nexus stores raw session transcripts from OpenClaw agents for future analytics, cost tracking, and debugging. No processing is done during ingestion — transcripts are stored as-is.

## Ingestion Endpoint

```
POST /api/sessions?code=<function-key>
```

**Request:**
```json
{
  "agentId": "main",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b", 
  "transcript": "raw JSONL content as string..."
}
```

**Response (Success):**
```json
{
  "status": "ok",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b",
  "stored": "2026-02-06T15:48:00Z"
}
```

**Response (Duplicate):**
```json
{
  "error": "Session already exists",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b"
}
```

## Data Storage

### Sessions Table

| Field | Type | Description |
|-------|------|-------------|
| **PartitionKey** | string | `yyyy-MM-dd` (date received) |
| **RowKey** | string | Session UUID (36 characters) |
| **Timestamp** | datetime | When received |
| **RawData** | string | Complete JSONL transcript |

**Design decisions:**
- **Date partitioning** - Easy cleanup of old sessions
- **Raw storage** - No parsing, preserves original format
- **1MB limit** - Table Storage constraint for large sessions

## Worker Integration

The Nexus worker uploads completed sessions:

1. **Scan agents** - Check session directories for all configured agents
2. **Find completed** - Sessions not in `sessions.json` are complete
3. **Upload raw** - Send entire `.jsonl` file content
4. **Archive locally** - Move uploaded files to archive folder

Session identification:
- **Filename:** `0fca86c2-49d4-4985-b7ea-2ea80fa8556b.jsonl.deleted.2026-02-02T20-29-47.344Z`
- **Session ID:** First 36 characters = `0fca86c2-49d4-4985-b7ea-2ea80fa8556b`

## Future Analytics

Raw transcript storage enables:
- **Token usage tracking** - Parse usage fields from messages
- **Cost analysis** - Sum cost fields across sessions
- **Performance metrics** - Response times, context efficiency
- **Model usage** - Which models used by which agents
- **Debugging** - Full session replay for issues

## Error Handling

| Scenario | Response |
|----------|----------|
| Missing fields | 400 Bad Request |
| Invalid sessionId format | 400 Bad Request |
| Duplicate sessionId | 409 Conflict |
| Transcript > 1MB | 413 Payload Too Large |
| Storage failure | 500 Internal Server Error |

## Data Lifecycle

**Retention:** TBD (90 days? 1 year?)

**Cleanup:** Partition by date enables batch deletion:
```sql
DELETE FROM Sessions WHERE PartitionKey < '2025-11-01'
```

**Access patterns:**
- Session lookup by ID (RowKey)
- Date range queries (PartitionKey)
- Analytics processing (scan all or date range)