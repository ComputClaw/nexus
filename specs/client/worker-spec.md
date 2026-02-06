# Nexus Worker Specification

Local Python service that runs jobs to sync data between the OpenClaw host and Nexus.

## Overview

The worker is a scheduler that runs configured jobs at intervals. Jobs do work and return `JobResult`. Worker handles notifications if configured.

**Principles:**
- Jobs are pure — receive args, return `JobResult`, no side effects
- Worker handles cross-cutting concerns: scheduling, logging, notifications
- All config in one file — no hardcoded paths or agent IDs
- Fail gracefully — log errors, continue running

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        Nexus Worker                         │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │                     Config                            │  │
│  │  url, apiKey                                         │  │
│  │  jobs: [{id, type, description, interval, args,      │  │
│  │          notifyAgentId?}]                            │  │
│  └──────────────────────────────────────────────────────┘  │
│                            │                                │
│                            ▼                                │
│  ┌──────────────────────────────────────────────────────┐  │
│  │                    Scheduler                          │  │
│  │  - Tracks last run time per job                      │  │
│  │  - Runs jobs when interval elapsed                   │  │
│  │  - Sends JobResult to notifyAgentId (if set)         │  │
│  └──────────────────────────────────────────────────────┘  │
│                            │                                │
│         ┌──────────────────┼──────────────────┐            │
│         ▼                  ▼                  ▼            │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐    │
│  │ session_    │    │ webhook_    │    │  (future)   │    │
│  │ upload      │    │ pull        │    │             │    │
│  │ → JobResult │    │ → JobResult │    │             │    │
│  └─────────────┘    └─────────────┘    └─────────────┘    │
│                                                             │
└─────────────────────────────────────────────────────────────┘
           │                                    │
           ▼                                    ▼
    ┌─────────────┐                      ┌─────────────┐
    │   Nexus     │                      │  OpenClaw   │
    │  (Azure)    │                      │  (notify)   │
    └─────────────┘                      └─────────────┘
```

## Configuration

### File Location

`config.json` in worker directory, or specified via `--config` flag.

### Schema

```json
{
  "url": "https://nexusassistant.azurewebsites.net/api",
  "apiKey": "function-key-here",
  "jobs": [
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
    },
    {
      "id": "flickclaw-sessions",
      "type": "session_upload", 
      "description": "Upload FlickClaw sessions to Nexus",
      "intervalMinutes": 60,
      "notifyAgentId": "flickclaw",
      "args": [
        "/home/martin/.openclaw/agents/flickclaw/sessions",
        "/home/martin/.openclaw/agents/flickclaw/sessions/archive",
        "flickclaw"
      ]
    }
  ]
}
```

### Config Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `url` | string | Yes | Nexus API base URL |
| `apiKey` | string | Yes | Azure Function key for auth |
| `jobs` | array | Yes | List of job configurations |

### Job Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique job identifier (for logging) |
| `type` | string | Yes | Job type: `session_upload`, `webhook_pull` |
| `description` | string | Yes | Human-readable description |
| `intervalMinutes` | int | Yes | Minutes between runs (daemon mode) |
| `args` | array | Yes | Job-specific arguments (positional) |
| `notifyAgentId` | string | No | Agent to notify with JobResult (omit = no notification) |

## Job Types

See individual job specifications:
- [session_upload](jobs/session-upload-spec.md) - Upload completed session transcripts
- [webhook_pull](jobs/webhook-pull-spec.md) - Deliver webhook items to agent inboxes

## Job Interface

All jobs implement:

```python
class Job(Protocol):
    id: str
    type: str
    description: str
    interval_minutes: int
    args: list[str]
    notify_agent_id: str | None
    last_run: datetime | None
    
    def run(self, nexus_url: str, api_key: str) -> JobResult:
        """Execute job. Always returns JobResult, never raises."""
        ...

@dataclass
class JobResult:
    job_id: str
    success: bool
    message: str              # Human-readable summary
    items_processed: int
    errors: list[str]         # Individual error messages
```

## CLI Interface

```
usage: nexus-worker [-h] [--config CONFIG] [--job JOB] [--verbose]

Nexus Worker - syncs data between OpenClaw and Nexus

optional arguments:
  -h, --help       Show help message
  --config CONFIG  Path to config file (default: config.json)
  --job JOB        Run specific job by ID (ignores intervalMinutes)
  --verbose        Enable debug logging
```

**Behavior:**
- No arguments → daemon mode (runs forever, respects `intervalMinutes`)
- `--job <id>` → run that job immediately, then exit

Config is always required — provides Nexus credentials and job definitions.

**Examples:**

```bash
# Daemon mode (runs forever, jobs run on their intervals)
python -m nexus_worker

# Run specific job immediately and exit
python -m nexus_worker --job main-sessions

# Custom config location (daemon mode)  
python -m nexus_worker --config /etc/nexus/config.json

# Verbose + specific job
python -m nexus_worker --job main-sessions --verbose
```

## Notifications

Jobs always return a `JobResult`. If `notifyAgentId` is set on the job, the worker spawns a task on that agent with the full result JSON.

**How:**
```bash
openclaw sessions spawn --agent {notifyAgentId} --task "Nexus job completed. JobResult: {full JSON}"
```

**When:**
- Job completes (success or failure)
- `notifyAgentId` is configured on the job

**JobResult format:**
```json
{
  "jobId": "main-sessions",
  "success": true,
  "message": "Uploaded 3 sessions",
  "itemsProcessed": 3,
  "errors": []
}
```

Agent receives the complete JobResult JSON and can process/respond accordingly.

If `notifyAgentId` is not set, the job runs silently (results logged only).

## Scheduler Loop

```python
def run_scheduler(config: Config):
    jobs = load_jobs(config)
    
    while True:
        for job in jobs:
            if not job.is_due():
                continue
            
            log.info(f"Running job: {job.id}")
            result = job.run(config.url, config.api_key)
            job.mark_run()
            
            log.info(f"Job {job.id}: {result.message}")
            if result.errors:
                for err in result.errors:
                    log.warning(f"Job {job.id}: {err}")
            
            if job.notify_agent_id:
                notify_agent(job.notify_agent_id, result)
        
        sleep(60)  # Check every minute
```

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Config file missing | Exit with error |
| Config invalid JSON | Exit with error |
| Nexus API unreachable | Log error, retry next cycle |
| Single file upload fails | Log error, continue to next file |
| Job raises exception | Log error, mark job as run, continue |

## Logging

Format: `{timestamp} [{level}] [{job_id}] {message}`

```
2026-02-06T14:00:00 [INFO] [scheduler] Starting Nexus Worker
2026-02-06T14:00:00 [INFO] [scheduler] Loaded 3 jobs
2026-02-06T14:00:01 [INFO] [main-sessions] Running job
2026-02-06T14:00:01 [INFO] [main-sessions] Found 2 completed sessions
2026-02-06T14:00:02 [INFO] [main-sessions] Uploaded: abc123.jsonl
2026-02-06T14:00:03 [INFO] [main-sessions] Uploaded: def456.jsonl
2026-02-06T14:00:03 [INFO] [main-sessions] Archived 2 files
2026-02-06T14:00:03 [INFO] [main-sessions] Job complete: 2 uploaded, 0 failed
2026-02-06T14:00:03 [INFO] [scheduler] Next check in 60s
```

## Deployment

### Systemd Service

```ini
[Unit]
Description=Nexus Worker
After=network.target

[Service]
Type=simple
User=martin
WorkingDirectory=/home/martin/repos/nexus/worker
ExecStart=/usr/bin/python3 -m nexus_worker
Restart=always
RestartSec=30
Environment=PYTHONUNBUFFERED=1

[Install]
WantedBy=multi-user.target
```

## File Structure

```
worker/
├── nexus_worker/
│   ├── __init__.py
│   ├── __main__.py          # Entry point
│   ├── config.py            # Config loading/validation
│   ├── scheduler.py         # Main loop
│   └── jobs/
│       ├── __init__.py
│       ├── base.py          # Job protocol/base class
│       ├── session_upload.py
│       └── webhook_pull.py
├── config.json
├── config.example.json
├── requirements.txt
└── worker-spec.md
```

## Dependencies

```
# requirements.txt
requests>=2.28.0
```

## Security

- API key in config file, not in code
- Config file should be `chmod 600` (owner read/write only)
- Worker runs as unprivileged user
- No external network exposure — outbound only to Nexus

---

*Status: Specification complete, pending implementation*