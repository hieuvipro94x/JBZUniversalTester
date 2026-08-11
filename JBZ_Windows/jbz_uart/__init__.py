"""Tự động phát hiện và nhận diện bo JBZ Universal Tester qua UART."""

from .manager import (
    ProbeResult,
    UartDiscoveryError,
    UartManager,
    candidate_ports,
    probe_jbz_port,
)

__all__ = [
    "ProbeResult",
    "UartDiscoveryError",
    "UartManager",
    "candidate_ports",
    "probe_jbz_port",
]
