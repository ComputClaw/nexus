# session_upload Job

Upload completed session transcripts from OpenClaw agents to Nexus.

## Purpose

Finds completed session files across all configured agents, uploads the raw JSONL content to Nexus, and archives the files locally. The worker does no parsing — it ships raw files and lets Nexus handle processing.

## Arguments (Positional)

| Position | Name | Description |
|----------|------|-------------|
| 0 | sessionsDir | Path to agent's sessions directory |
| 1 | archiveDir | Path to move uploaded files to |
| 2 | agentId | Agent identifier sent to Nexus |

## Example Configuration

```json
{
  "id": "main-sessions",
  "type": "session_upload",
  "description": "Upload main agent sessions to Nexus",
  "intervalMinutes": 60,
  "args": [
    "/home/martin/.openclaw/agents/main/sessions",
    "/home/martin/.openclaw/agents/main/sessions/archive", 
    "main"
  ]
}
```

## Processing Logic

1. **Read active sessions** - Load `sessions.json` from `sessionsDir` → list of active session IDs
2. **List session files** - Find all `*.jsonl` files in `sessionsDir`
3. **Filter completed** - Files whose session ID (first 36 characters) is NOT in active sessions list
4. **Upload each completed session:**
   a. Extract session ID from filename (first 36 characters)
   b. Read raw file content
   c. POST to `{nexus_url}/sessions?code={api_key}`
   d. On success: move file to `archiveDir`
   e. On failure: log error, continue to next file
5. **Return results** - Count of uploaded files and any errors

## Session Identification

**Filename format:** `{sessionId}.{suffix}`
- Example: `0fca86c2-49d4-4985-b7ea-2ea80fa8556b.jsonl.deleted.2026-02-02T20-29-47.344Z`
- **Session ID:** First 36 characters = `0fca86c2-49d4-4985-b7ea-2ea80fa8556b`

## Nexus API Request

```
POST /api/sessions?code={api_key}
Content-Type: application/json

{
  "agentId": "main",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b",
  "transcript": "...raw jsonl content as string..."
}
```

## Response Handling

### Success (200 OK)
```json
{
  "status": "ok",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b",
  "stored": "2026-02-06T15:48:00Z"
}
```
**Action:** Move file to archive directory

### Conflict (409 Conflict)
```json
{
  "error": "Session already exists",
  "sessionId": "0fca86c2-49d4-4985-b7ea-2ea80fa8556b"
}
```
**Action:** Log warning, move file to archive (duplicate upload)

### Too Large (413 Payload Too Large)
```json
{
  "error": "Transcript exceeds 1 MB limit"
}
```
**Action:** Log error, skip file (leave in sessions dir)

### Other Errors (400, 500, etc.)
**Action:** Log error, retry next cycle (leave file in place)

## File Operations

### Archive Structure
```
archive/
└── {sessionId}.jsonl.deleted.{timestamp}
```

### Error Handling
- **Archive directory missing** - Create it
- **File already in archive** - Overwrite (shouldn't happen)
- **Move fails** - Log error but mark upload as successful

## Job Result

```python
@dataclass
class JobResult:
    job_id: str = "session_upload"
    success: bool = True  # False only if critical failure
    message: str = "Uploaded 3 sessions for main"
    items_processed: int = 3  # Number of sessions uploaded
    errors: list[str] = []  # Individual file errors
```

**Example results:**
```json
{
  "jobId": "main-sessions",
  "success": true,
  "message": "Uploaded 3 sessions for main",
  "itemsProcessed": 3,
  "errors": []
}
```

```json
{
  "jobId": "main-sessions", 
  "success": true,
  "message": "Uploaded 2 sessions, 1 failed",
  "itemsProcessed": 2,
  "errors": ["Failed to upload abc123.jsonl: 500 Internal Server Error"]
}
```

## Implementation Notes

- **No JSONL parsing** - Upload raw file content as-is
- **Idempotent** - Safe to re-run, handles duplicates gracefully
- **Resilient** - Individual file failures don't stop processing
- **Atomic moves** - Use temp file + rename for archive operations
- **Session ID validation** - Must be 36-character UUID format

## Testing

**Test cases:**
- Empty sessions directory
- No completed sessions (all in sessions.json)
- Multiple completed sessions
- Network failures during upload
- Disk full during archive
- Malformed session filenames
- Very large session files (>1MB)