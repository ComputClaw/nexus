# API Reference

Complete reference for Nexus REST APIs.

## Base URL

```
https://nexusassistant.azurewebsites.net/api/
```

## Authentication

All requests require a Function Key:

```bash
curl "https://nexus.../api/sessions?code=<function-key>"
```

**Function Key** — Azure-managed, passed in URL as `code` parameter.

## Sessions API

Store OpenClaw session transcripts for analytics and archival.

### Store Session

```bash
POST /api/sessions?code=<function-key>
Content-Type: application/json

{
  "agentId": "main",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b",
  "transcript": "raw JSONL content..."
}
```

**Response (200 OK):**
```json
{
  "status": "ok",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b",
  "stored": "2026-02-06T15:48:00Z"
}
```

**Response (400 Bad Request):**
```json
{
  "error": "Missing required fields: agentId, sessionId, transcript"
}
```

**Response (409 Conflict):**
```json
{
  "error": "Session already exists",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b"
}
```

**Response (413 Payload Too Large):**
```json
{
  "error": "Transcript exceeds 1 MB limit"
}
```

### Request Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `agentId` | string | Yes | Agent identifier (main, flickclaw, etc.) |
| `sessionId` | string | Yes | Session UUID (36 characters) |
| `transcript` | string | Yes | Raw JSONL file content (max 1MB) |

## Error Responses

| Code | Description |
|------|-------------|
| 400 | Bad Request — Missing or invalid fields |
| 401 | Unauthorized — Invalid function key |
| 409 | Conflict — Session already exists |
| 413 | Payload Too Large — Transcript exceeds 1MB |
| 500 | Internal Server Error — Storage failure |