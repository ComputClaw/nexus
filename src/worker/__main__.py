"""Entry point for the Nexus Worker."""

import argparse
import logging
import sys

from config import load_config
from scheduler import run_scheduler, run_single_job


def setup_logging(verbose: bool = False) -> None:
    """Configure logging."""
    level = logging.DEBUG if verbose else logging.INFO
    
    logging.basicConfig(
        level=level,
        format="%(asctime)s [%(levelname)s] [%(name)s] %(message)s",
        datefmt="%Y-%m-%dT%H:%M:%S"
    )


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Nexus Worker - syncs data between OpenClaw and Nexus"
    )
    parser.add_argument(
        "--config",
        default="config.json",
        help="Path to config file (default: config.json)"
    )
    parser.add_argument(
        "--job",
        help="Run specific job by ID (ignores intervalMinutes)"
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Enable debug logging"
    )
    
    args = parser.parse_args()
    
    setup_logging(args.verbose)
    log = logging.getLogger(__name__)
    
    # Load config
    try:
        config = load_config(args.config)
    except FileNotFoundError as e:
        log.error(str(e))
        return 1
    except ValueError as e:
        log.error(f"Config error: {e}")
        return 1
    except Exception as e:
        log.exception("Failed to load config")
        return 1
    
    # Run single job or scheduler
    if args.job:
        result = run_single_job(config, args.job)
        if result is None:
            return 1
        return 0 if result.success else 1
    else:
        try:
            run_scheduler(config)
        except KeyboardInterrupt:
            log.info("Shutting down")
        return 0


if __name__ == "__main__":
    sys.exit(main())
