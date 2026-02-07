# Nexus Memory

## Lessons Learned
- PowerShell commands (`Remove-Item`) don't work in this shell — use `rm -rf`
- Git remote: `https://github.com/ComputClaw/nexus.git`
- Worker was built from scratch in commit `fabc7af` — no prior worker existed
- Example config (`config.example.json`) matches what `load_config()` expects

## Recent Changes
- 2026-02-07: Moved `src/jobs/` into `src/worker/` as flat modules (`job.py`, `session_upload.py`), removed jobs/ subpackage
- 2026-02-07: Fixed all imports to use fully qualified `worker.*` paths
