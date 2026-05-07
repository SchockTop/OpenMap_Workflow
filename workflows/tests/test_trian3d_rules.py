"""Tests for workflows.trian3d_rules — pure-Python, no bpy involvement."""
from __future__ import annotations
import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from workflows.trian3d_rules import Rule, RuleSet, first_match


class _Obj:
    """Minimal stand-in for bpy.types.Object covering name + custom-props +
    material_slots access. Subscript access is via the type so it actually
    triggers __getitem__ (a SimpleNamespace lambda would not)."""
    def __init__(self, name: str = "bldg_res_42",
                 props: dict | None = None,
                 first_material_name: str | None = None):
        self.name = name
        self._props: dict = dict(props or {})
        if first_material_name is not None:
            slot = SimpleNamespace(material=SimpleNamespace(name=first_material_name))
            self.material_slots = [slot]
        else:
            self.material_slots = []

    def __getitem__(self, key: str):
        return self._props[key]

    def __contains__(self, key: str) -> bool:
        return key in self._props

    def get(self, key: str, default=None):
        return self._props.get(key, default)


def _obj(name: str = "bldg_res_42",
         props: dict | None = None,
         first_material_name: str | None = None) -> _Obj:
    return _Obj(name=name, props=props, first_material_name=first_material_name)


# ---------------------------------------------------------------------------
# Rule.matches — name_regex
# ---------------------------------------------------------------------------

def test_name_regex_match():
    r = Rule(target="Buildings", match={"name_regex": r"^bldg_"})
    assert r.matches(_obj("bldg_res_42"))
    assert not r.matches(_obj("road_primary_3"))


def test_name_regex_uses_search_not_fullmatch():
    """Should match anywhere; rule authors anchor with ^/$ themselves."""
    r = Rule(target="X", match={"name_regex": "tree"})
    assert r.matches(_obj("veg_tree_oak_99"))
    assert r.matches(_obj("oak_tree_99"))


def test_name_regex_compiles_once():
    r = Rule(target="X", match={"name_regex": r"^bldg_"})
    r.matches(_obj("bldg_x"))
    first = r._name_re
    r.matches(_obj("bldg_y"))
    assert r._name_re is first  # cached


# ---------------------------------------------------------------------------
# Rule.matches — material_name_regex
# ---------------------------------------------------------------------------

def test_material_name_regex_match():
    r = Rule(target="X", match={"material_name_regex": r"^Roof_"})
    assert r.matches(_obj(first_material_name="Roof_Tile"))
    assert not r.matches(_obj(first_material_name="Wall_Brick"))


def test_material_name_regex_no_slots_is_no_match():
    r = Rule(target="X", match={"material_name_regex": r"."})
    assert not r.matches(_obj(first_material_name=None))


# ---------------------------------------------------------------------------
# Rule.matches — prop
# ---------------------------------------------------------------------------

def test_prop_equals():
    r = Rule(target="X", match={"prop": "osm_class", "equals": "wheat"})
    assert r.matches(_obj(props={"osm_class": "wheat"}))
    assert not r.matches(_obj(props={"osm_class": "corn"}))


def test_prop_in():
    r = Rule(target="X", match={"prop": "osm_id", "in": [1, 2, 3]})
    assert r.matches(_obj(props={"osm_id": 2}))
    assert not r.matches(_obj(props={"osm_id": 99}))


def test_prop_regex_on_string_value():
    r = Rule(target="X", match={"prop": "name_de", "regex": r"München"})
    assert r.matches(_obj(props={"name_de": "München-Süd"}))
    assert not r.matches(_obj(props={"name_de": "Berlin"}))


def test_prop_regex_coerces_value_to_string():
    r = Rule(target="X", match={"prop": "osm_id", "regex": r"^4"})
    assert r.matches(_obj(props={"osm_id": 42}))
    assert not r.matches(_obj(props={"osm_id": 99}))


def test_prop_existence_only():
    """Bare `prop` with no equals/in/regex matches if the prop exists at all."""
    r = Rule(target="X", match={"prop": "tagged"})
    assert r.matches(_obj(props={"tagged": True}))
    assert r.matches(_obj(props={"tagged": False}))   # value is irrelevant
    assert not r.matches(_obj(props={}))


def test_prop_missing_is_no_match():
    r = Rule(target="X", match={"prop": "absent", "equals": "x"})
    assert not r.matches(_obj(props={}))


# ---------------------------------------------------------------------------
# Combined predicates (AND)
# ---------------------------------------------------------------------------

def test_combined_predicates_are_and(capsys):
    """All keys present in `match` must succeed."""
    r = Rule(target="X", match={
        "name_regex": r"^bldg_",
        "prop": "stories", "equals": 5,
    })
    assert r.matches(_obj("bldg_res_1", props={"stories": 5}))
    assert not r.matches(_obj("bldg_res_1", props={"stories": 3}))
    assert not r.matches(_obj("road_x",     props={"stories": 5}))


# ---------------------------------------------------------------------------
# first_match
# ---------------------------------------------------------------------------

def test_first_match_returns_first():
    rules = [
        Rule(target="A", match={"name_regex": r"^bldg_res_"}),
        Rule(target="B", match={"name_regex": r"^bldg_"}),
    ]
    hit = first_match(rules, _obj("bldg_res_42"))
    assert hit is not None and hit.target == "A"


def test_first_match_falls_through():
    rules = [
        Rule(target="A", match={"name_regex": r"^bldg_res_"}),
        Rule(target="B", match={"name_regex": r"^bldg_"}),
    ]
    hit = first_match(rules, _obj("bldg_industrial_1"))
    assert hit is not None and hit.target == "B"


def test_first_match_returns_none_when_no_rule_matches():
    rules = [Rule(target="A", match={"name_regex": r"^road_"})]
    assert first_match(rules, _obj("bldg_res_42")) is None


# ---------------------------------------------------------------------------
# RuleSet.from_json
# ---------------------------------------------------------------------------

def test_ruleset_from_dict():
    rs = RuleSet.from_json({
        "version": 1,
        "organize":  [{"collection": "Buildings", "match": {"name_regex": r"^bldg_"}}],
        "materials": [{"material":   "Wheat",     "match": {"prop": "crop", "equals": "wheat"}}],
    })
    assert len(rs.organize) == 1 and rs.organize[0].target == "Buildings"
    assert len(rs.materials) == 1 and rs.materials[0].target == "Wheat"


def test_ruleset_from_path(tmp_path: Path):
    p = tmp_path / "rules.json"
    p.write_text(json.dumps({
        "version": 1,
        "organize": [{"collection": "X", "match": {"name_regex": "y"}}],
    }))
    rs = RuleSet.from_json(p)
    assert rs.organize[0].target == "X"


def test_ruleset_rejects_unknown_version():
    with pytest.raises(ValueError, match="version"):
        RuleSet.from_json({"version": 99, "organize": []})


def test_default_rules_json_loads_cleanly():
    """The shipped default ruleset must parse without errors."""
    repo_root = Path(__file__).resolve().parent.parent.parent
    default = repo_root / "workflows" / "trian3d_default_rules.json"
    assert default.is_file(), default
    rs = RuleSet.from_json(default)
    # Should have a non-trivial organize list, no surprise materials.
    assert len(rs.organize) >= 5
    assert all(rule.target.startswith(("Buildings/", "Vegetation/", "Roads/",
                                       "Water/", "Land use/", "Reference"))
               for rule in rs.organize)
