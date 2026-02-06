# FirefliesWebhookFunction

Receives meeting transcripts and summaries from Fireflies.ai.

## Endpoint

```
POST /api/fireflies?code={functionKey}
```

## Purpose

Entry point for Fireflies.ai webhook notifications when meeting transcripts are ready.

## Authentication

- **Function Key** — Required as `code` query parameter
- **Fireflies Verification** — Validates webhook signature

## Request

**Headers:**
- `X-Fireflies-Signature` — Webhook signature for verification

**Body:**
```json
{
  "event": "transcript_ready",
  "data": {
    "meeting_id": "abc123",
    "title": "Team Standup",
    "date": "2026-02-06",
    "duration": 1800,
    "participants": ["John Doe", "Jane Smith"],
    "transcript_url": "https://...",
    "summary": "Brief meeting summary...",
    "action_items": ["Task 1", "Task 2"]
  }
}
```

## Response

**Success (202 Accepted):**
```json
{
  "status": "queued",
  "meetingId": "abc123"
}
```

**Verification Error (401 Unauthorized):**
```json
{
  "error": "Invalid signature"
}
```

## Processing

1. **Verify signature** — Validate Fireflies webhook signature
2. **Extract meeting data** — Parse webhook payload
3. **Enqueue for processing** — Send to meeting-ingest queue
4. **Return confirmation** — Meeting ID and status

## Queue

Notifications are sent to the `meeting-ingest` queue for async processing by `MeetingProcessorFunction`.

## Error Handling

- Invalid signature → 401 Unauthorized
- Malformed payload → 400 Bad Request
- Queue failure → 500 Internal Server Error

## Storage

Does not directly write to storage — enqueues for async processing.

## Status

⬜ **Pending** — Requires Fireflies.ai API key configuration