"""Runs tests/fixtures/tag_cases.json against the Python tag model.

The C# engine mirrors sidecar/factoryforge_sidecar/tags.py by hand (see the
comment on engine/src/TagBus/Tag.cs). Nothing enforced that agreement before
this -- both sides were diffed by eye once and left to drift. This test and its
C# counterpart (`godot --self-test=parity`, engine/src/Sim/TagParitySelfTest.cs)
run the identical fixture against both models, so a future edit to one side's
coercion rules that forgets the other fails a test instead of shipping quietly.
See FF-29.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from factoryforge_sidecar.tags import Tag, TagError

FIXTURE = json.loads(
    (Path(__file__).resolve().parent / "fixtures" / "tag_cases.json").read_text(encoding="utf-8")
)


def _probe(tag_type: str, value=None) -> Tag:
    return Tag(id="case", name="case", type=tag_type, kind="output", value=value)


@pytest.mark.parametrize("case", FIXTURE["coerce"], ids=lambda c: f"{c['type']}:{c['input']!r}")
def test_coerce(case: dict) -> None:
    tag = _probe(case["type"])
    if case.get("error"):
        with pytest.raises(TagError):
            tag.coerce(case["input"])
    else:
        assert tag.coerce(case["input"]) == case["expect"]


@pytest.mark.parametrize(
    "case", FIXTURE["differs"],
    ids=lambda c: f"{c['type']}:{c['current']!r}->{c['candidate']!r}",
)
def test_differs(case: dict) -> None:
    tag = _probe(case["type"], case["current"])
    assert tag.differs(case["candidate"]) == case["expect"]
