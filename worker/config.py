"""Configuration loading and validation."""

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any


@dataclass
class AgentConfig:
    workspace: str
    sessions_dir: str


@dataclass
class JobConfig:
    id: str
    type: str
    enabled: bool
    interval_minutes: int
    config: dict[str, Any]


@dataclass
class Config:
    endpoint: str
    api_key: str
    agents: dict[str, AgentConfig]
    jobs: list[JobConfig]


def load_config(path: str = "config.json") -> Config:
    """Load configuration from JSON file."""
    config_path = Path(path)
    if not config_path.exists():
        raise FileNotFoundError(f"Config file not found: {path}")
    
    with open(config_path) as f:
        data = json.load(f)
    
    # Parse nexus config
    nexus = data.get("nexus", {})
    endpoint = nexus.get("endpoint", "")
    api_key = nexus.get("apiKey", "")
    
    if not endpoint:
        raise ValueError("Missing nexus.endpoint in config")
    if not api_key:
        raise ValueError("Missing nexus.apiKey in config")
    
    # Parse agents
    agents = {}
    for agent_id, agent_data in data.get("agents", {}).items():
        agents[agent_id] = AgentConfig(
            workspace=agent_data.get("workspace", ""),
            sessions_dir=agent_data.get("sessionsDir", "")
        )
    
    # Parse jobs
    jobs = []
    for job_data in data.get("jobs", []):
        jobs.append(JobConfig(
            id=job_data.get("id", ""),
            type=job_data.get("type", ""),
            enabled=job_data.get("enabled", True),
            interval_minutes=job_data.get("intervalMinutes", 60),
            config=job_data.get("config", {})
        ))
    
    return Config(
        endpoint=endpoint,
        api_key=api_key,
        agents=agents,
        jobs=jobs
    )
