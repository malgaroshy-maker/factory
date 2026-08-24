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


# --- what a frozen release can actually run -----------------------------------

def test_usable_distinguishes_registered_from_runnable():
    """A driver can be registered and still unable to run.

    Every protocol driver guards its own third-party import and registers
    regardless, so it can explain itself at connect time rather than vanishing
    from the CLI. That is the right behaviour and it makes `available()` a
    misleading answer to "what does this build support" -- which matters for a
    PyInstaller release, because it bundles whatever was importable when it was
    built. A release built without an extra ships a driver that is present,
    listed, and dead (see tools/packaging/check_release.py).
    """
    from factoryforge_sidecar import drivers

    report = drivers.usable()
    assert set(report) == set(drivers.available()), "every registered driver is reported"
    assert all(isinstance(ok, bool) for ok in report.values())

    # mock and modbus-tcp have no third-party dependency at all, so they are
    # usable in any build -- including a CI one with no extras installed.
    assert report["mock"] is True
    assert report["modbus-tcp"] is True


def test_usable_tracks_the_dependency_flag_each_driver_sets():
    from factoryforge_sidecar import drivers
    from factoryforge_sidecar.drivers import plcsim_advanced, s7_snap7

    report = drivers.usable()
    assert report["s7-snap7"] == s7_snap7.HAS_SNAP7
    assert report["plcsim-advanced"] == plcsim_advanced.HAS_PYTHONNET
