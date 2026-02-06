# Meeting Transcripts

Fireflies.ai integration for meeting transcripts and summaries.

## Overview

Nexus receives webhook notifications from Fireflies.ai when meeting transcripts are ready. It processes them asynchronously and stores both summaries and full transcripts.

## Webhook Integration

### Fireflies Webhook Endpoint
```
POST /api/fireflies
```

Receives notifications when Fireflies completes meeting processing.

**Authentication:** HMAC-SHA256 signature validation
- Header: `X-Fireflies-Signature`
- Validates against configured webhook secret

**Payload:**
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

## Processing

1. **Validate webhook** - Check HMAC signature
2. **Enqueue notification** - Send to `meeting-ingest` queue
3. **Fetch full transcript** - Download from Fireflies API
4. **Auto-whitelist participants** - Add to email whitelist
5. **Storage** - Metadata in Items table, full transcript in blob

## Data Storage

### Items Table
Meeting metadata stored with:
- **PartitionKey:** `meeting`
- **RowKey:** Fireflies meeting ID
- **Fields:** Title, Date, Duration, Participants, Summary, ActionItems
- **TranscriptBlob:** Blob path for full transcript

### Blob Storage
Full transcripts stored in:
- **transcripts/{year}/{month}/{meetingId}.txt**

## Agent Consumption

Agents fetch meeting data via Items API:

```
GET /api/items?type=meeting
GET /api/items/body?type=meeting&id=<meetingId>
DELETE /api/items?type=meeting&id=<meetingId>
```

## Configuration

**Required settings:**
- `FirefliesApiKey` - API key for Fireflies GraphQL API
- `FirefliesWebhookSecret` - Secret for webhook signature validation

**Webhook URL:**
Register with Fireflies: `https://nexusassistant.azurewebsites.net/api/fireflies?code=<function-key>`

## Status

⬜ **Pending** - Requires Fireflies API key from administrator