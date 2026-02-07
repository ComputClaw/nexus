"""Session upload job - uploads completed sessions to Nexus."""

import json
import logging
import shutil
from pathlib import Path
from typing import Any

import requests

from jobs.base import Job, JobResult

log = logging.getLogger(__name__)


class SessionUploadJob(Job):
    """Upload completed session transcripts to Nexus."""
    
    def run(self, endpoint: str, api_key: str) -> JobResult:
        """Execute the session upload job."""
        agent_ids = self.config.get("agents", [])
        
        if not agent_ids:
            return JobResult(
                job_id=self.id,
                success=True,
                message="No agents configured",
                items_processed=0
            )
        
        total_uploaded = 0
        errors: list[str] = []
        
        for agent_id in agent_ids:
            agent_config = self.agents.get(agent_id)
            if not agent_config:
                errors.append(f"Agent not found in config: {agent_id}")
                continue
            
            sessions_dir = Path(agent_config.sessions_dir)
            if not sessions_dir.exists():
                log.warning(f"Sessions directory not found: {sessions_dir}")
                continue
            
            uploaded, agent_errors = self._process_agent(
                agent_id=agent_id,
                sessions_dir=sessions_dir,
                endpoint=endpoint,
                api_key=api_key
            )
            
            total_uploaded += uploaded
            errors.extend(agent_errors)
        
        success = len(errors) == 0 or total_uploaded > 0
        
        if errors:
            message = f"Uploaded {total_uploaded} sessions, {len(errors)} errors"
        else:
            message = f"Uploaded {total_uploaded} sessions"
        
        return JobResult(
            job_id=self.id,
            success=success,
            message=message,
            items_processed=total_uploaded,
            errors=errors
        )
    
    def _process_agent(
        self,
        agent_id: str,
        sessions_dir: Path,
        endpoint: str,
        api_key: str
    ) -> tuple[int, list[str]]:
        """Process sessions for a single agent."""
        uploaded = 0
        errors: list[str] = []
        
        # Read active sessions from sessions.json
        active_sessions = self._get_active_sessions(sessions_dir)
        
        # Find completed session files
        completed = self._find_completed_sessions(sessions_dir, active_sessions)
        
        if not completed:
            log.debug(f"No completed sessions for {agent_id}")
            return 0, []
        
        log.info(f"Found {len(completed)} completed sessions for {agent_id}")
        
        # Upload each completed session
        for session_file in completed:
            session_id = self._extract_session_id(session_file.name)
            
            if not session_id:
                errors.append(f"Invalid session filename: {session_file.name}")
                continue
            
            try:
                success = self._upload_session(
                    agent_id=agent_id,
                    session_id=session_id,
                    session_file=session_file,
                    endpoint=endpoint,
                    api_key=api_key
                )
                
                if success:
                    self._archive_session(session_file, sessions_dir)
                    uploaded += 1
                    log.info(f"Uploaded and archived: {session_id}")
                else:
                    errors.append(f"Upload failed: {session_id}")
                    
            except Exception as e:
                errors.append(f"Error uploading {session_id}: {str(e)}")
                log.exception(f"Error uploading session {session_id}")
        
        return uploaded, errors
    
    def _get_active_sessions(self, sessions_dir: Path) -> set[str]:
        """Read active session IDs from sessions.json."""
        sessions_file = sessions_dir / "sessions.json"
        
        if not sessions_file.exists():
            return set()
        
        try:
            with open(sessions_file) as f:
                data = json.load(f)
            
            # sessions.json is a dict with session IDs as keys
            if isinstance(data, dict):
                return set(data.keys())
            return set()
            
        except Exception as e:
            log.warning(f"Error reading sessions.json: {e}")
            return set()
    
    def _find_completed_sessions(
        self,
        sessions_dir: Path,
        active_sessions: set[str]
    ) -> list[Path]:
        """Find session files that are not in active sessions."""
        completed = []
        
        for file in sessions_dir.glob("*.jsonl*"):
            if file.is_file():
                session_id = self._extract_session_id(file.name)
                if session_id and session_id not in active_sessions:
                    completed.append(file)
        
        return completed
    
    def _extract_session_id(self, filename: str) -> str | None:
        """Extract session ID (first 36 chars) from filename."""
        if len(filename) >= 36:
            session_id = filename[:36]
            # Validate it looks like a UUID
            if len(session_id) == 36 and session_id.count("-") == 4:
                return session_id
        return None
    
    # Maximum transcript size that Nexus will accept (10 MB - blob storage)
    MAX_TRANSCRIPT_BYTES = 10_485_760

    def _upload_session(
        self,
        agent_id: str,
        session_id: str,
        session_file: Path,
        endpoint: str,
        api_key: str
    ) -> bool:
        """Upload a session to Nexus."""
        # Check file size first to skip oversized files
        file_size = session_file.stat().st_size
        if file_size > self.MAX_TRANSCRIPT_BYTES:
            log.warning(
                f"Session {session_id} too large ({file_size:,} bytes), "
                f"max is {self.MAX_TRANSCRIPT_BYTES:,} bytes — skipping"
            )
            return False

        # Read transcript content
        transcript = session_file.read_text(encoding="utf-8")
        
        # POST to Nexus
        url = f"{endpoint}/sessions"
        params = {"code": api_key}
        payload = {
            "agentId": agent_id,
            "sessionId": session_id,
            "transcript": transcript
        }
        
        try:
            response = requests.post(
                url,
                params=params,
                json=payload,
                timeout=30
            )
            
            if response.status_code == 200:
                return True
            elif response.status_code == 409:
                # Already exists - treat as success, archive the file
                log.warning(f"Session already exists: {session_id}")
                return True
            elif response.status_code == 413:
                log.error(f"Session too large: {session_id}")
                return False
            else:
                log.error(f"Upload failed ({response.status_code}): {response.text}")
                return False
                
        except requests.RequestException as e:
            log.error(f"Request failed: {e}")
            return False
    
    def _archive_session(self, session_file: Path, sessions_dir: Path) -> None:
        """Move session file to archive directory."""
        archive_dir = sessions_dir / "archive"
        archive_dir.mkdir(exist_ok=True)
        
        dest = archive_dir / session_file.name
        shutil.move(str(session_file), str(dest))
