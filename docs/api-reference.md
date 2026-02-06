# API Reference

Complete reference for Nexus REST APIs.

## Base URL

```
https://nexusassistant.azurewebsites.net/api/
```

## Authentication

All requests require two keys:

```bash
curl "https://nexus.../api/items?code=<function-key>" \
  -H "X-Api-Key: <app-key>"
```

**Function Key** - Azure-managed, in URL as `code` parameter  
**App Key** - Nexus-managed, in `X-Api-Key` header

Get both from your administrator.

## Items API

### List Items

Get available data for processing.

```bash
GET /api/items?code=<function-key>
```

**Parameters:**
- `type` - Filter by type: `email`, `calendar`, `meeting` (optional)
- `top` - Max results, default 100, max 500 (optional)

**Response:**
```json
{
  "items": [
    {
      "partitionKey": "email",
      "rowKey": "msg123",
      "subject": "Meeting Tomorrow",
      "from": "alice@example.com",
      "receivedAt": "2026-02-06T10:30:00Z",
      "fileName": "2026-02-06-meeting-tomorrow.md"
    }
  ],
  "count": 1
}
```

### Get Full Content

Fetch complete email body or meeting transcript.

```bash
GET /api/items/body?type=<type>&id=<rowKey>&code=<function-key>
```

**Parameters:**
- `type` - Required: `email` or `meeting`
- `id` - Required: `rowKey` from list response

**Response:** Plain text content

### Delete Item

Remove item after successful processing.

```bash
DELETE /api/items?type=<type>&id=<rowKey>&code=<function-key>
```

**Response:** 204 No Content (success, even if already deleted)

## Whitelist API

Control which emails are accepted.

### List Whitelist

```bash
GET /api/whitelist?code=<function-key>
```

**Response:**
```json
{
  "entries": [
    {
      "type": "domain",
      "value": "example.com",
      "addedAt": "2026-02-06T10:00:00Z"
    },
    {
      "type": "email", 
      "value": "alice@other.com",
      "addedAt": "2026-02-06T10:15:00Z"
    }
  ]
}
```

### Add to Whitelist

```bash
POST /api/whitelist?code=<function-key>
Content-Type: application/json

{
  "domains": ["example.com", "acme.org"],
  "emails": ["bob@specific.com"]
}
```

### Remove from Whitelist

```bash
DELETE /api/whitelist/{type}/{value}?code=<function-key>
```

**Examples:**
- `DELETE /api/whitelist/domain/example.com`
- `DELETE /api/whitelist/email/alice@example.com`

## Sessions API

Store OpenClaw session transcripts.

```bash
POST /api/sessions?code=<function-key>
Content-Type: application/json

{
  "agentId": "main",
  "sessionId": "abc123-def456-...",
  "transcript": "raw JSONL content..."
}
```

**Response:**
```json
{
  "status": "ok",
  "sessionId": "abc123-def456-...",
  "stored": "2026-02-06T15:48:00Z"
}
```

## Error Responses

### 400 Bad Request
```json
{
  "error": "Invalid request",
  "details": "Missing required field: type"
}
```

### 401 Unauthorized
```json
{
  "error": "Unauthorized",
  "details": "Invalid API key"
}
```

### 404 Not Found
```json
{
  "error": "Not found",
  "details": "Item not found"
}
```

### 409 Conflict
```json
{
  "error": "Already exists",
  "sessionId": "abc123-def456-..."
}
```

## Rate Limits

No formal rate limits, but:
- Recommended: max 1 request/second for polling
- Use sync script instead of frequent API calls
- Batch delete operations when possible

## Best Practices

1. **Use sync script** for regular data access
2. **Delete after processing** to avoid re-syncing
3. **Handle errors gracefully** - network issues are common
4. **Filter by type** when you only need specific data
5. **Fetch body only when needed** - saves bandwidth