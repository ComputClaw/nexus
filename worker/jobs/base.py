"""Base job interface."""

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any


@dataclass
class JobResult:
    """Result of a job execution."""
    job_id: str
    success: bool
    message: str
    items_processed: int = 0
    errors: list[str] = field(default_factory=list)


class Job(ABC):
    """Base class for all jobs."""
    
    def __init__(
        self,
        job_id: str,
        job_type: str,
        enabled: bool,
        interval_minutes: int,
        config: dict[str, Any],
        agents: dict[str, Any]
    ):
        self.id = job_id
        self.type = job_type
        self.enabled = enabled
        self.interval_minutes = interval_minutes
        self.config = config
        self.agents = agents
        self.last_run: datetime | None = None
    
    def is_due(self) -> bool:
        """Check if job should run based on interval."""
        if self.last_run is None:
            return True
        
        elapsed = (datetime.now() - self.last_run).total_seconds()
        return elapsed >= (self.interval_minutes * 60)
    
    def mark_run(self) -> None:
        """Update last_run timestamp."""
        self.last_run = datetime.now()
    
    @abstractmethod
    def run(self, endpoint: str, api_key: str) -> JobResult:
        """Execute the job. Returns JobResult."""
        pass
