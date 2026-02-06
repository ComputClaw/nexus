# Nexus Worker

Local Python process that syncs webhook data from Nexus to agent workspaces.

## Overview

The worker runs on the same host as OpenClaw. It:

1. Polls Nexus for pending webhook items
2. Writes items as JSON files to each agent's inbox
3. Spawns isolated agent tasks via `sessions_spawn`
4. Marks items as processed in Nexus

This is a **push model** — agents don't poll Nexus directly. The worker delivers data to them.

## Setup

```bash
# Copy config
cp config.example.json config.json

# Edit with your Nexus API key and agent paths
nano config.json

# Run once (for testing)
python3 nexus-worker.py --once

# Run as daemon
python3 nexus-worker.py
```

## Documentation

- [SPEC.md](SPEC.md) — Full specification (requirements, API, deployment)

## Status

📝 **Spec complete, implementation pending**

See SPEC.md for implementation requirements.
