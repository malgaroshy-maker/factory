"""Unit tests for Siemens native protocol drivers (PLCSIM Advanced API & Snap7)."""

from unittest.mock import MagicMock
import pytest
from factoryforge_sidecar import drivers


def test_siemens_drivers_registered() -> None:
    available = drivers.available()
    assert "plcsim-advanced" in available
    assert "s7-snap7" in available
    assert "mock" in available
    assert "modbus-tcp" in available


@pytest.mark.asyncio
async def test_plcsim_advanced_driver_lifecycle() -> None:
    mock_bus = MagicMock()
    drv = drivers.create("plcsim-advanced", mock_bus)
    assert drv.driver_name == "plcsim-advanced"
    assert hasattr(drv, "instance_name")


@pytest.mark.asyncio
async def test_s7_snap7_driver_lifecycle() -> None:
    mock_bus = MagicMock()
    drv = drivers.create("s7-snap7", mock_bus)
    assert drv.driver_name == "s7-snap7"
    assert hasattr(drv, "host")
    assert hasattr(drv, "db_number")
