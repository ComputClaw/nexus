# Session Transcripts

OpenClaw session transcript storage for analytics and archival.

## Overview

Nexus stores raw session transcripts from OpenClaw agents for future analytics, cost tracking, and debugging. No processing is done during ingestion — transcripts are stored as-is in Blob Storage.

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
  "path": "inbox/main/0fca86c2-49d4-4985-b7ea-2ea80fa8556b.jsonl",
  "bytes": 245760,
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
  "error": "Transcript exceeds 10 MB limit"
}
```

## Storage

### Blob Storage

Transcripts are stored as blobs in the `sessions` container:

```
sessions/inbox/{agentId}/{sessionId}.jsonl
```

**Example:**
```
sessions/inbox/main/0fca86c2-49d4-4985-b7ea-2ea80fa8556b.jsonl
```

### Storage Constraints

- **Max transcript size:** 10MB
- **Deduplication:** Upload with `overwrite: false` — returns 409 if blob already exists
- **Container:** Auto-created on first write via `CreateIfNotExistsAsync`

### Why Blob Storage (not Table Storage)

- **No 1MB entity limit** — session transcripts can be large
- **Simpler access patterns** — direct blob path lookup by agent + session ID
- **Cost effective** — blob storage is cheaper for large text data

## Processing Logic

1. **Fast reject** — check Content-Length header for obviously oversized requests
2. **Deserialize** — parse JSON body
3. **Validate request** — check required fields present
4. **Validate sessionId** — must be 36-character UUID format
5. **Validate size** — transcript must be ≤ 10MB (UTF-8 byte count)
6. **Ensure container** — create `sessions` container if needed
7. **Upload blob** — `inbox/{agentId}/{sessionId}.jsonl` with `overwrite: false`
8. **Return confirmation** — sessionId, path, bytes, and timestamp

**No parsing or analysis** — store raw transcript as-is for later processing.

## Worker Integration

The Nexus worker uploads completed sessions (see [worker spec](worker.md)):

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

**Cleanup:** Blob prefix enables easy deletion by agent or date.

**Access patterns:**
- Session lookup by agent + session ID (direct blob path)
- Agent listing (prefix `inbox/{agentId}/`)
- Full scan for analytics processing
