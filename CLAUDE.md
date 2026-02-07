# Claude Code Instructions

Read these files immediately after this one:
- `MEMORY.md` — persistent memory across sessions, update as you learn
- `README.md` — project overview and architecture

---

## Project: Nexus

Integration service for [OpenClaw](https://github.com/openclaw/openclaw) agents. Two sides: a cloud Function App (C# Azure Functions) and a local Worker (Python) running on the OpenClaw host.

**Repo:** `ComputClaw/nexus`

## Conventions

- Keep docs (`specs/worker.md`, `specs/README.md`, `README.md`) in sync with code structure
- Commits go to main directly
- "squash the last N commits" = squash and force push, no confirmation needed
- Concise communication preferred

## Environment

- Windows, but shell is bash (git bash / WSL) — no PowerShell commands
