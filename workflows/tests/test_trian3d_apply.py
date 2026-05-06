"""Tests for workflows.trian3d_apply.

bpy isn't available in this sandbox, so we run the bpy-dependent functions
against a hand-rolled fake-bpy that captures the operations we care about
(collection creation, object linking, material assignment, mesh datablock
identity).
"""
from __future__ import annotations
from types import SimpleNamespace

import pytest

from workflows.trian3d_rules import Rule
from workflows.trian3d_apply import (
    split_collection_path,
    mesh_signature,
    ensure_collection_path,
    organize_scene,
    apply_material_rules,
    collapse_to_linked_data,
)


# ---------------------------------------------------------------------------
# Pure-Python helpers
# ---------------------------------------------------------------------------

class TestSplitCollectionPath:
    def test_simple(self):
        assert split_collection_path("Buildings") == ["Buildings"]

    def test_nested(self):
        assert split_collection_path("A/B/C") == ["A", "B", "C"]

    def test_strips_empty_segments(self):
        assert split_collection_path("/A//B/") == ["A", "B"]


class TestMeshSignature:
    def test_same_inputs_same_signature(self):
        a = mesh_signature(10, 20, 5, (0.0, 0.0, 0.0))
        b = mesh_signature(10, 20, 5, (0.0, 0.0, 0.0))
        assert a == b

    def test_different_vert_count_different_signature(self):
        a = mesh_signature(10, 20, 5, (0.0, 0.0, 0.0))
        b = mesh_signature(11, 20, 5, (0.0, 0.0, 0.0))
        assert a != b

    def test_different_first_vertex_different_signature(self):
        a = mesh_signature(10, 20, 5, (0.0, 0.0, 0.0))
        b = mesh_signature(10, 20, 5, (1.0, 0.0, 0.0))
        assert a != b


# ---------------------------------------------------------------------------
# Fake-bpy harness
# ---------------------------------------------------------------------------

class FakeObjectsView:
    def __init__(self, owner):
        self.owner = owner

    def link(self, obj):
        if obj not in self.owner._objects:
            self.owner._objects.append(obj)
            if self.owner not in obj.users_collection:
                obj.users_collection.append(self.owner)

    def unlink(self, obj):
        if obj in self.owner._objects:
            self.owner._objects.remove(obj)
        if self.owner in obj.users_collection:
            obj.users_collection.remove(self.owner)

    def __iter__(self):
        return iter(self.owner._objects)

    def __contains__(self, obj):
        return obj in self.owner._objects

    def __len__(self):
        return len(self.owner._objects)


class FakeChildrenView:
    def __init__(self, owner):
        self.owner = owner

    def link(self, child):
        if child in self.owner._children:
            raise RuntimeError("already linked")
        self.owner._children.append(child)

    def __iter__(self):
        return iter(self.owner._children)

    def __contains__(self, child):
        return child in self.owner._children


class FakeCollection:
    def __init__(self, name: str):
        self.name = name
        self._objects: list = []
        self._children: list = []
        self.objects = FakeObjectsView(self)
        self.children = FakeChildrenView(self)


class FakeMaterial:
    def __init__(self, name: str):
        self.name = name


class FakeMaterialSlot:
    def __init__(self, material):
        self.material = material


class FakeMeshMaterials(list):
    """list with .append() that mirrors mesh.materials in bpy."""
    def append(self, m):
        super().append(m)


class FakeObject:
    def __init__(self, name: str,
                 props: dict | None = None,
                 mesh=None,
                 first_material: str | None = None):
        self.name = name
        self._props: dict = dict(props or {})
        self.users_collection: list = []
        self.material_slots: list = []
        if first_material is not None:
            self.material_slots.append(FakeMaterialSlot(FakeMaterial(first_material)))
        self.data = mesh

    def __getitem__(self, key):
        return self._props[key]

    def __contains__(self, key):
        return key in self._props

    def get(self, key, default=None):
        return self._props.get(key, default)


def make_fake_mesh(verts: int, edges: int, polys: int,
                   first_vert: tuple = (0.0, 0.0, 0.0)):
    vert_objs = [SimpleNamespace(co=first_vert if i == 0 else (i, 0, 0))
                 for i in range(verts)]
    return SimpleNamespace(
        vertices=vert_objs,
        edges=[SimpleNamespace() for _ in range(edges)],
        polygons=[SimpleNamespace() for _ in range(polys)],
        materials=FakeMeshMaterials(),
    )


class FakeRegistry:
    def __init__(self, factory):
        self._items: dict = {}
        self._factory = factory

    def new(self, name):
        item = self._factory(name)
        self._items[name] = item
        return item

    def get(self, name, default=None):
        return self._items.get(name, default)

    def __contains__(self, name):
        return name in self._items


class FakeBpy:
    def __init__(self):
        self.context = SimpleNamespace(
            scene=SimpleNamespace(
                collection=FakeCollection("Scene Collection"),
                objects=[],
            ),
        )
        self.data = SimpleNamespace(
            collections=FakeRegistry(FakeCollection),
            materials=FakeRegistry(FakeMaterial),
        )

    def add_object(self, obj: FakeObject):
        self.context.scene.objects.append(obj)
        self.context.scene.collection.objects.link(obj)


@pytest.fixture
def fake_bpy(monkeypatch):
    fb = FakeBpy()
    from workflows import trian3d_apply
    monkeypatch.setattr(trian3d_apply, "_require_bpy", lambda: fb)
    return fb


# ---------------------------------------------------------------------------
# ensure_collection_path
# ---------------------------------------------------------------------------

class TestEnsureCollectionPath:
    def test_creates_single_collection(self, fake_bpy):
        leaf = ensure_collection_path(fake_bpy, "Buildings")
        assert leaf.name == "Buildings"
        assert fake_bpy.data.collections.get("Buildings") is leaf
        assert leaf in fake_bpy.context.scene.collection.children

    def test_creates_nested_path(self, fake_bpy):
        leaf = ensure_collection_path(fake_bpy, "Buildings/Residential")
        bldg = fake_bpy.data.collections.get("Buildings")
        res = fake_bpy.data.collections.get("Residential")
        assert leaf is res
        assert res in bldg.children

    def test_idempotent(self, fake_bpy):
        a = ensure_collection_path(fake_bpy, "Buildings/Residential")
        b = ensure_collection_path(fake_bpy, "Buildings/Residential")
        assert a is b

    def test_empty_path_raises(self, fake_bpy):
        with pytest.raises(ValueError):
            ensure_collection_path(fake_bpy, "")


# ---------------------------------------------------------------------------
# organize_scene
# ---------------------------------------------------------------------------

class TestOrganizeScene:
    def test_moves_matched_objects(self, fake_bpy):
        a = FakeObject("bldg_res_1")
        b = FakeObject("road_main_2")
        fake_bpy.add_object(a); fake_bpy.add_object(b)
        rules = [
            Rule(target="Buildings", match={"name_regex": r"^bldg_"}),
            Rule(target="Roads",     match={"name_regex": r"^road_"}),
        ]
        counts = organize_scene(rules, unmatched_collection=None)
        assert counts == {"Buildings": 1, "Roads": 1}
        assert a in fake_bpy.data.collections.get("Buildings").objects
        assert b in fake_bpy.data.collections.get("Roads").objects

    def test_unmatched_goes_to_unmatched_collection(self, fake_bpy):
        a = FakeObject("mystery_1")
        fake_bpy.add_object(a)
        organize_scene([Rule(target="Buildings", match={"name_regex": r"^bldg_"})])
        unmatched = fake_bpy.data.collections.get("Unmatched")
        assert unmatched is not None
        assert a in unmatched.objects

    def test_unmatched_left_alone_when_disabled(self, fake_bpy):
        a = FakeObject("mystery_1")
        fake_bpy.add_object(a)
        organize_scene([], unmatched_collection=None)
        assert fake_bpy.data.collections.get("Unmatched") is None
        assert a in fake_bpy.context.scene.collection.objects

    def test_object_unlinks_from_old_collection(self, fake_bpy):
        a = FakeObject("bldg_res_1")
        fake_bpy.add_object(a)
        organize_scene([Rule(target="Buildings", match={"name_regex": r"^bldg_"})])
        assert a not in fake_bpy.context.scene.collection.objects
        assert a in fake_bpy.data.collections.get("Buildings").objects

    def test_first_rule_wins(self, fake_bpy):
        a = FakeObject("bldg_res_1")
        fake_bpy.add_object(a)
        rules = [
            Rule(target="Specific", match={"name_regex": r"^bldg_res_"}),
            Rule(target="Generic",  match={"name_regex": r"^bldg_"}),
        ]
        organize_scene(rules, unmatched_collection=None)
        assert a in fake_bpy.data.collections.get("Specific").objects
        assert fake_bpy.data.collections.get("Generic") is None


# ---------------------------------------------------------------------------
# apply_material_rules
# ---------------------------------------------------------------------------

class TestApplyMaterialRules:
    def test_replaces_slot_zero_when_slot_exists(self, fake_bpy):
        wheat = fake_bpy.data.materials.new("Wheat")
        a = FakeObject("field_42", props={"crop": "wheat"},
                       first_material="Default")
        fake_bpy.add_object(a)
        counts = apply_material_rules([
            Rule(target="Wheat", match={"prop": "crop", "equals": "wheat"}),
        ])
        assert counts == {"Wheat": 1}
        assert a.material_slots[0].material is wheat

    def test_appends_to_mesh_when_no_slot_exists(self, fake_bpy):
        wheat = fake_bpy.data.materials.new("Wheat")
        a = FakeObject("field_42", props={"crop": "wheat"},
                       mesh=make_fake_mesh(0, 0, 0))
        fake_bpy.add_object(a)
        apply_material_rules([
            Rule(target="Wheat", match={"prop": "crop", "equals": "wheat"}),
        ])
        assert wheat in a.data.materials

    def test_warns_on_missing_material(self, fake_bpy, capsys):
        a = FakeObject("field_42", props={"crop": "wheat"})
        fake_bpy.add_object(a)
        counts = apply_material_rules([
            Rule(target="DoesNotExist", match={"prop": "crop", "equals": "wheat"}),
        ])
        assert counts == {}
        assert "DoesNotExist" in capsys.readouterr().out

    def test_no_match_no_change(self, fake_bpy):
        fake_bpy.data.materials.new("Wheat")
        a = FakeObject("road_1", first_material="Asphalt")
        fake_bpy.add_object(a)
        apply_material_rules([Rule(target="Wheat", match={"name_regex": "^field_"})])
        assert a.material_slots[0].material.name == "Asphalt"


# ---------------------------------------------------------------------------
# collapse_to_linked_data
# ---------------------------------------------------------------------------

class TestCollapseToLinkedData:
    def test_collapses_identical_meshes(self, fake_bpy):
        m1 = make_fake_mesh(verts=10, edges=15, polys=5, first_vert=(1.0, 2.0, 3.0))
        m2 = make_fake_mesh(verts=10, edges=15, polys=5, first_vert=(1.0, 2.0, 3.0))
        m3 = make_fake_mesh(verts=10, edges=15, polys=5, first_vert=(1.0, 2.0, 3.0))
        a = FakeObject("a", mesh=m1)
        b = FakeObject("b", mesh=m2)
        c = FakeObject("c", mesh=m3)
        for obj in (a, b, c):
            fake_bpy.add_object(obj)
        relinked, unique = collapse_to_linked_data()
        assert relinked == 2
        assert unique == 1
        assert b.data is m1 and c.data is m1

    def test_keeps_different_meshes_separate(self, fake_bpy):
        m1 = make_fake_mesh(verts=10, edges=15, polys=5)
        m2 = make_fake_mesh(verts=20, edges=30, polys=10)
        a = FakeObject("a", mesh=m1)
        b = FakeObject("b", mesh=m2)
        fake_bpy.add_object(a); fake_bpy.add_object(b)
        relinked, unique = collapse_to_linked_data()
        assert (relinked, unique) == (0, 0)

    def test_skips_objects_without_mesh(self, fake_bpy):
        a = FakeObject("light_1", mesh=None)
        b = FakeObject("light_2", mesh=None)
        fake_bpy.add_object(a); fake_bpy.add_object(b)
        assert collapse_to_linked_data() == (0, 0)

    def test_collection_scope_limits_search(self, fake_bpy):
        m1 = make_fake_mesh(verts=10, edges=15, polys=5)
        m2 = make_fake_mesh(verts=10, edges=15, polys=5)
        a = FakeObject("bldg_1", mesh=m1)
        b = FakeObject("road_1", mesh=m2)
        fake_bpy.add_object(a); fake_bpy.add_object(b)
        organize_scene([
            Rule(target="Buildings", match={"name_regex": r"^bldg_"}),
            Rule(target="Roads",     match={"name_regex": r"^road_"}),
        ])
        relinked, unique = collapse_to_linked_data(collection_name="Buildings")
        # Only one building → singleton bucket, nothing to collapse.
        assert (relinked, unique) == (0, 0)
