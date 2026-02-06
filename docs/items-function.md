# ItemsFunction

API for sync consumers to fetch, read, and delete processed items.

## Endpoints

### List Items

```
GET /api/items?code={functionKey}
```

**Query Parameters:**
- `type` — Filter by item type (email, calendar, meeting)
- `top` — Max results (default: 100, max: 500)

**Response:**
```json
{
  "items": [
    {
      "partitionKey": "email",
      "rowKey": "12345",
      "timestamp": "2026-02-06T10:30:00Z",
      "subject": "Meeting Tomorrow",
      "from": "user@example.com",
      "uniqueBody": "Let's discuss the project..."
    }
  ],
  "count": 1
}
```

### Get Item Body

```
GET /api/items/body?code={functionKey}&type={type}&id={rowKey}
```

Fetches full content from blob storage (email body, meeting transcript).

**Response:**
```
Content-Type: text/plain; charset=utf-8

Full email body or meeting transcript content...
```

### Delete Item

```
DELETE /api/items?code={functionKey}&type={type}&id={rowKey}
```

Removes item after processing (idempotent).

**Response:** 204 No Content

## Authentication

- **Function Key** — Required as `code` query parameter
- **Application Key** — Required as `X-Api-Key` header

## Purpose

Sync consumers (agents) use this API to:
1. List pending items
2. Fetch full content for processing
3. Delete items after successful processing

## Storage

- **Reads from:** Items table
- **Reads from:** Blob storage (for full content)
- **Deletes from:** Items table (after processing)

## Error Handling

- Missing auth → 401 Unauthorized
- Item not found → 404 Not Found
- Invalid parameters → 400 Bad Request