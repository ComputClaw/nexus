"""Job scheduler - main loop for running jobs."""

import logging
import time
from typing import Any

from config import Config, JobConfig
from jobs import Job, JobResult, SessionUploadJob

log = logging.getLogger(__name__)


def create_job(job_config: JobConfig, agents: dict[str, Any]) -> Job | None:
    """Create a job instance from config."""
    job_types = {
        "session_upload": SessionUploadJob,
        # Add more job types here
    }
    
    job_class = job_types.get(job_config.type)
    if not job_class:
        log.error(f"Unknown job type: {job_config.type}")
        return None
    
    return job_class(
        job_id=job_config.id,
        job_type=job_config.type,
        enabled=job_config.enabled,
        interval_minutes=job_config.interval_minutes,
        config=job_config.config,
        agents=agents
    )


def run_job(job: Job, endpoint: str, api_key: str) -> JobResult:
    """Run a single job and handle errors."""
    try:
        log.info(f"Running job: {job.id}")
        result = job.run(endpoint, api_key)
        job.mark_run()
        
        log.info(f"Job {job.id}: {result.message}")
        
        if result.errors:
            for error in result.errors:
                log.warning(f"Job {job.id}: {error}")
        
        return result
        
    except Exception as e:
        log.exception(f"Job {job.id} failed with exception")
        job.mark_run()  # Still mark as run to avoid infinite retry
        
        return JobResult(
            job_id=job.id,
            success=False,
            message=f"Exception: {str(e)}",
            items_processed=0,
            errors=[str(e)]
        )


def run_scheduler(config: Config) -> None:
    """Run the scheduler loop."""
    log.info("Starting Nexus Worker")
    
    # Create job instances
    jobs: list[Job] = []
    for job_config in config.jobs:
        job = create_job(job_config, config.agents)
        if job:
            jobs.append(job)
            log.info(f"Loaded job: {job.id} (type={job.type}, enabled={job.enabled})")
    
    if not jobs:
        log.error("No jobs configured")
        return
    
    log.info(f"Loaded {len(jobs)} jobs")
    
    # Main loop
    while True:
        for job in jobs:
            if not job.enabled:
                continue
            
            if not job.is_due():
                continue
            
            run_job(job, config.endpoint, config.api_key)
        
        time.sleep(60)  # Check every minute


def run_single_job(config: Config, job_id: str) -> JobResult | None:
    """Run a single job by ID and exit."""
    log.info(f"Running single job: {job_id}")
    
    # Find the job config
    job_config = None
    for jc in config.jobs:
        if jc.id == job_id:
            job_config = jc
            break
    
    if not job_config:
        log.error(f"Job not found: {job_id}")
        return None
    
    # Create and run the job
    job = create_job(job_config, config.agents)
    if not job:
        return None
    
    return run_job(job, config.endpoint, config.api_key)
