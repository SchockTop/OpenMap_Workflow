"""trian3d_rules.py — rule schema + matcher for TRIAN3D-imported scenes.

A `Rule` describes: "find objects matching X, do Y to them." Rules are
loaded from JSON and applied by `trian3d_apply.py` against a Blender scene.

Two top-level rule kinds, both sharing the same `match` predicate schema:

    organize:    {"collection": "Buildings/Residential", "match": {...}}
    materials:   {"material":   "Field_Wheat",           "match": {...}}

Match predicate (any one key per match dict):

    {"name_regex":          "<re>"}     — match object.name
    {"material_name_regex": "<re>"}     — match first material slot's name
    {"prop":                "<key>",
     "equals" | "in" | "regex": <val>}  — match custom property

Rules are evaluated in the order they appear; the first match wins. This
file is pure-Python (no bpy) so the matcher can be exercised in tests
without Blender.
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Optional, Protocol


# ---------------------------------------------------------------------------
# A minimal protocol describing the slice of bpy.types.Object we touch. Tests
# can pass any object with these attributes (a SimpleNamespace works fine).
# ---------------------------------------------------------------------------

class ObjectLike(Protocol):
    name: str
    def __getitem__(self, key: str) -> Any: ...
    def get(self, key: str, default: Any = None) -> Any: ...


@dataclass
class Rule:
    """A single rule. `target` is the collection-name (organize) or
    material-name (materials). `match` is a predicate dict."""
    target: str
    match: dict[str, Any]
    # Compiled regex caches — lazy.
    _name_re: Optional[re.Pattern] = field(default=None, init=False, repr=False)
    _mat_re:  Optional[re.Pattern] = field(default=None, init=False, repr=False)
    _prop_re: Optional[re.Pattern] = field(default=None, init=False, repr=False)

    def matches(self, obj: ObjectLike) -> bool:
        m = self.match
        if "name_regex" in m:
            if self._name_re is None:
                self._name_re = re.compile(m["name_regex"])
            if not self._name_re.search(obj.name):
                return False
        if "material_name_regex" in m:
            if self._mat_re is None:
                self._mat_re = re.compile(m["material_name_regex"])
            slots = getattr(obj, "material_slots", None) or []
            first_mat = None
            if slots:
                slot0 = slots[0]
                first_mat = getattr(getattr(slot0, "material", None), "name", None)
            if not first_mat or not self._mat_re.search(first_mat):
                return False
        if "prop" in m:
            key = m["prop"]
            try:
                value = obj[key]
            except (KeyError, TypeError):
                return False
            if "equals" in m:
                if value != m["equals"]:
                    return False
            elif "in" in m:
                if value not in m["in"]:
                    return False
            elif "regex" in m:
                if self._prop_re is None:
                    self._prop_re = re.compile(m["regex"])
                if not self._prop_re.search(str(value)):
                    return False
            else:
                # `prop` alone (existence check)
                pass
        return True


@dataclass
class RuleSet:
    organize:  list[Rule] = field(default_factory=list)
    materials: list[Rule] = field(default_factory=list)

    @classmethod
    def from_json(cls, payload: dict | str | Path) -> "RuleSet":
        """Load a ruleset from a JSON dict, string, or file path."""
        if isinstance(payload, (str, Path)):
            data = json.loads(Path(payload).read_text())
        elif isinstance(payload, dict):
            data = payload
        else:
            raise TypeError(f"unsupported payload type: {type(payload)!r}")
        if data.get("version", 1) != 1:
            raise ValueError(f"unsupported ruleset version: {data.get('version')!r}")
        return cls(
            organize=[Rule(target=r["collection"], match=r["match"])
                      for r in data.get("organize", [])],
            materials=[Rule(target=r["material"], match=r["match"])
                       for r in data.get("materials", [])],
        )


def first_match(rules: list[Rule], obj: ObjectLike) -> Optional[Rule]:
    """Return the first rule that matches `obj`, or None. Rule order is
    significant — put more-specific rules first."""
    for rule in rules:
        if rule.matches(obj):
            return rule
    return None
