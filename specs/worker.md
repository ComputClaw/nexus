# Nexus Worker

Local Python service that syncs data between the OpenClaw host and Nexus.

## Status

✅ **Implemented** — Core worker and session_upload job complete, pending deployment

## Purpose

Multi-job worker running on the OpenClaw host. Jobs are configured independently — each has its own schedule, type, and settings.

**Key principle:** Worker posts raw data, Nexus processes. Logic lives in Nexus, worker stays dumb.

## Repo Location

```
ComputClaw/nexus/
└── src/
    ├── worker/
    │   ├── __init__.py
    │   ├── __main__.py        # Entry point, CLI
    │   ├── config.py          # Config loading
    │   ├── scheduler.py       # Main loop
    │   └── config.example.json
    └── jobs/
        ├── __init__.py
        ├── base.py            # Job ABC + JobResult
        └── session_upload.py
```

## Configuration

```json
{
  "nexus": {
    "endpoint": "https://nexusassistant.azurewebsites.net/api",
    "apiKey": "..."
  },
  "agents": {
    "<agent-id>": {
      "workspace": "/path/to/workspace",
      "sessionsDir": "/path/to/sessions"
    }
  },
  "jobs": [
    {
      "id": "session-upload",
      "type": "session_upload",
      "enabled": true,
      "intervalMinutes": 60,
      "config": {
        "agents": ["<agent-id>"]
      }
    },
    {
      "id": "webhook-pull",
      "type": "webhook_pull",
      "enabled": false,
      "intervalMinutes": 5,
      "config": {}
    }
  ]
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Nexus Worker                           │
│                                                             │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐       │
│  │   Config    │   │  Scheduler  │   │   Logger    │       │
│  │   Loader    │──▶│  (main loop)│──▶│  (stdout)   │       │
│  └─────────────┘   └──────┬──────┘   └─────────────┘       │
│                           │                                 │
│         ┌─────────────────┼─────────────────┐              │
│         ▼                 ▼                 ▼              │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐       │
│  │  session_   │   │  webhook_   │   │  (future    │       │
│  │  upload     │   │  pull       │   │   jobs)     │       │
│  └─────────────┘   └─────────────┘   └─────────────┘       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
                    ┌─────────────┐
                    │   Nexus     │
                    │  (Azure)    │
                    └─────────────┘
```

## Job Types

| Type | Direction | Description | Details |
|------|-----------|-------------|---------|
| `session_upload` | Push | Upload completed session transcripts | [job-session-upload.md](job-session-upload.md) |
| `webhook_pull` | Pull | Fetch webhook items, deliver to agents | [job-webhook-pull.md](job-webhook-pull.md) |

## Main Loop

```python
def main():
    config = load_config()
    jobs = initialize_jobs(config)

    while True:
        for job in jobs:
            if job.enabled and job.due():
                try:
                    job.run()
                    job.mark_success()
                except Exception as e:
                    job.mark_failure(e)
                    log_error(job.id, e)

        sleep(60)  # Check every minute
```

## Job Interface

Each job extends `Job` (ABC) and implements `run()`:

```python
class Job(ABC):
    id: str
    type: str
    enabled: bool
    interval_minutes: int
    last_run: datetime | None

    def is_due(self) -> bool:
        """Check if job should run based on interval."""

    def mark_run(self) -> None:
        """Update last_run timestamp."""

    @abstractmethod
    def run(self, endpoint: str, api_key: str) -> JobResult:
        """Execute the job. Returns JobResult."""

@dataclass
class JobResult:
    job_id: str
    success: bool
    message: str
    items_processed: int = 0
    errors: list[str] = field(default_factory=list)
```

## Deployment

Systemd service:

```ini
[Unit]
Description=Nexus Worker
After=network.target

[Service]
Type=simple
User=<user>
WorkingDirectory=/path/to/nexus/src
ExecStart=/usr/bin/python3 -m worker
Restart=always
RestartSec=30
Environment=PYTHONUNBUFFERED=1

[Install]
WantedBy=multi-user.target
```

**Commands:**
```bash
# Install
sudo cp nexus-worker.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable nexus-worker

# Control
sudo systemctl start nexus-worker
sudo systemctl stop nexus-worker
sudo systemctl restart nexus-worker

# Monitor
sudo systemctl status nexus-worker
journalctl -u nexus-worker -f
```

## Dependencies

```
# requirements.txt
requests>=2.28.0
```

---

*Part of the Nexus integration service*
