# Agent Integration

How OpenClaw agents consume data from Nexus.

## Items API

The primary interface for agents to fetch processed data.

### List Items
```
GET /api/items?code=<function-key>
X-Api-Key: <app-key>
```

**Query parameters:**
- `type` - Filter by type (`email`, `calendar`, `meeting`)
- `top` - Max results (default: 100, max: 500)

**Response:**
```json
{
  "items": [
    {
      "partitionKey": "email",
      "rowKey": "AAMkAGQ5...",
      "subject": "Re: Q4 budget",
      "from": "alice@example.com", 
      "receivedAt": "2025-01-15T10:30:00Z",
      "fileName": "2025-01-15-re-q4-budget.md"
    }
  ],
  "count": 1
}
```

### Get Full Content
```
GET /api/items/body?code=<function-key>&type=<type>&id=<rowKey>
X-Api-Key: <app-key>
```

Fetches full email body or meeting transcript from blob storage.

**Response:** Plain text content

### Delete Item
```
DELETE /api/items?code=<function-key>&type=<type>&id=<rowKey>
X-Api-Key: <app-key>
```

Removes item after processing (idempotent).

## Sync Script

**Location:** `scripts/nexus-sync.js`

Zero-dependency Node.js script that:
1. Lists pending items
2. Writes markdown files to `data/inbox/`
3. Deletes processed items

**Usage:**
```bash
node nexus-sync.js --with-body --type email
```

**Flags:**
- `--with-body` - Fetch full content
- `--type <type>` - Filter by type
- `--dry-run` - Preview without changes

**Configuration:** `scripts/.nexus-config.json`
```json
{
  "nexusUrl": "https://nexusassistant.azurewebsites.net/api",
  "functionKey": "...", 
  "apiKey": "...",
  "outputDir": "../data/inbox"
}
```

## File Format

Items are saved as markdown with YAML frontmatter:

```markdown
---
id: AAMkAGQ5...
type: email
subject: "Re: Q4 budget"
from: alice@example.com
receivedAt: 2025-01-15T10:30:00Z
---

# Re: Q4 budget

Email body content here...
```

## Integration Patterns

### Pull-based (Current)
1. **Cron schedule** - Run sync script every 5-15 minutes
2. **Batch processing** - Agent processes all files in inbox
3. **Cleanup** - Items deleted from Nexus after successful sync

### Push-based (Future)
1. **Webhook delivery** - Worker delivers items to agent inbox
2. **Task spawning** - Worker spawns agent task via `sessions_spawn`
3. **Real-time** - Near-instant delivery vs polling

## Error Handling

- **Network failures** - Sync script retries with exponential backoff
- **Delete failures** - Items re-synced on next run (overwritten)
- **Processing errors** - Agent logs errors, continues with next item

## Security

**Agent credentials:**
- Function key - Access to Nexus endpoints
- Application key - Additional security layer

**Data flow:**
- All data flows from Nexus → Agent (one direction)
- No agent credentials stored in Nexus
- Agent controls what data to fetch/delete