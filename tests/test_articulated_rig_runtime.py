# !/usr/bin/python
# coding=utf-8
"""Integration test: the articulated-rig runtime inside a real Unity (no DCC).

The ArticulatedRig feature's Model part (``ArticulationModel.cs``) is a C# port
of pythontk's ``ArticulationModel`` (articulation v2: an anchor frame on every
joint); its components (``ArticulatedJoint`` on each part, ``ArticulatedRig``
on the rig's root) hold the rig in Unity's terms, which the importer fills
from the record by reference. This suite holds the port to the reference and
the components to their contract with one batch Unity run:

    1. **Conformance.** ``ptk.ArticulationConformance.cases()`` is generated
       HERE, at test time (never a checked-in copy, so it cannot drift from
       the reference), handed to Unity, and every case runs through the C#
       model: pose, world, read, every solve -- the anchored arm, the flat
       model and the anchored tree among them. Compared within the
       document's own tolerances (quaternions sign-insensitive). The port's
       record validation is checked against the reference's ValueErrors.
    2. **The chain in Unity's axes.** One case per rig, stated as Unity's FBX
       import lands it (X mirrored: Python spells the mirror independently of
       the C# it checks) and filled into components on a real hierarchy under
       a rotated, scaled root -- once nested on the chain, once FLAT (every
       part a child of the rig's root): ``ApplyState`` poses every part at the
       mirror of the golden rig-space world, whatever node it hangs under,
       and the state reads back off the transforms.
    3. **The rig.** The conformance ``arm`` as components under a rotated,
       scaled root: the state read off the transforms, grabs in each rotation
       mode -- every solved state must equal pythontk's solve from the same
       inputs (mirrored back), and the grabbed point must land on the world
       target through the real transforms -- the held pose, and the pointer
       grabber's ray / plane / scroll path through real physics. The
       Animator override without play mode: each frame is an edit-mode
       ``Animator.Update(dt)`` playing a clip, then the rig's own
       ``LateUpdate`` (by reflection) -- the held and released pose must win
       over the clip, and ``ReturnToAnimation`` / ``resumeOnAnimatorChange``
       hand it back.
    4. **Binding by name.** Hierarchies through the importer's own wiring:
       exact, else namespace-stripped leaf; a grab part under its own joint;
       a tie, or root joints under different nodes, left unbound and
       reported, never guessed.
    5. **Importer.** A hand-written ASCII FBX in a DCC's common convention
       (Y-up, right-handed, CENTIMETRES, joint orients as PreRotation, the
       rotate order as RotationOrder) carrying a v2 record in METRES on
       ``data_export``: the components land by reference, colliders go on the
       grab parts' meshes only, every imported joint's local is the record's
       pose mirrored -- Unity's FBX import frames the file in metres, so the
       record's metres apply as they are -- and reads back as the state
       written; a version 1 re-export is refused, and one without the carrier
       drops the components.
    6. **A real DCC export** (env-gated, ``ARTICULATED_RIG_LIVE_FBX``: an FBX
       a producer's export pipeline wrote, with the record; machine-local,
       never checked in): the importer binds it, every imported joint's
       local IS a channel pose of the record (which holds only where Unity
       frames the joints as the record states them, in metres), every clip
       sample is a channel pose, and a Hand grab on its part lands.

A second class drives a project with the XR Interaction Toolkit 3.x installed
(downloaded by Unity's package manager): the XRI door's assemblies must compile
there -- and stay out of the default project above -- and its setup helper
and grab forwarding must drive the rig; the Input System door compiles there
too and registers its pointer through the grabber's seam.

A third runs the same golden cases through the model under ``dotnet test``
(``tests/models_dotnet``), no Unity needed: the port is engine-free.

Requires:
    - Unity Hub with at least one editor installed (skips on license wall)
    - network access for the XRI class (skips when the package cannot resolve)
"""

import json
import math
import os
import random
import re
import shutil
import struct
import subprocess
import sys
import unittest
import logging

logger = logging.getLogger(__name__)

import base_test  # noqa: F401 — sys.path bootstrap for the sibling repos
from base_test import EnvSkip, PackageSources, TempProjects, UnityEnv

import pythontk as ptk

from unitytk.project_builder import ProjectBuilder
from unitytk.unity_host import UnityHost


# ======================================================================
# Quaternion helpers (Python side of the expectations)
# ======================================================================


class Q:
    """Plain (x, y, z, w) quaternion / 3-vector math, the model's formulas."""

    @staticmethod
    def mul(a, b):
        ax, ay, az, aw = a
        bx, by, bz, bw = b
        return (
            aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz,
        )

    @staticmethod
    def conj(q):
        return (-q[0], -q[1], -q[2], q[3])

    @staticmethod
    def rot(q, v):
        u = q[:3]
        t = [2.0 * c for c in Q.cross(u, v)]
        c2 = Q.cross(u, t)
        return tuple(v[i] + q[3] * t[i] + c2[i] for i in range(3))

    @staticmethod
    def cross(a, b):
        return (
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        )

    @staticmethod
    def normalize(q):
        n = math.sqrt(sum(c * c for c in q))
        return tuple(c / n for c in q)

    @staticmethod
    def axis_angle(axis, degrees):
        n = math.sqrt(sum(c * c for c in axis))
        h = math.radians(degrees) * 0.5
        s = math.sin(h) / n
        return (axis[0] * s, axis[1] * s, axis[2] * s, math.cos(h))

    @staticmethod
    def euler_xyz(q):
        """Degrees (rx, ry, rz), x applied first (M = Rz Ry Rx) -- an FBX
        PreRotation / a DCC's joint orient."""
        x, y, z, w = q
        m20 = 2 * (x * z - y * w)
        m21 = 2 * (y * z + x * w)
        m22 = 1 - 2 * (x * x + y * y)
        m10 = 2 * (x * y + z * w)
        m00 = 1 - 2 * (y * y + z * z)
        m12 = 2 * (y * z - x * w)
        m11 = 1 - 2 * (x * x + z * z)
        ry = math.asin(max(-1.0, min(1.0, -m20)))
        if math.cos(ry) > 1e-6:
            rx = math.atan2(m21, m22)
            rz = math.atan2(m10, m00)
        else:
            rx = math.atan2(-m12, m11)
            rz = 0.0
        return [math.degrees(rx), math.degrees(ry), math.degrees(rz)]

    @staticmethod
    def angle(a, b):
        """Degrees between two rotations (sign-insensitive)."""
        d = abs(sum(x * y for x, y in zip(Q.normalize(a), Q.normalize(b))))
        return math.degrees(2.0 * math.acos(min(1.0, d)))


class Unity:
    """Unity's axes, spelled independently of the C# they check: Unity's FBX
    import mirrors X -- a point (x, y, z) is (-x, y, z), a rotation
    (x, y, z, w) is (x, -y, -z, w) -- so a turn about y or z, and a slide
    along x, runs the other way there (:attr:`FLIPPED`). Every map here is its
    own inverse. Lengths are metres on both sides."""

    #: The channels whose value (and limits) change sign in Unity's axes.
    FLIPPED = ("ry", "rz", "tx")

    @staticmethod
    def point(p):
        return (-p[0], p[1], p[2])

    @staticmethod
    def rotation(q):
        return (q[0], -q[1], -q[2], q[3])

    @classmethod
    def signs(cls, model):
        """Per state slot, the sign that takes a value to Unity's (and back)."""
        return [-1.0 if c in cls.FLIPPED else 1.0 for _j, c in model.channels]

    @classmethod
    def state(cls, model, state):
        """*state* in Unity's terms (or back: the map is an involution)."""
        return [v * s for v, s in zip(state, cls.signs(model))]

    @classmethod
    def rig(cls, rig):
        """*rig* (a record's) as the components hold it: every rest and anchor
        mirrored, each flipped channel's limits negated and swapped -- what the
        importer fills, spelled here on its own."""
        out = json.loads(json.dumps(rig))
        for joint in out["joints"]:
            joint["t"] = list(cls.point(joint["t"]))
            joint["q"] = list(cls.rotation(joint["q"]))
            joint["anchor"] = {
                "t": list(cls.point(joint["anchor"]["t"])),
                "q": list(cls.rotation(joint["anchor"]["q"])),
            }
            for channel in joint["channels"]:
                if channel["channel"] in cls.FLIPPED:
                    lo, hi = channel["min"], channel["max"]
                    channel["min"] = None if hi is None else -hi
                    channel["max"] = None if lo is None else -lo
        return out

    @staticmethod
    def world_point(frames, p):
        """*p* (a Unity-local point of the last frame) through a chain of
        ``(position, rotation, uniform scale)`` parent frames, outermost first."""
        v = tuple(p)
        for pos, rot, scale in reversed(frames):
            v = Q.rot(rot, tuple(scale * c for c in v))
            v = tuple(pos[i] + v[i] for i in range(3))
        return v

    @staticmethod
    def world_rotation(frames, q):
        out = tuple(q)
        for _pos, rot, _scale in reversed(frames):
            out = Q.mul(rot, out)
        return out


def _close_quat(a, b):
    return min(
        max(abs(x - y) for x, y in zip(a, b)), max(abs(x + y) for x, y in zip(a, b))
    )


def _max_diff(a, b):
    return max((abs(x - y) for x, y in zip(a, b)), default=0.0)


# ======================================================================
# The ASCII FBX fixture (a DCC's common axes and units, hand-written)
# ======================================================================


class AsciiFbx:
    """A minimal FBX 7.4 ASCII writer -- just enough for Unity's importer to
    build a node hierarchy with joints, a mesh and a string user property.

    A DCC's common convention on purpose: Y-up right-handed axes, centimetres
    (``UnitScaleFactor`` 1), a joint's orient as ``PreRotation`` and its
    rotate order as ``RotationOrder`` -- the import then applies exactly the
    conversions such an export meets (X mirror, cm -> m). A string property's
    double quotes travel as ``&quot;``, FBX ASCII's escape.
    """

    #: Rotate order -> FBX ``RotationOrder`` enum.
    ORDER = {"xyz": 0, "xzy": 1, "yzx": 2, "yxz": 3, "zxy": 4, "zyx": 5}

    CUBE = (
        [
            (-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1),
            (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1),
        ],
        [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (0, 4, 7, 3)],
    )  # fmt: skip

    def __init__(self):
        self.nodes = []
        self._next = 1000

    def add(self, name, kind="Null", parent=0, t=(0, 0, 0), pre=(0, 0, 0),
            r=(0, 0, 0), order="xyz", mesh=None, user=None):  # fmt: skip
        """Add a node; returns its id (the *parent* of later nodes; 0 = scene root)."""
        self._next += 1
        self.nodes.append(
            dict(id=self._next, name=name, kind=kind, parent=parent, t=t, pre=pre,
                 r=r, order=self.ORDER[order], mesh=mesh, user=user or {})
        )  # fmt: skip
        return self._next

    @staticmethod
    def _floats(values):
        return ",".join(repr(float(v)) for v in values)

    def text(self) -> str:
        out = []
        w = out.append
        w("; FBX 7.4.0 project file")
        w("FBXHeaderExtension:  {")
        w("\tFBXHeaderVersion: 1003")
        w("\tFBXVersion: 7400")
        w('\tCreator: "unitytk test fixture"')
        w("}")
        w("GlobalSettings:  {")
        w("\tVersion: 1000")
        w("\tProperties70:  {")
        for key, value in (
            ("UpAxis", 1), ("UpAxisSign", 1), ("FrontAxis", 2), ("FrontAxisSign", 1),
            ("CoordAxis", 0), ("CoordAxisSign", 1), ("OriginalUpAxis", 1),
            ("OriginalUpAxisSign", 1),
        ):  # fmt: skip
            w(f'\t\tP: "{key}", "int", "Integer", "",{value}')
        w('\t\tP: "UnitScaleFactor", "double", "Number", "",1')
        w('\t\tP: "OriginalUnitScaleFactor", "double", "Number", "",1')
        w("\t}")
        w("}")
        meshes = [n for n in self.nodes if n["mesh"]]
        limbs = [n for n in self.nodes if n["kind"] == "LimbNode"]
        w("Definitions:  {")
        w("\tVersion: 100")
        w(f"\tCount: {1 + len(self.nodes) + len(meshes) + len(limbs)}")
        for kind, count in (
            ("GlobalSettings", 1), ("Model", len(self.nodes)),
            ("Geometry", len(meshes)), ("NodeAttribute", len(limbs)),
        ):  # fmt: skip
            if count:
                w(f'\tObjectType: "{kind}" {{')
                w(f"\t\tCount: {count}")
                w("\t}")
        w("}")
        w("Objects:  {")
        links = []
        for n in self.nodes:
            if n["kind"] == "LimbNode":
                aid = n["id"] + 500000
                w(f'\tNodeAttribute: {aid}, "NodeAttribute::", "LimbNode" {{')
                w('\t\tTypeFlags: "Skeleton"')
                w("\t}")
                links.append((aid, n["id"]))
            if n["mesh"]:
                gid = n["id"] + 700000
                verts, polys = n["mesh"]
                index = []
                for poly in polys:
                    index += list(poly[:-1]) + [-(poly[-1] + 1)]
                w(f'\tGeometry: {gid}, "Geometry::", "Mesh" {{')
                w(f"\t\tVertices: *{3 * len(verts)} {{")
                w(f"\t\t\ta: {self._floats(c for p in verts for c in p)}")
                w("\t\t}")
                w(f"\t\tPolygonVertexIndex: *{len(index)} {{")
                w(f"\t\t\ta: {','.join(str(i) for i in index)}")
                w("\t\t}")
                w("\t\tGeometryVersion: 124")
                w("\t}")
                links.append((gid, n["id"]))
            kind = "Mesh" if n["mesh"] else n["kind"]
            w(f'\tModel: {n["id"]}, "Model::{n["name"]}", "{kind}" {{')
            w("\t\tVersion: 232")
            w("\t\tProperties70:  {")
            w('\t\t\tP: "RotationActive", "bool", "", "",1')
            w('\t\t\tP: "InheritType", "enum", "", "",1')
            w(f'\t\t\tP: "RotationOrder", "enum", "", "",{n["order"]}')
            w(
                f'\t\t\tP: "PreRotation", "Vector3D", "Vector", "",{self._floats(n["pre"])}'
            )
            w(
                f'\t\t\tP: "Lcl Translation", "Lcl Translation", "", "A",{self._floats(n["t"])}'
            )
            w(
                f'\t\t\tP: "Lcl Rotation", "Lcl Rotation", "", "A",{self._floats(n["r"])}'
            )
            for key, value in n["user"].items():
                escaped = value.replace('"', "&quot;")
                w(f'\t\t\tP: "{key}", "KString", "", "U", "{escaped}"')
            w("\t\t}")
            w("\t\tShading: Y")
            w('\t\tCulling: "CullingOff"')
            w("\t}")
            links.append((n["id"], n["parent"]))
        w("}")
        w("Connections:  {")
        for child, parent in links:
            w(f'\tC: "OO",{child},{parent}')
        w("}")
        return "\n".join(out) + "\n"

    def write(self, path: str) -> None:
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(self.text())


# ======================================================================
# Scenario builders
# ======================================================================

#: The conformance document's parameters.
SEED = 0
PER_RIG = 4
#: The rig scenario's metres per conformance unit: the arm about 11 m long, at
#: the scale the camera and the parts below are placed for.
ARM_METRES = 0.37
#: The FBX fixture's joint state seed.
FIXTURE_SEED = 7


def _random_state(model, rng, span_r=150.0, span_t=2.0, margin=0.1):
    """A state inside the limits, kept ``margin`` of each span off them."""
    state = []
    for slot, (_joint, channel) in enumerate(model.channels):
        lo, hi = model.limits(slot)
        span = span_r if channel[0] == "r" else span_t
        lo = -span if lo is None else lo
        hi = span if hi is None else hi
        pad = (hi - lo) * margin
        state.append(rng.uniform(lo + pad, hi - pad))
    return state


def _metres(rig, factor):
    """*rig* with every length times *factor*: rests, anchor points, slide limits."""
    out = json.loads(json.dumps(rig))
    for joint in out["joints"]:
        joint["t"] = [v * factor for v in joint["t"]]
        joint["anchor"]["t"] = [v * factor for v in joint["anchor"]["t"]]
        for channel in joint["channels"]:
            if channel["channel"][0] == "t":
                for side in ("min", "max"):
                    if channel[side] is not None:
                        channel[side] *= factor
    return out


def _f32(value):
    """*value* (a rig, or any JSON) with every float rounded to single precision,
    as a component's fields hold it -- so the reference solves from exactly the
    numbers Unity's model was built from."""
    if isinstance(value, dict):
        return {k: _f32(v) for k, v in value.items()}
    if isinstance(value, list):
        return [_f32(v) for v in value]
    if isinstance(value, float):
        return struct.unpack("f", struct.pack("f", value))[0]
    return value


class ControllerScenario:
    """The arm rig as a Unity hierarchy with its components, and every input
    the verifier drives it with -- plus the pythontk expectations that do not
    depend on what Unity reports. The reference model is built from the rig as
    the components hold it (single precision), mirrored back."""

    ROOT = dict(p=(1.2, -0.4, 2.5), q=Q.axis_angle((0.3, 1.0, -0.2), 37.0), s=1.25)
    SPACE = dict(p=(0.3, 0.1, -0.2), q=Q.axis_angle((1.0, 0.2, 0.5), -24.0))
    VIEW = (0.0, 0.6, -4.0)
    STEPS = 4

    def __init__(self, doc):
        rig = _metres(doc["rigs"]["arm"], ARM_METRES)
        # A second grab part, on a hinge link (elbow), so a hinge grab is
        # covered alongside the ball.
        rig["grab"] = [
            {"node": "head_geo", "joint": 4},
            {"node": "elbow_geo", "joint": 1},
        ]
        self.rig = _f32(rig)
        self.model = ptk.ArticulationModel(self.rig)
        rng = random.Random(11)
        self.s0 = _random_state(self.model, rng, margin=0.25)
        delta = [
            rng.uniform(-12.0, 12.0) if c[0] == "r" else rng.uniform(-0.3, 0.3)
            for _j, c in self.model.channels
        ]
        self.s1 = self.model.clamp([a + b for a, b in zip(self.s0, delta)])
        self.local = tuple(v * ARM_METRES for v in (0.4, -0.3, 0.25))  # on the head link
        self.elbow_local = tuple(v * ARM_METRES for v in (4.0, 0.6, -0.2))  # on the elbow link
        self.frames = [
            (self.ROOT["p"], self.ROOT["q"], self.ROOT["s"]),
            (self.SPACE["p"], self.SPACE["q"], 1.0),
        ]

    # ---- conversions of rig-space model values to Unity world ------------
    def world_point(self, p):
        return Unity.world_point(self.frames, Unity.point(p))

    def world_rotation(self, q):
        return Unity.world_rotation(self.frames, Unity.rotation(q))

    def unity_locals(self, state):
        return [
            [list(Unity.point(t)), list(Unity.rotation(q))]
            for t, q in self.model.pose(state)
        ]

    def path_states(self):
        n = self.STEPS
        return [
            [a + (b - a) * (i / n) for a, b in zip(self.s0, self.s1)]
            for i in range(1, n + 1)
        ]

    def inputs(self):
        m = self.model
        head_path = []
        for s in self.path_states():
            head_path.append(
                {
                    "target": list(self.world_point(m.point(s, 4, self.local))),
                    "hand": list(self.world_rotation(m.world(s)[4][1])),
                }
            )
        s_end = self.path_states()[-1]
        return {
            "rig": Unity.rig(self.rig),
            "root": {
                "p": list(self.ROOT["p"]),
                "q": list(self.ROOT["q"]),
                "s": self.ROOT["s"],
            },
            "space": {"p": list(self.SPACE["p"]), "q": list(self.SPACE["q"])},
            "s0_locals": self.unity_locals(self.s0),
            "head": {
                "grab": list(self.world_point(m.point(self.s0, 4, self.local))),
                "hand": list(self.world_rotation(m.world(self.s0)[4][1])),
                "path": head_path,
                "keep_target": list(self.world_point(m.point(s_end, 4, self.local))),
            },
            "elbow": {
                "grab": list(self.world_point(m.point(self.s0, 1, self.elbow_local))),
                "hand": list(self.world_rotation(m.world(self.s0)[1][1])),
                "target": list(self.world_point(m.point(s_end, 1, self.elbow_local))),
                "target_hand": list(self.world_rotation(m.world(s_end)[1][1])),
            },
            "view": list(self.VIEW),
        }


class FixtureScenario:
    """Two rigs of the conformance set (``arm`` and ``tree``: every rotate
    order, orients, slides along a rotated frame) posed at a random state and
    written as a Y-up, CENTIMETRE ASCII FBX, with their v2 record in METRES on
    ``data_export`` -- the file's lengths are the conformance numbers (cm), the
    record's a hundredth of them (m)."""

    def __init__(self, doc):
        self.rigs = {}
        for name in ("arm", "tree"):
            self.rigs[name] = _metres(doc["rigs"][name], 0.01)
        self.rigs["tree"]["grab"] = [{"node": "tip_geo", "joint": 3}]
        self.payload = {"version": 2, "rigs": [self.rigs["arm"], self.rigs["tree"]]}
        rng = random.Random(FIXTURE_SEED)
        self.models = {n: ptk.ArticulationModel(r) for n, r in self.rigs.items()}
        self.states = {
            n: _random_state(m, rng, span_t=0.02) for n, m in self.models.items()
        }
        self.group = {
            "arm": dict(t=(12.0, 3.0, -7.0), r=(10.0, 35.0, -5.0)),
            "tree": dict(t=(-20.0, 0.0, 4.0), r=(0.0, -60.0, 0.0)),
        }

    def fbx(self, with_carrier=True, payload=None) -> AsciiFbx:
        """The fixture; *payload* replaces the record (a version 1 re-export)."""
        fbx = AsciiFbx()
        by_name = {}
        for name, rig in self.rigs.items():
            model = self.models[name]
            state = self.states[name]
            group = fbx.add(
                f"{name}_RIG", t=self.group[name]["t"], r=self.group[name]["r"]
            )
            ids = {}
            for index, joint in enumerate(rig["joints"]):
                values = {
                    c: state[s] for s, (j, c) in enumerate(model.channels) if j == index
                }
                slide = (
                    values.get("tx", 0.0),
                    values.get("ty", 0.0),
                    values.get("tz", 0.0),
                )
                moved = Q.rot(Q.normalize(joint["q"]), slide)
                ids[index] = fbx.add(
                    joint["name"],
                    kind="LimbNode",
                    parent=group if joint["parent"] is None else ids[joint["parent"]],
                    # The file's centimetres: the record's metres times 100.
                    t=[100.0 * (joint["t"][i] + moved[i]) for i in range(3)],
                    pre=Q.euler_xyz(Q.normalize(joint["q"])),
                    r=(
                        values.get("rx", 0.0),
                        values.get("ry", 0.0),
                        values.get("rz", 0.0),
                    ),
                    order=joint["rotate_order"],
                )
                by_name[joint["name"]] = ids[index]
            for grab in rig["grab"]:
                fbx.add(
                    grab["node"],
                    parent=ids[grab["joint"]],
                    t=(1.0, 0.0, 0.0),
                    mesh=AsciiFbx.CUBE,
                )
        # A mesh that is NOT a grab part: it must not get a collider.
        fbx.add(
            "elbow_cover",
            parent=by_name["elbow"],
            t=(2.0, 0.0, 0.0),
            mesh=AsciiFbx.CUBE,
        )
        if with_carrier:
            fbx.add(
                "data_export",
                user={"articulation": json.dumps(payload or self.payload)},
            )
        else:
            fbx.add("data_export")
        return fbx

    def version1(self):
        """The record as version 1 wrote it: no anchors, lengths in the file's cm."""
        rigs = []
        for rig in self.payload["rigs"]:
            old = _metres(rig, 100.0)
            for joint in old["joints"]:
                del joint["anchor"]
            rigs.append(old)
        return {"version": 1, "rigs": rigs}

    def expected(self):
        """Per rig: the local pose written (metres), and the state the reference
        reads back off it with no hint (a ball's Euler triple is the one nearest
        rest, which need not be the triple that was written)."""
        out = {}
        for name, model in self.models.items():
            pose = model.pose(self.states[name])
            out[name] = {
                "state": model.read(pose, None),
                "pose": [[list(t), list(q)] for t, q in pose],
            }
        return out


class BindingScenario:
    """Hierarchies that put the importer's name binding to the test -- the rule
    every reader of the delivery follows: a joint binds to the one node of its
    name, exactly, else by namespace-stripped leaf, the rig's joints settled
    together; a grab part to the one of its name UNDER its joint; a tie, or
    root joints under different nodes, is left unbound and reported.

    A case is ``{"nodes": [[name, parent index or None], ...], "record"}``;
    ``None`` hangs a node off the model root.
    """

    class Tree:
        """A node list under construction."""

        def __init__(self):
            self.nodes = []

        def add(self, name, parent=None):
            self.nodes.append([name, parent])
            return len(self.nodes) - 1

        def rig(self, rig, group, ns=""):
            """*rig*'s joints under a group node, every name prefixed with
            *ns*; returns joint index -> node index."""
            ids = {}
            parent = self.add(ns + group)
            for index, joint in enumerate(rig["joints"]):
                up = parent if joint["parent"] is None else ids[joint["parent"]]
                ids[index] = self.add(ns + joint["name"], up)
            return ids

    def __init__(self, doc):
        arm, tree = doc["rigs"]["arm"], doc["rigs"]["tree"]
        self.cases = {}
        # Parts named alike under sibling joints: each grab entry takes the
        # one under ITS joint.
        t = self.Tree()
        ids = t.rig(tree, "tree_RIG")
        t.add("cap_geo", ids[1])
        t.add("cap_geo", ids[3])
        self._case(t, tree, "repeated_part", ("cap_geo", 1), ("cap_geo", 3))
        # A namespaced export of a rig the record names bare; one part
        # namespaced on both sides, one only in the record.
        t = self.Tree()
        ids = t.rig(arm, "arm_RIG", ns="lamp:")
        t.add("lamp:head_geo", ids[4])
        t.add("elbow_geo", ids[1])
        self._case(t, arm, "namespaced", ("lamp:head_geo", 4), ("rig:elbow_geo", 1))
        # A joint's exact name twice: the rig is refused, not bound to the first.
        t = self.Tree()
        t.rig(arm, "arm_RIG")
        t.add("elbow", t.add("props"))
        self._case(t, arm, "duplicate_joint")
        # Two namespaced instances of the rig: every bare name matches both.
        t = self.Tree()
        t.rig(arm, "arm_RIG", ns="A:")
        t.rig(arm, "arm_RIG", ns="B:")
        self._case(t, arm, "two_instances")
        # Two parts of one name under one joint: that part is not grabbable,
        # the rest of the rig binds.
        t = self.Tree()
        ids = t.rig(arm, "arm_RIG")
        t.add("head_geo", ids[4])
        t.add("head_geo", ids[4])
        t.add("elbow_geo", ids[1])
        self._case(t, arm, "ambiguous_part", ("head_geo", 4), ("elbow_geo", 1))
        # Namesakes ahead of the real joints -- one differing only by case,
        # one by namespace -- never steal an exact match.
        t = self.Tree()
        t.add("Elbow")
        t.add("NS:base")
        t.rig(arm, "arm_RIG")
        self._case(t, arm, "exact_wins")
        # Two root joints under different nodes: each root's chain poses in its
        # own root's space (articulation v2), so the rig imports, on the node
        # above them both.
        split = json.loads(json.dumps(arm))
        split["joints"][1]["parent"] = None
        t = self.Tree()
        spaces = (t.add("arm_RIG"), t.add("other_RIG"))
        ids = {}
        for index, joint in enumerate(split["joints"]):
            if joint["parent"] is None:
                up = spaces[min(index, 1)]
            else:
                up = ids[joint["parent"]]
            ids[index] = t.add(joint["name"], up)
        self._case(t, split, "split_roots")

    def _case(self, tree, rig, name, *grab):
        record = dict(rig, grab=[{"node": n, "joint": j} for n, j in grab])
        self.cases[name] = {
            "nodes": tree.nodes,
            "record": json.dumps({"version": 2, "rigs": [record]}),
        }


#: A real DCC export of an articulated rig (an FBX carrying the
#: ``articulation`` record, a clip optional). Machine-local and never checked
#: in: unset (or missing) = the live-export tests env-skip.
LIVE_FBX_ENV = "ARTICULATED_RIG_LIVE_FBX"


def _live_fbx():
    path = os.environ.get(LIVE_FBX_ENV, "")
    return path if path and os.path.isfile(path) else None


def _live_record(path):
    """The ``articulation`` record a real export carries, read with pythontk's
    FBX reader -- independently of Unity."""
    fbx = ptk.FbxFile.load(path, decode_arrays=True, raw_payloads=False)
    raw = fbx.user_properties("articulation")
    if not raw:
        raise ValueError(f"{path} carries no 'articulation' user property")
    text = raw[0].decode("utf-8") if isinstance(raw[0], (bytes, bytearray)) else raw[0]
    return json.loads(text)


def _invalid_rigs():
    """Rigs the reference refuses (ValueError); the port must refuse them too.
    Each is a rig as the record states it -- every key its shape requires, the
    one without an anchor aside -- so each is refused for its own fault."""
    base = {"name": "bad", "joints": [], "grab": []}

    def rig(*joints):
        out = dict(base)
        out["joints"] = list(joints)
        return out

    def channel(name, lo=None, hi=None):
        return {"channel": name, "min": lo, "max": hi, "weight": 1.0}

    ok = {
        "name": "a",
        "parent": None,
        "t": [0, 0, 0],
        "q": [0, 0, 0, 1],
        "anchor": {"t": [0, 0, 0], "q": [0, 0, 0, 1]},
        "rotate_order": "xyz",
        "channels": [],
    }
    bare = {k: v for k, v in ok.items() if k != "anchor"}
    turned = {"t": [0.1, 0.2, 0.0], "q": [0.0, 0.0, 0.38268343236, 0.92387953251]}
    return [
        rig(ok, dict(ok, name="b", parent=1)),  # parent not earlier
        rig(dict(ok, rotate_order="xxy")),  # unknown order
        rig(dict(ok, channels=[channel("sx")])),  # unknown channel
        rig(dict(ok, channels=[channel("rx"), channel("rx")])),  # twice
        rig(dict(ok, channels=[channel("rz", lo=10, hi=-10)])),  # reversed
        rig(bare),  # no anchor: a version 1 record's
        rig(ok, dict(ok, name="b", parent=0, channels=[channel("tx", hi=3)])),  # valid
        rig(dict(ok, anchor=turned, channels=[channel("rz", -30, 30)])),  # valid, anchored
    ]  # fmt: skip


# ======================================================================
# Unity-side verifier
# ======================================================================

UNITY_VERIFIER_CS = r"""
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using M3trik.Unitytk.ArticulatedRig;
using M3trik.Unitytk.Contracts;
using UnityEditor;
using UnityEngine;
using M = M3trik.Unitytk.ArticulatedRig.ArticulationModel;
using Rig = M3trik.Unitytk.ArticulatedRig.ArticulatedRig;

public class ArticulatedRigVerifier
{
    const string RESULTS = @"__RESULTS_PATH__";
    const string INPUTS = @"__INPUTS_PATH__";

    // ---- JSON in ----------------------------------------------------------
    static IDictionary<string, object> O(object o) => (IDictionary<string, object>)o;
    static IList<object> L(object o) => (IList<object>)o;
    static double D(object o) => (double)o;
    static M.Vec Vec(object o) { var l = L(o); return new M.Vec(D(l[0]), D(l[1]), D(l[2])); }
    static M.Quat Qt(object o) { var l = L(o); return new M.Quat(D(l[0]), D(l[1]), D(l[2]), D(l[3])); }
    static Vector3 V3(object o) { var l = L(o); return new Vector3((float)D(l[0]), (float)D(l[1]), (float)D(l[2])); }
    static Quaternion Q4(object o) { var l = L(o); return new Quaternion((float)D(l[0]), (float)D(l[1]), (float)D(l[2]), (float)D(l[3])); }
    static double[] Arr(object o) => L(o).Select(D).ToArray();
    static M.Frame[] Frames(object o) => L(o).Select(f => new M.Frame(Vec(L(f)[0]), Qt(L(f)[1]))).ToArray();

    // ---- JSON out ---------------------------------------------------------
    static string N(double v) => M3trik.Unitytk.Core.Json.Number(v);
    static string B(bool b) => b ? "true" : "false";
    static string S(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string A(double[] a) => a == null ? "null" : "[" + string.Join(",", a.Select(N)) + "]";
    static string V(M.Vec v) => "[" + N(v.x) + "," + N(v.y) + "," + N(v.z) + "]";
    static string Qs(M.Quat q) => "[" + N(q.x) + "," + N(q.y) + "," + N(q.z) + "," + N(q.w) + "]";
    static string U3(Vector3 v) => "[" + N(v.x) + "," + N(v.y) + "," + N(v.z) + "]";
    static string UQ(Quaternion q) => "[" + N(q.x) + "," + N(q.y) + "," + N(q.z) + "," + N(q.w) + "]";
    static string Fr(M.Frame[] fs) => "[" + string.Join(",", fs.Select(f => "[" + V(f.t) + "," + Qs(f.q) + "]")) + "]";

    static readonly List<string> _out = new List<string>();
    static void Put(string key, string json) => _out.Add(S(key) + ": " + json);

    public static void Verify()
    {
        IDictionary<string, object> inputs = null;
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            inputs = O(M3trik.Unitytk.Core.Json.Parse(File.ReadAllText(INPUTS), false));
        }
        catch (Exception e) { Put("inputs_error", S(e.ToString())); }
        if (inputs != null)
        {
            Run("guids", () => Guids(L(inputs["guids"])));
            Run("conformance", () => Conformance(O(inputs["doc"])));
            Run("invalid", () => Invalid(L(inputs["invalid"])));
            Run("chain", () => Chain(O(inputs["chain"])));
            Run("controller", () => Controller(O(inputs["controller"])));
            Run("binding", () => Binding(O(inputs["binding"])));
            Run("animator", () => AnimatorOverride(O(inputs["controller"])));
            Run("importer", () => Importer(O(inputs["importer"])));
            if (inputs.ContainsKey("live")) Run("live", () => Live(O(inputs["live"])));
        }
        Put("xr_assembly_loaded", B(AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "M3trik.Unitytk.ArticulatedRig.XRI")));
        Put("unity_version", S(Application.unityVersion));
        File.WriteAllText(RESULTS, "{\n" + string.Join(",\n", _out) + "\n}\n");
        Debug.Log("ARTICULATED RIG TEST COMPLETE. Results at: " + RESULTS);
        EditorApplication.Exit(0);
    }

    static void Run(string section, Action body)
    {
        try { body(); }
        catch (Exception e) { Put(section + "_error", S(e.ToString())); }
    }

    // ---- 0. fixed GUIDs ---------------------------------------------------
    static void Guids(IList<object> paths)
    {
        var rows = paths.Select(p => S((string)p) + ": " + S(AssetDatabase.AssetPathToGUID((string)p)));
        Put("guids", "{" + string.Join(", ", rows) + "}");
    }

    // ---- 1. conformance ---------------------------------------------------
    static void Conformance(IDictionary<string, object> doc)
    {
        var models = new Dictionary<string, M>();
        foreach (var kv in O(doc["rigs"])) models[kv.Key] = new M(O(kv.Value));
        var cases = new List<string>();
        foreach (var raw in L(doc["cases"]))
        {
            var c = O(raw);
            var model = models[(string)c["rig"]];
            var state = Arr(c["state"]);
            var read = O(c["read"]);
            var solves = new List<string>();
            foreach (var rs in L(c["solve"]))
            {
                var s = O(rs);
                M.Quat? rotation = s["rotation"] == null ? (M.Quat?)null : Qt(s["rotation"]);
                solves.Add(A(model.Solve(Arr(s["from"]), (int)D(s["joint"]), Vec(s["local"]), Vec(s["target"]), rotation)));
            }
            cases.Add("{\"pose\": " + Fr(model.Pose(state)) + ", \"world\": " + Fr(model.World(state))
                + ", \"read\": " + A(model.Read(Frames(read["locals"]), Arr(read["hint"])))
                + ", \"solve\": [" + string.Join(",", solves) + "]}");
        }
        Put("conformance", "[\n" + string.Join(",\n", cases) + "\n]");
    }

    static void Invalid(IList<object> rigs)
    {
        var thrown = new List<string>();
        foreach (var raw in rigs)
        {
            bool threw = false;
            try { new M(O(raw)); }
            catch (ArgumentException) { threw = true; }
            thrown.Add(B(threw));
        }
        Put("invalid", "[" + string.Join(",", thrown) + "]");
    }

    // ---- components from a rig in Unity's terms ---------------------------
    static Transform Frame(string name, Transform parent, object p, object q, float s)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = V3(p);
        t.localRotation = Q4(q);
        t.localScale = Vector3.one * s;
        return t;
    }

    /// <summary>The rig on <paramref name="space"/>, a joint on each of <paramref name="parts"/>,
    /// filled from <paramref name="spec"/> (a rig as the components hold it, Unity's axes).</summary>
    static Rig Fill(Transform space, IDictionary<string, object> spec, Transform[] parts)
    {
        var rig = space.gameObject.AddComponent<Rig>();
        var joints = L(spec["joints"]);
        for (int j = 0; j < joints.Count; j++)
        {
            var js = O(joints[j]);
            var joint = parts[j].gameObject.AddComponent<ArticulatedJoint>();
            joint.parent = js["parent"] == null ? null : rig.joints[(int)D(js["parent"])];
            joint.restPosition = V3(js["t"]);
            joint.restRotation = Q4(js["q"]);
            joint.anchorPosition = V3(O(js["anchor"])["t"]);
            joint.anchorRotation = Q4(O(js["anchor"])["q"]);
            joint.rotateOrder = (ArticulatedJoint.RotateOrder)Array.IndexOf(M.RotateOrders, (string)js["rotate_order"]);
            joint.channels = new List<ArticulatedJoint.Channel>();
            foreach (var raw in L(js["channels"]))
            {
                var c = O(raw);
                joint.channels.Add(new ArticulatedJoint.Channel
                {
                    axis = (ArticulatedJoint.Axis)Array.IndexOf(M.ChannelNames, (string)c["channel"]),
                    hasMin = c["min"] != null,
                    min = c["min"] == null ? 0f : (float)D(c["min"]),
                    hasMax = c["max"] != null,
                    max = c["max"] == null ? 0f : (float)D(c["max"]),
                    weight = (float)D(c["weight"]),
                });
            }
            rig.joints.Add(joint);
        }
        rig.Rebuild();
        return rig;
    }

    static Transform[] Parts(IDictionary<string, object> spec, Transform space, bool flat)
    {
        var joints = L(spec["joints"]);
        var parts = new Transform[joints.Count];
        for (int j = 0; j < parts.Length; j++)
        {
            var js = O(joints[j]);
            parts[j] = new GameObject((string)js["name"]).transform;
            parts[j].SetParent(flat || js["parent"] == null ? space : parts[(int)D(js["parent"])], false);
        }
        return parts;
    }

    static string Pose(Transform space, Transform t)
        => "{\"rig_p\": " + U3(space.InverseTransformPoint(t.position))
           + ", \"rig_q\": " + UQ(Quaternion.Inverse(space.rotation) * t.rotation)
           + ", \"world_p\": " + U3(t.position) + ", \"world_q\": " + UQ(t.rotation) + "}";

    // ---- 2. the chain in Unity's axes, nested and flat ---------------------
    static void Chain(IDictionary<string, object> h)
    {
        var outs = new List<string>();
        foreach (var rawCase in L(h["cases"]))
        {
            var c = O(rawCase);
            var spec = O(c["rig"]);
            var layouts = new List<string>();
            foreach (bool flat in new[] { false, true })
            {
                var root = Frame("H_root", null, O(h["root"])["p"], O(h["root"])["q"], (float)D(O(h["root"])["s"]));
                var space = Frame("H_space", root, O(h["space"])["p"], O(h["space"])["q"], 1f);
                var parts = Parts(spec, space, flat);
                var rig = Fill(space, spec, parts);
                rig.followAnimation = true;
                var state = Arr(c["state"]);
                rig.ApplyState(state);
                var rows = parts.Select(t => Pose(space, t)).ToList();
                rig.ReturnToAnimation();
                var back = rig.ReadState();
                layouts.Add("{\"flat\": " + B(flat) + ", \"valid\": " + B(rig.IsValid) + ", \"problem\": " + S(rig.Problem)
                    + ", \"parts\": [" + string.Join(",", rows) + "], \"read\": " + A(back) + "}");
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
            outs.Add("[" + string.Join(",", layouts) + "]");
        }
        Put("chain", "[" + string.Join(",\n", outs) + "]");
    }

    // ---- 3. the rig --------------------------------------------------------
    static Mesh _cube;
    static GameObject Part(string name, Transform parent, Vector3 local)
    {
        if (_cube == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(tmp);
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = Vector3.one * 0.3f;
        go.AddComponent<MeshFilter>().sharedMesh = _cube;
        go.AddComponent<MeshRenderer>();
        return go;
    }

    static void SetLocals(Transform[] joints, IList<object> locals)
    {
        for (int j = 0; j < joints.Length; j++)
        {
            joints[j].localPosition = V3(L(locals[j])[0]);
            joints[j].localRotation = Q4(L(locals[j])[1]);
        }
    }

    static string Grab(Rig rig)
    {
        return "{\"grabbing\": " + B(rig.IsGrabbing) + ", \"joint\": " + rig.IndexOf(rig.GrabbedJoint)
            + ", \"node\": " + S(rig.GrabbedPart != null ? rig.GrabbedPart.name : null)
            + ", \"local\": " + V(rig.GrabLocalPoint) + ", \"state\": " + A(rig.GetState()) + "}";
    }

    static string RigPoint(Transform space, Vector3 world) => U3(space.InverseTransformPoint(world));

    static string RigRotation(Transform space, Quaternion world) => UQ(Quaternion.Inverse(space.rotation) * world);

    static Vector3 U(M.Vec v) => new Vector3((float)v.x, (float)v.y, (float)v.z);

    /// <summary>The arm as a Unity hierarchy with its components: model root > rig space > joints.</summary>
    static Rig BuildArm(IDictionary<string, object> c, string rootName, out Transform root, out Transform space, out Transform[] joints)
    {
        var spec = O(c["rig"]);
        root = Frame(rootName, null, O(c["root"])["p"], O(c["root"])["q"], (float)D(O(c["root"])["s"]));
        space = Frame("arm_RIG", root, O(c["space"])["p"], O(c["space"])["q"], 1f);
        joints = Parts(spec, space, false);
        SetLocals(joints, L(c["s0_locals"]));
        var rig = Fill(space, spec, joints);
        rig.followAnimation = true;  // as the importer leaves a delivery's
        return rig;
    }

    static void Controller(IDictionary<string, object> c)
    {
        var rig = BuildArm(c, "ArmModel", out Transform root, out Transform space, out Transform[] joints);
        var head = Part("head_geo", joints[4], new Vector3(-0.1f, 0f, 0f));
        var elbow = Part("elbow_geo", joints[1], new Vector3(-0.8f, 0f, 0f));
        var elbowChild = Part("elbow_detail", elbow.transform, new Vector3(0.5f, 0.5f, 0f));
        elbowChild.AddComponent<BoxCollider>();  // has one: must be left alone
        rig.grabs.Add(new Rig.GrabPart { part = head.transform, joint = rig.joints[4] });
        rig.grabs.Add(new Rig.GrabPart { part = elbow.transform, joint = rig.joints[1] });
        var s0Locals = L(c["s0_locals"]);

        int colliders = rig.EnsureGrabColliders();
        var headCollider = head.GetComponent<MeshCollider>();
        Put("ctrl_valid", B(rig.IsValid));
        Put("ctrl_problem", S(rig.Problem));
        Put("ctrl_colliders_added", colliders.ToString());
        Put("ctrl_head_collider_convex", B(headCollider != null && headCollider.convex));
        Put("ctrl_detail_collider", S(elbowChild.GetComponent<Collider>().GetType().Name));
        var s0 = rig.ReadState();
        Put("ctrl_s0", A(s0));

        // A part outside every grab part is refused.
        Put("ctrl_refuses_joint", B(!rig.BeginGrab(joints[2], joints[2].position)));

        // ---- Hand: the ball link follows the hand along a path ----
        var h = O(c["head"]);
        rig.grabRotation = Rig.GrabRotation.Hand;
        bool began = rig.BeginGrab(head.transform, V3(h["grab"]), Q4(h["hand"]));
        var steps = new List<string>();
        steps.Add("{\"began\": " + B(began) + ", \"grab\": " + Grab(rig)
            + ", \"target_rig\": " + RigPoint(space, V3(h["grab"]))
            + ", \"hand_rig\": " + RigRotation(space, Q4(h["hand"])) + "}");
        foreach (var raw in L(h["path"]))
        {
            var p = O(raw);
            rig.UpdateGrab(V3(p["target"]), Q4(p["hand"]));
            var held = rig.GrabbedJoint.transform;
            var viaJoint = held.TransformPoint(U(rig.GrabLocalPoint));
            steps.Add("{\"state\": " + A(rig.GetState()) + ", \"target_rig\": " + RigPoint(space, V3(p["target"]))
                + ", \"hand_rig\": " + RigRotation(space, Q4(p["hand"]))
                + ", \"point_world\": " + U3(viaJoint) + ", \"grab_point_world\": " + U3(rig.GrabPointWorld)
                + ", \"link_world_q\": " + UQ(held.rotation) + "}");
        }
        Put("ctrl_hand", "[" + string.Join(",\n", steps) + "]");
        rig.EndGrab();
        Put("ctrl_after_end", "{\"grabbing\": " + B(rig.IsGrabbing) + ", \"holding\": " + B(rig.IsHoldingPose) + "}");
        rig.ReturnToAnimation();
        Put("ctrl_after_return", "{\"holding\": " + B(rig.IsHoldingPose) + "}");

        // ApplyState / ReadState round trip through the float transforms: the
        // first channel wound a whole turn (unclamped -- a script may), so the
        // read must unwrap toward the applied state, not to (-180, 180].
        var wound = (double[])s0.Clone();
        wound[0] += 360.0;
        rig.ApplyState(wound);
        bool heldAfterApply = rig.IsHoldingPose;
        rig.ReturnToAnimation();
        var back = rig.ReadState();
        Put("ctrl_apply_read", "{\"applied\": " + A(wound) + ", \"read\": " + A(back) + ", \"held\": " + B(heldAfterApply) + "}");

        // ---- Keep: a mouse (no hand) keeps the link's rotation as taken ----
        SetLocals(joints, s0Locals);
        var sKeep = rig.ReadState();
        rig.grabRotation = Rig.GrabRotation.Keep;
        rig.BeginGrab(head.transform, V3(h["grab"]));
        var keepGrab = Grab(rig);
        rig.UpdateGrab(V3(h["keep_target"]));
        Put("ctrl_keep", "{\"from\": " + A(sKeep) + ", \"grab\": " + keepGrab
            + ", \"target_rig\": " + RigPoint(space, V3(h["keep_target"])) + ", \"state\": " + A(rig.GetState()) + "}");
        rig.EndGrab();
        rig.ReturnToAnimation();

        // ---- Free: position only ----
        SetLocals(joints, s0Locals);
        var sFree = rig.ReadState();
        rig.grabRotation = Rig.GrabRotation.Free;
        rig.BeginGrab(head.transform, V3(h["grab"]), Q4(h["hand"]));
        var freeGrab = Grab(rig);
        rig.UpdateGrab(V3(h["keep_target"]), Q4(h["hand"]));
        Put("ctrl_free", "{\"from\": " + A(sFree) + ", \"grab\": " + freeGrab
            + ", \"target_rig\": " + RigPoint(space, V3(h["keep_target"])) + ", \"state\": " + A(rig.GetState()) + "}");
        rig.EndGrab();
        rig.ReturnToAnimation();

        // ---- Hinge: a hit on a DESCENDANT of the elbow part grabs it ----
        var e = O(c["elbow"]);
        SetLocals(joints, s0Locals);
        var sHinge = rig.ReadState();
        rig.grabRotation = Rig.GrabRotation.Hand;
        bool hingeBegan = rig.BeginGrab(elbowChild.transform, V3(e["grab"]), Q4(e["hand"]));
        var hingeGrab = Grab(rig);
        rig.UpdateGrab(V3(e["target"]), Q4(e["target_hand"]));
        Put("ctrl_hinge", "{\"began\": " + B(hingeBegan) + ", \"from\": " + A(sHinge) + ", \"grab\": " + hingeGrab
            + ", \"target_rig\": " + RigPoint(space, V3(e["target"]))
            + ", \"hand0_rig\": " + RigRotation(space, Q4(e["hand"]))
            + ", \"hand_rig\": " + RigRotation(space, Q4(e["target_hand"]))
            + ", \"state\": " + A(rig.GetState())
            + ", \"point_world\": " + U3(joints[1].TransformPoint(U(rig.GrabLocalPoint))) + "}");
        rig.EndGrab();
        rig.ReturnToAnimation();

        // ---- the pointer grabber: a real ray through real physics ----
        SetLocals(joints, s0Locals);
        rig.ReadState();
        rig.grabRotation = Rig.GrabRotation.Keep;
        var view = new GameObject("View").transform;
        view.position = V3(c["view"]);
        view.rotation = Quaternion.LookRotation(head.transform.position - view.position, Vector3.up);
        Physics.SyncTransforms();
        var pointer = new GameObject("Pointer").AddComponent<ArticulatedRigPointerGrab>();
        var miss = new Ray(view.position, -view.forward);
        bool missBegan = pointer.TryBegin(miss, view);
        var aim = new Ray(view.position, (head.transform.position - view.position).normalized);
        bool pointerBegan = pointer.TryBegin(aim, view);
        float depth0 = pointer.Depth;
        var node = rig.GrabbedPart;
        var drag = new Ray(view.position, Quaternion.AngleAxis(3f, view.up) * aim.direction);
        var planePoint = ArticulatedRigPointerGrab.PlanePoint(drag, view.position, view.forward, depth0);
        pointer.Drag(drag, view, 0f);
        var afterDrag = rig.GrabPointWorld;
        pointer.Drag(drag, view, 1f);
        float depth1 = pointer.Depth;
        var afterScroll = rig.GrabPointWorld;
        pointer.End();
        Put("pointer", "{\"miss_began\": " + B(missBegan) + ", \"began\": " + B(pointerBegan)
            + ", \"released\": " + B(pointer.Held == null && !rig.IsGrabbing)
            + ", \"node\": " + S(node != null ? node.name : null)
            + ", \"depth0\": " + N(depth0) + ", \"depth1\": " + N(depth1)
            + ", \"view_p\": " + U3(view.position) + ", \"view_f\": " + U3(view.forward)
            + ", \"drag_o\": " + U3(drag.origin) + ", \"drag_d\": " + U3(drag.direction)
            + ", \"plane_point\": " + U3(planePoint) + ", \"after_drag\": " + U3(afterDrag)
            + ", \"after_scroll\": " + U3(afterScroll) + ", \"step\": " + N(pointer.scrollStep) + "}");
        UnityEngine.Object.DestroyImmediate(pointer.gameObject);
        UnityEngine.Object.DestroyImmediate(view.gameObject);
        UnityEngine.Object.DestroyImmediate(root.gameObject);
    }

    // ---- 3a. binding by name, through the importer's own wiring -----------
    static string RelPath(Transform root, Transform t)
    {
        if (t == null) return null;
        var names = new List<string>();
        for (var p = t; p != null && p != root; p = p.parent) names.Add(p.name);
        names.Reverse();
        return string.Join("/", names);
    }

    static void Binding(IDictionary<string, object> cases)
    {
        const string Asset = "Assets/Binding.fbx";
        var wire = typeof(ArticulatedRigImporter).GetMethod("Wire", BindingFlags.Static | BindingFlags.NonPublic);
        var rows = new List<string>();
        foreach (var kv in cases)
        {
            var c = O(kv.Value);
            var root = new GameObject("BindingModel").transform;
            var made = new List<Transform>();
            foreach (var raw in L(c["nodes"]))
            {
                var n = L(raw);
                var t = new GameObject((string)n[0]).transform;
                t.SetParent(n[1] == null ? root : made[(int)D(n[1])], false);
                made.Add(t);
            }
            var warnings = new List<string>();
            Application.LogCallback hook = (message, stack, type) =>
            {
                if (type == LogType.Warning && message.Contains(Asset)) warnings.Add(message);
            };
            List<Rig> rigs;
            Application.logMessageReceived += hook;
            // Type.Missing: the frame left to its default, as a call that names none.
            try { rigs = (List<Rig>)wire.Invoke(null, new object[] { root.gameObject, (string)c["record"], Asset, new PureRecordSource(), Type.Missing }); }
            finally { Application.logMessageReceived -= hook; }
            string joints = "null", grabs = "{}";
            if (rigs.Count > 0)
            {
                var rig = rigs[0];
                joints = "[" + string.Join(",", rig.joints.Select(j => S(RelPath(root, j.transform)))) + "]";
                grabs = "{" + string.Join(",", rig.grabs.Select(g => S(RelPath(root, g.part)) + ": " + rig.IndexOf(g.joint))) + "}";
            }
            rows.Add(S(kv.Key) + ": {\"rigs\": " + rigs.Count + ", \"joints\": " + joints
                + ", \"grabs\": " + grabs + ", \"warnings\": [" + string.Join(",", warnings.Select(S)) + "]}");
            UnityEngine.Object.DestroyImmediate(root.gameObject);
        }
        Put("binding", "{" + string.Join(",\n", rows) + "}");
    }

    // ---- 3b. the Animator override (edit mode, no play mode) ---------------
    // A frame is driven by hand: Animator.Update(dt) -- which evaluates the
    // controller's clip and writes the transforms in edit mode -- then the
    // rig's own (private) LateUpdate, invoked by reflection, exactly the order
    // the player loop runs them in.
    static void AnimatorOverride(IDictionary<string, object> c)
    {
        var rig = BuildArm(c, "AnimatedArm", out Transform root, out Transform space, out Transform[] joints);
        var head = Part("head_geo", joints[4], new Vector3(-0.1f, 0f, 0f));
        rig.grabs.Add(new Rig.GrabPart { part = head.transform, joint = rig.joints[4] });
        rig.grabRotation = Rig.GrabRotation.Keep;
        var model = rig.Model;
        var s0 = rig.ReadState();

        // The clip swings the elbow (a hinge, joint 1) from -30 to +30
        // degrees about its s0 value over one second; nothing else is keyed.
        int elbowSlot = Enumerable.Range(0, model.ChannelCount).First(s => model.ChannelJoint(s) == 1);
        var a = (double[])s0.Clone(); a[elbowSlot] -= 30.0;
        var b = (double[])s0.Clone(); b[elbowSlot] += 30.0;
        Func<double[], Quaternion> elbowAt = s =>
        {
            var q = model.Local(s, 1).q;
            return new Quaternion((float)q.x, (float)q.y, (float)q.z, (float)q.w);
        };
        var qa = elbowAt(a);
        var qb = elbowAt(b);
        var clip = new AnimationClip { name = "ArmSwing" };
        string path = AnimationUtility.CalculateTransformPath(joints[1], root);
        string[] comps = { "x", "y", "z", "w" };
        for (int i = 0; i < 4; i++)
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation." + comps[i], AnimationCurve.Linear(0f, qa[i], 1f, qb[i]));
        clip.EnsureQuaternionContinuity();
        AssetDatabase.CreateAsset(clip, "Assets/ArmSwing.anim");
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip("Assets/ArmSwing.controller", clip);
        var animator = root.gameObject.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.Rebind();
        var late = typeof(Rig).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        var elbow = joints[1];
        var rows = new List<string>();
        Quaternion animated = Quaternion.identity;
        // One frame: the Animator, then the rig; returns the row.
        Func<string, float, string> frame = (label, dt) =>
        {
            animator.Update(dt);
            animated = elbow.localRotation;
            late.Invoke(rig, null);
            var held = rig.GetState();
            return "{\"label\": " + S(label) + ", \"time\": " + N(animator.GetCurrentAnimatorStateInfo(0).normalizedTime)
                + ", \"anim_vs_final\": " + N(Quaternion.Angle(animated, elbow.localRotation))
                + ", \"final_vs_held\": " + N(Quaternion.Angle(elbow.localRotation, elbowAt(held)))
                + ", \"elbow\": " + N(rig.joints[1].GetValue(ArticulatedJoint.Axis.Rz))
                + ", \"grabbing\": " + B(rig.IsGrabbing) + ", \"holding\": " + B(rig.IsHoldingPose) + "}";
        };

        Put("anim_late_found", B(late != null));
        Put("anim_initialized", B(animator.isInitialized));
        var before = elbow.localRotation;
        rows.Add(frame("start", 0f));
        Put("anim_writes_in_edit_mode", "{\"moved_by\": " + N(Quaternion.Angle(before, elbow.localRotation))
            + ", \"to_key0\": " + N(Quaternion.Angle(elbow.localRotation, qa))
            + ", \"followed\": " + N(Math.Abs(rig.GetState()[elbowSlot] - a[elbowSlot])) + "}");

        // Grab from the ANIMATED pose, drag, then frames with the clip running.
        bool began = rig.BeginGrab(head.transform, head.transform.position);
        var start = rig.GetState();
        Put("anim_grab_start", "{\"began\": " + B(began) + ", \"elbow\": " + N(start[elbowSlot])
            + ", \"clip_elbow\": " + N(a[elbowSlot]) + ", \"s0_elbow\": " + N(s0[elbowSlot]) + "}");
        rig.UpdateGrab(head.transform.position + space.TransformVector(new Vector3(0.6f, 0.9f, 0.3f)));
        rows.Add(frame("held_a", 0.2f));
        rows.Add(frame("held_b", 0.2f));
        // A script reading the state between the Animator and LateUpdate (an
        // Update of its own) must see the HELD pose -- the one on screen --
        // not the clip's, or the next LateUpdate writes the clip back.
        animator.Update(0f);
        var heldNow = rig.GetState();
        var readWhileHeld = rig.ReadState();
        late.Invoke(rig, null);
        Put("anim_read_while_held", "{\"diff\": " + N(readWhileHeld.Zip(heldNow, (x, y) => Math.Abs(x - y)).Max())
            + ", \"final_vs_held\": " + N(Quaternion.Angle(elbow.localRotation, elbowAt(heldNow))) + "}");
        rig.EndGrab();
        rows.Add(frame("released_a", 0.2f));
        rows.Add(frame("released_b", 0.1f));
        rig.ReturnToAnimation();
        rows.Add(frame("returned", 0.1f));

        // Auto-resume: a release holds while the Animator stands still and
        // hands back as soon as its time moves.
        rig.resumeOnAnimatorChange = true;
        rig.BeginGrab(head.transform, head.transform.position);
        rig.UpdateGrab(head.transform.position + space.TransformVector(new Vector3(-0.5f, 0.4f, 0.2f)));
        rig.EndGrab();
        rows.Add(frame("auto_still", 0f));
        rows.Add(frame("auto_moved", 0.1f));
        Put("anim_frames", "[" + string.Join(",\n", rows) + "]");
        UnityEngine.Object.DestroyImmediate(root.gameObject);
    }

    // ---- 4. importer ------------------------------------------------------
    static string Rigs(GameObject model)
    {
        var rows = new List<string>();
        foreach (var rig in model.GetComponentsInChildren<Rig>(true))
        {
            var locals = rig.joints.Select(j => "[" + U3(j.transform.localPosition) + "," + UQ(j.transform.localRotation) + "]");
            rows.Add("{\"space\": " + S(rig.name) + ", \"valid\": " + B(rig.IsValid) + ", \"follows\": " + B(rig.followAnimation)
                + ", \"joints\": [" + string.Join(",", rig.joints.Select(j => S(j.name))) + "]"
                + ", \"parents\": [" + string.Join(",", rig.joints.Select(j => rig.IndexOf(j.parent).ToString())) + "]"
                + ", \"grabs\": {" + string.Join(",", rig.grabs.Select(g => S(g.part.name) + ": " + rig.IndexOf(g.joint))) + "}"
                + ", \"state\": " + A(rig.ReadState()) + ", \"unity_locals\": [" + string.Join(",", locals) + "]}");
        }
        return "[" + string.Join(",\n", rows) + "]";
    }

    static void Importer(IDictionary<string, object> imp)
    {
        string asset = (string)imp["asset"];
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
        Put("imp_loaded", B(prefab != null));
        if (prefab == null) return;
        var headGeo = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head_geo");
        var headCollider = headGeo != null ? headGeo.GetComponent<MeshCollider>() : null;
        Put("imp_head_collider", "{\"found\": " + B(headGeo != null) + ", \"collider\": " + B(headCollider != null)
            + ", \"convex\": " + B(headCollider != null && headCollider.convex)
            + ", \"mesh\": " + B(headCollider != null && headCollider.sharedMesh != null) + "}");
        var cover = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "elbow_cover");
        Put("imp_cover_collider", B(cover != null && cover.GetComponent<Collider>() != null));
        Put("imp_cover_found", B(cover != null && cover.GetComponent<MeshFilter>() != null));
        var inst = (GameObject)UnityEngine.Object.Instantiate(prefab);
        Put("imp_rigs", Rigs(inst));
        UnityEngine.Object.DestroyImmediate(inst);

        // Re-exports: a version 1 record is refused (no rig), the original
        // brings it back, and one without the carrier drops it.
        Put("imp_after_v1", Reimport(asset, (string)imp["v1"]));
        Put("imp_after_restore", Reimport(asset, (string)imp["original"]));
        Put("imp_after_nocarrier", Reimport(asset, (string)imp["nocarrier"]));
    }

    static string Reimport(string asset, string source)
    {
        File.Copy(source, asset, true);
        var warnings = new List<string>();
        Application.LogCallback hook = (message, stack, type) =>
        {
            if (type == LogType.Warning && message.Contains("articulation")) warnings.Add(message);
        };
        Application.logMessageReceived += hook;
        try { AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport); }
        finally { Application.logMessageReceived -= hook; }
        var again = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
        return "{\"loaded\": " + B(again != null) + ", \"rigs\": " + (again == null ? 0 : again.GetComponentsInChildren<Rig>(true).Length)
            + ", \"joints\": " + (again == null ? 0 : again.GetComponentsInChildren<ArticulatedJoint>(true).Length)
            + ", \"warnings\": [" + string.Join(",", warnings.Select(S)) + "]}";
    }

    // ---- 5. a real DCC export (env-gated) ----------------------------------
    static string Locals(Rig rig)
        => "[" + string.Join(",", rig.joints.Select(j => "{\"unity_t\": " + U3(j.transform.localPosition)
               + ", \"unity_q\": " + UQ(j.transform.localRotation) + "}")) + "]";

    static void Live(IDictionary<string, object> live)
    {
        string asset = (string)live["asset"];
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
        Put("live_loaded", B(prefab != null));
        if (prefab == null) return;
        var inst = (GameObject)UnityEngine.Object.Instantiate(prefab);
        var all = inst.GetComponentsInChildren<Rig>(true);
        Put("live_rigs", Rigs(inst));
        if (all.Length == 0) { UnityEngine.Object.DestroyImmediate(inst); return; }
        var rig = all[0];
        var space = rig.transform;
        var grabRows = rig.grabs.Select(g => "{\"name\": " + S(g.part.name)
            + ", \"colliders\": " + g.part.GetComponentsInChildren<Collider>(true).Length
            + ", \"convex\": " + B(g.part.GetComponentsInChildren<MeshCollider>(true).Any(m => m.convex && m.sharedMesh != null)) + "}");
        Put("live_rig", "{\"space\": " + S(space.name) + ", \"space_scale\": " + N(space.lossyScale.x)
            + ", \"animator\": " + B(inst.GetComponent<Animator>() != null)
            + ", \"grab\": [" + string.Join(",", grabRows) + "]}");

        // The imported DEFAULT pose (the prefab as imported, no clip sampled).
        Put("live_default", "{\"locals\": " + Locals(rig) + ", \"state\": " + A(rig.ReadState()) + "}");

        // Each clip, sampled into the transforms at its start and middle.
        var clips = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>()
            .Where(cl => !cl.name.StartsWith("__preview__")).ToArray();
        var sampled = new List<string>();
        foreach (var clip in clips)
        {
            float mid = Mathf.Round(0.5f * clip.length * clip.frameRate) / clip.frameRate;
            foreach (float time in new[] { 0f, mid })
            {
                clip.SampleAnimation(inst, time);
                sampled.Add("{\"clip\": " + S(clip.name) + ", \"time\": " + N(time) + ", \"locals\": " + Locals(rig)
                    + ", \"state\": " + A(rig.ReadState()) + "}");
            }
        }
        Put("live_clips", "[" + string.Join(",\n", sampled) + "]");

        // A Hand grab on a grab part -- a ball-jointed one where the rig has
        // one -- from the default pose (put back after the sampling), walked
        // to where a nearby state puts the held point: reachable by construction.
        var src = prefab.GetComponentsInChildren<Transform>(true);
        var dst = inst.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < src.Length && i < dst.Length; i++)
        {
            dst[i].localPosition = src[i].localPosition;
            dst[i].localRotation = src[i].localRotation;
        }
        rig.ReadState();
        var model = rig.Model;
        Rig.GrabPart pick = rig.grabs.FirstOrDefault(g => model.Joints[rig.IndexOf(g.joint)].IsBall) ?? rig.grabs.FirstOrDefault();
        Put("live_part", S(pick != null ? pick.part.name : null));
        if (pick == null) { UnityEngine.Object.DestroyImmediate(inst); return; }
        int joint = rig.IndexOf(pick.joint);
        var renderer = pick.part.GetComponentInChildren<Renderer>(true);
        var point = renderer != null ? renderer.bounds.center : pick.part.position;
        var link = pick.joint.transform;
        rig.grabRotation = Rig.GrabRotation.Hand;
        var hand0 = link.rotation;
        bool began = rig.BeginGrab(pick.part, point, hand0);
        var s0 = rig.GetState();
        var local = rig.GrabLocalPoint;
        var s1 = (double[])s0.Clone();
        foreach (int j in model.Chain(joint))
            for (int s = 0; s < model.ChannelCount; s++)
                if (model.ChannelJoint(s) == j)
                    s1[s] += model.ChannelName(s)[0] == 'r' ? 10.0 : 0.005;
        s1 = model.Clamp(s1);
        var steps = new List<string>();
        const int STEPS = 4;
        for (int i = 1; i <= STEPS; i++)
        {
            var si = s0.Select((v, s) => v + (s1[s] - v) * i / (double)STEPS).ToArray();
            var target = space.TransformPoint(U(model.Point(si, joint, local)));
            var wq = model.World(si)[joint].q;
            var hand = space.rotation * new Quaternion((float)wq.x, (float)wq.y, (float)wq.z, (float)wq.w);
            rig.UpdateGrab(target, hand);
            steps.Add("{\"target_world\": " + U3(target) + ", \"target_rig\": " + RigPoint(space, target)
                + ", \"hand_rig\": " + RigRotation(space, hand) + ", \"state\": " + A(rig.GetState())
                + ", \"point_world\": " + U3(link.TransformPoint(U(local)))
                + ", \"link_world_q\": " + UQ(link.rotation) + ", \"hand_world\": " + UQ(hand) + "}");
        }
        Put("live_grab", "{\"began\": " + B(began) + ", \"joint\": " + joint + ", \"local\": " + V(local)
            + ", \"from\": " + A(s0) + ", \"hand0_rig\": " + RigRotation(space, hand0)
            + ", \"steps\": [" + string.Join(",\n", steps) + "]}");
        rig.EndGrab();
        UnityEngine.Object.DestroyImmediate(inst);
    }
}
"""


def _verifier_source(results_path: str, inputs_path: str) -> str:
    return UNITY_VERIFIER_CS.replace("__RESULTS_PATH__", results_path).replace(
        "__INPUTS_PATH__", inputs_path
    )


def _compile_errors(log_path: str) -> str:
    try:
        with open(log_path, encoding="utf-8", errors="ignore") as fh:
            text = fh.read()
    except OSError:
        return ""
    errors = sorted(set(re.findall(r"^.*error CS\d+.*$", text, flags=re.M)))
    return "\n".join(errors[:20])


#: The embedded package's root as an AssetDatabase path.
_PACKAGE_ASSET_ROOT = f"Packages/{ProjectBuilder.PACKAGE}"


def _bundle_assets():
    """Every installed package file and folder, as "/"-joined package paths."""
    names = PackageSources.files()
    return names + PackageSources.folders(names)


def _meta_bytes(name):
    """Package asset *name*'s .meta in the checkout, as it is on disk."""
    with open(PackageSources.path(name + ".meta"), "rb") as fh:
        return fh.read()


def _committed_meta(name):
    """Package asset *name*'s committed .meta, LF."""
    return _meta_bytes(name).replace(b"\r\n", b"\n")


def _run_unity(test, temp_dir, execute_method, prepare, timeout=600):
    """Create a project, let *prepare(project_path)* stage it, run the
    verifier; returns the parsed results (or skips on the license wall)."""
    project_path = TempProjects.project(temp_dir)
    UnityEnv.create_project(test, project_path)
    results_path = os.path.join(temp_dir, "unity_results.json")
    prepare(project_path, results_path)
    log_path = os.path.join(temp_dir, "unity_log.txt")
    returncode = UnityEnv.run(test, project_path, execute_method, log_path, timeout=timeout)
    if not os.path.exists(results_path):
        log_text = ""
        if os.path.exists(log_path):
            with open(log_path, encoding="utf-8", errors="ignore") as fh:
                log_text = fh.read()
        if UnityEnv.is_licence_failure(returncode, log_text):
            logger.info(
                "Unity batch license failure (rc=%s) — see %s", returncode, log_path
            )
            test.skipTest(UnityEnv.license_reason())
        return None, returncode, log_path, log_text
    with open(results_path, encoding="utf-8") as fh:
        return json.load(fh), returncode, log_path, ""


# ======================================================================
# Test class: the default project (no XR Interaction Toolkit)
# ======================================================================


class TestArticulatedRigRuntime(unittest.TestCase):
    """The articulated rig's model, components and importer against pythontk,
    read back from a live Unity."""

    @classmethod
    def setUpClass(cls):
        cls.temp_dir = TempProjects.allocate("articulated_runtime", cls)
        cls.unity = None
        cls.doc = ptk.ArticulationConformance.cases(seed=SEED, per_rig=PER_RIG)
        cls.scenario = ControllerScenario(cls.doc)
        cls.fixture = FixtureScenario(cls.doc)
        cls.binding = BindingScenario(cls.doc)
        # One case per rig through a real hierarchy, in Unity's axes.
        seen, chain = set(), []
        for i, case in enumerate(cls.doc["cases"]):
            if case["rig"] in seen:
                continue
            seen.add(case["rig"])
            rig = _f32(cls.doc["rigs"][case["rig"]])
            model = ptk.ArticulationModel(rig)
            chain.append(
                {
                    "index": i,
                    "rig": Unity.rig(rig),
                    "state": Unity.state(model, case["state"]),
                }
            )
        cls.chain = {
            "cases": chain,
            "root": {
                "p": [2.0, -1.0, 0.5],
                "q": list(Q.axis_angle((0.2, 1.0, 0.4), 71.0)),
                "s": 1.7,
            },
            "space": {
                "p": [0.25, 0.5, -0.75],
                "q": list(Q.axis_angle((1.0, -0.3, 0.1), 33.0)),
            },
        }
        logger.info(f"Temp dir: {cls.temp_dir}")

    def _results(self):
        unity = self.__class__.unity
        if not unity:
            self.skipTest("Unity results not available — test_01 must run first")
        return unity

    #: Result-key prefix -> the verifier section that writes it.
    SECTIONS = {
        "ctrl": "controller",
        "pointer": "controller",
        "anim": "animator",
        "imp": "importer",
    }

    def _section(self, key):
        """``unity[key]``, failing with the section's exception if it threw."""
        unity = self._results()
        section = self.SECTIONS.get(key.split("_")[0], key)
        for name in ("inputs", section):
            error = unity.get(f"{name}_error")
            if error:
                self.fail(f"the verifier's {name} section threw:\n{error}")
        self.assertIn(key, unity, f"no '{key}' in the Unity results")
        return unity[key]

    # ---- Unity's values in the model's terms -------------------------------
    def _m(self, state, model=None):
        """A Unity state (or a list of them) back in the model's terms."""
        return Unity.state(model or self.scenario.model, state)

    def _read(self, state):
        """The state the reference reads back off *state*'s pose, no hint."""
        m = self.scenario.model
        return m.read(m.pose(state), None)

    # ---- Step 1: the Unity run ---------------------------------------------

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_01_unity_runtime(self):
        """Install the package, stage the inputs and the FBX, run the verifier."""
        cls = self.__class__
        fixture_name = "ArticulationFixture.fbx"

        def prepare(project_path, results_path):
            UnityHost.install(project_path, "local")
            assets = os.path.join(project_path, "Assets")
            cls.fixture.fbx().write(os.path.join(assets, fixture_name))
            # Re-export variants, kept outside Assets/ (not .fbx) until copied in.
            variants = {}
            for key, kwargs in (
                ("original", {}),
                ("v1", {"payload": cls.fixture.version1()}),
                ("nocarrier", {"with_carrier": False}),
            ):
                variants[key] = os.path.join(cls.temp_dir, f"fixture_{key}.fbx.txt")
                cls.fixture.fbx(**kwargs).write(variants[key])
            inputs = {
                "guids": [f"{_PACKAGE_ASSET_ROOT}/{n}" for n in _bundle_assets()],
                "doc": cls.doc,
                "invalid": _invalid_rigs(),
                "chain": cls.chain,
                "controller": cls.scenario.inputs(),
                "binding": cls.binding.cases,
                "importer": dict(variants, asset=f"Assets/{fixture_name}"),
            }
            live = _live_fbx()
            if live:
                shutil.copyfile(live, os.path.join(assets, "LiveExport.fbx"))
                inputs["live"] = {"asset": "Assets/LiveExport.fbx"}
            inputs_path = os.path.join(cls.temp_dir, "inputs.json")
            with open(inputs_path, "w", encoding="utf-8") as fh:
                json.dump(inputs, fh)
            editor_dir = os.path.join(assets, "Editor")
            os.makedirs(editor_dir, exist_ok=True)
            with open(
                os.path.join(editor_dir, "ArticulatedRigVerifier.cs"),
                "w",
                encoding="utf-8",
            ) as fh:
                fh.write(_verifier_source(results_path, inputs_path))

        # The project links this checkout's package (``UnityHost.install``), so
        # what Unity imports is these very files: their bytes now are what the
        # import must leave untouched.
        cls.metas_before = {name: _meta_bytes(name) for name in _bundle_assets()}
        unity, rc, log_path, _log = _run_unity(
            self, cls.temp_dir, "ArticulatedRigVerifier.Verify", prepare
        )
        if unity is None:
            self.fail(
                f"unity_results.json not created (Unity rc={rc}) — check {log_path}\n"
                + _compile_errors(log_path)
            )
        cls.unity = unity
        logger.info("Unity %s", unity.get("unity_version"))

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_01b_the_editor_reads_the_committed_guids(self):
        """Fixed GUIDs, proven by the editor rather than by reading files:
        AssetDatabase names every package script, shader, asmdef, the README
        and every component folder (the doors' assemblies included) by the
        GUID committed in the bundle's .meta -- and the import left those
        .meta files byte-identical (the linked checkout's, before and after),
        so a project never churns them."""
        got = self._section("guids")
        expected = {
            f"{_PACKAGE_ASSET_ROOT}/{name}": re.search(
                rb"^guid: ([0-9a-f]{32})$", _committed_meta(name), re.M
            )
            .group(1)
            .decode()
            for name in _bundle_assets()
        }
        self.assertEqual(got, expected, "Unity named a package asset differently")
        rewritten = [n for n, before in self.metas_before.items() if _meta_bytes(n) != before]
        self.assertEqual(rewritten, [], "Unity rewrote these .meta files on import")

    # ---- Step 2: conformance -------------------------------------------------

    def _conformance(self):
        got = self._section("conformance")
        cases = self.doc["cases"]
        self.assertEqual(len(got), len(cases), "one result per golden case")
        return zip(cases, got)

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_02_conformance_pose_and_world(self):
        """Forward kinematics: every joint's local and rig-space pose, the
        anchored rigs' included."""
        tol = self.doc["tolerance"]
        rigs = set()
        for index, (case, got) in enumerate(self._conformance()):
            rigs.add(case["rig"])
            for key in ("pose", "world"):
                for joint, ((et, eq), (gt, gq)) in enumerate(zip(case[key], got[key])):
                    where = f"case {index} ({case['rig']}) {key} joint {joint}"
                    self.assertLessEqual(
                        _max_diff(et, gt), tol[key], f"{where}: t {gt} != {et}"
                    )
                    self.assertLessEqual(
                        _close_quat(eq, gq), tol[key], f"{where}: q {gq} != {eq}"
                    )
        self.assertTrue({"anchored", "flat", "anchored_tree"} <= rigs, rigs)

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_03_conformance_read(self):
        """A state back from local transforms, unwrapped toward a wound hint."""
        tol = self.doc["tolerance"]["read"]
        for index, (case, got) in enumerate(self._conformance()):
            self.assertLessEqual(
                _max_diff(case["read"]["expect"], got["read"]),
                tol,
                f"case {index} ({case['rig']}): read {got['read']} != {case['read']['expect']}",
            )

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_04_conformance_solve(self):
        """Every grab solve -- with and without a hand rotation -- to the reference."""
        tol = self.doc["tolerance"]["solve"]
        count = 0
        for index, (case, got) in enumerate(self._conformance()):
            self.assertEqual(len(got["solve"]), len(case["solve"]))
            for n, (solve, state) in enumerate(zip(case["solve"], got["solve"])):
                count += 1
                self.assertLessEqual(
                    _max_diff(solve["expect"], state),
                    tol,
                    f"case {index} ({case['rig']}) solve {n} (joint {solve['joint']}, "
                    f"rotation {'yes' if solve['rotation'] else 'no'}): "
                    f"{state} != {solve['expect']}",
                )
        self.assertGreater(count, 0)

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_06_record_validation_matches_reference(self):
        """The port refuses exactly the rigs the reference refuses -- a joint
        with no anchor (a version 1 record's) among them."""
        got = self._section("invalid")
        expected = []
        for rig in _invalid_rigs():
            try:
                ptk.ArticulationModel(rig)
                expected.append(False)
            except ValueError:
                expected.append(True)
        self.assertEqual(got, expected)
        self.assertIn(False, expected, "a valid rig is among them")

    # ---- Step 3: the chain in Unity's axes -----------------------------------

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_07_the_chain_poses_every_part_nested_or_flat(self):
        """One case per rig, as components in Unity's axes on a real hierarchy
        under a rotated, scaled root: ApplyState puts every part at the mirror
        of the golden rig-space world -- nested on the chain, and FLAT (every
        part a child of the rig's root, chained by its parent joints alone) --
        in world space where an independent conversion says, and the state
        reads back off the transforms."""
        got = self._section("chain")
        h = self.chain
        frames = [
            (h["root"]["p"], h["root"]["q"], h["root"]["s"]),
            (h["space"]["p"], h["space"]["q"], 1.0),
        ]
        self.assertEqual(len(got), len(h["cases"]))
        for spec, layouts in zip(h["cases"], got):
            case = self.doc["cases"][spec["index"]]
            model = ptk.ArticulationModel(_f32(self.doc["rigs"][case["rig"]]))
            world_m = model.world(case["state"])
            self.assertEqual([row["flat"] for row in layouts], [False, True])
            for layout in layouts:
                kind = "flat" if layout["flat"] else "nested"
                self.assertTrue(layout["valid"], f"{case['rig']} {kind}: {layout['problem']}")
                for joint, ((p, q), row) in enumerate(zip(world_m, layout["parts"])):
                    where = f"case {spec['index']} ({case['rig']}, {kind}) joint {joint}"
                    scale = max(1.0, max(map(abs, p)))
                    self.assertLessEqual(
                        _max_diff(row["rig_p"], Unity.point(p)), 2e-4 * scale, f"{where}: rig p"
                    )
                    self.assertLessEqual(
                        _close_quat(row["rig_q"], Unity.rotation(q)), 2e-5, f"{where}: rig q"
                    )
                    world = Unity.world_point(frames, Unity.point(p))
                    self.assertLessEqual(
                        _max_diff(row["world_p"], world), 4e-4 * scale, f"{where}: world p"
                    )
                    wq = Unity.world_rotation(frames, Unity.rotation(q))
                    self.assertLessEqual(
                        _close_quat(row["world_q"], wq), 2e-5, f"{where}: world q"
                    )
                self.assertLessEqual(
                    _max_diff(layout["read"], spec["state"]), 2e-3, f"{case['rig']} {kind}: read back"
                )

    # ---- Step 4: the rig -----------------------------------------------------

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_09_the_rig_poses_and_reads_its_parts(self):
        """The components make a rig that poses, grab parts get convex
        colliders (a mesh with its own collider keeps it), and the state reads
        back off the float transforms -- in Unity's terms, the reference's
        with ry, rz and tx negated."""
        unity = self._results()
        self.assertTrue(self._section("ctrl_valid"), unity.get("ctrl_problem"))
        self.assertEqual(
            unity["ctrl_colliders_added"], 2, "one per grab part mesh without one"
        )
        self.assertTrue(unity["ctrl_head_collider_convex"])
        self.assertEqual(
            unity["ctrl_detail_collider"], "BoxCollider", "an existing collider is kept"
        )
        self.assertLessEqual(
            _max_diff(self._m(unity["ctrl_s0"]), self._read(self.scenario.s0)), 2e-3
        )
        self.assertTrue(
            unity["ctrl_refuses_joint"],
            "a transform outside every grab part is refused",
        )

    # ---- Step 4a: binding by name ----------------------------------------------

    def _binding(self, case):
        got = self._section("binding")
        self.assertIn(case, got, "no such binding case in the Unity results")
        return got[case]

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_09c_a_repeated_part_name_binds_under_its_own_joint(self):
        """Two parts share a name under sibling joints: each grab entry takes
        the one under its own joint, and both are grabbable."""
        got = self._binding("repeated_part")
        self.assertEqual(got["rigs"], 1)
        self.assertEqual(
            got["grabs"],
            {"tree_RIG/hub/left/cap_geo": 1, "tree_RIG/hub/right/tip/cap_geo": 3},
        )
        self.assertEqual(got["warnings"], [])

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_09d_namespaced_names_bind_by_their_leaf(self):
        """A namespaced export binds a record that names its joints bare, and
        a grab part namespaced on either side binds by its leaf."""
        got = self._binding("namespaced")
        self.assertEqual(got["rigs"], 1)
        chain = "lamp:arm_RIG/lamp:base/lamp:elbow"
        self.assertEqual(got["joints"][1], chain)
        self.assertEqual(
            got["grabs"],
            {
                f"{chain}/lamp:collar/lamp:extend/lamp:head/lamp:head_geo": 4,
                f"{chain}/elbow_geo": 1,
            },
        )
        self.assertEqual(got["warnings"], [])

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_09e_a_tie_stays_unbound_and_is_reported(self):
        """A joint name that matches several transforms -- exactly, or by leaf
        across two namespaced instances -- leaves the rig out; two parts of one
        name under one joint leave that part ungrabbable while the rig binds.
        Each is reported naming what tied, never guessed."""
        for case, named, rigs in (
            ("duplicate_joint", "joint 'elbow'", 0),
            ("two_instances", "joint 'base'", 0),
            ("ambiguous_part", "grab part 'head_geo'", 1),
        ):
            got = self._binding(case)
            self.assertEqual(got["rigs"], rigs, case)
            self.assertTrue(
                any(named in w and "matches several" in w for w in got["warnings"]),
                f"{case}: no report naming {named}: {got['warnings']}",
            )
        self.assertIsNone(self._binding("duplicate_joint")["joints"])
        self.assertEqual(
            self._binding("ambiguous_part")["grabs"],
            {"arm_RIG/base/elbow/elbow_geo": 1},
        )

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_09g_a_rig_whose_roots_hang_apart_imports(self):
        """Root joints under different nodes each pose in their own root's
        space (the record's assembly): the rig is wired, every joint bound,
        and nothing is reported."""
        got = self._binding("split_roots")
        self.assertEqual(got["rigs"], 1)
        self.assertEqual(got["warnings"], [])

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_09f_an_exact_name_wins_over_its_namesakes(self):
        """Transforms ahead of the joints that differ only by case
        (``Elbow``) or namespace (``NS:base``) never take an exact match."""
        got = self._binding("exact_wins")
        chain = ["arm_RIG/base"]
        for name in ("elbow", "collar", "extend", "head"):
            chain.append(f"{chain[-1]}/{name}")
        self.assertEqual(got["joints"], chain)
        self.assertEqual(got["warnings"], [])

    def _solve(self, state, joint, local, target, rotation):
        return self.scenario.model.solve(state, joint, local, target, rotation)

    @staticmethod
    def _p(v):
        """A Unity rig-space point (or a grab's local point) in the model's terms."""
        return Unity.point(v)

    @staticmethod
    def _q(q):
        """A Unity rig-space rotation in the model's terms."""
        return Unity.rotation(q)

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_10_hand_grab_follows_the_reference(self):
        """A ball-link grab along a path: every solved state equals
        pythontk's solve chained from the same inputs (mirrored back), the
        hand's rotation composes through the grab offset, and the held point
        lands on each world target through the real transforms."""
        steps = self._section("ctrl_hand")
        sc = self.scenario
        m = sc.model
        first = steps[0]
        self.assertTrue(first["began"])
        self.assertEqual(first["grab"]["joint"], 4)
        self.assertEqual(first["grab"]["node"], "head_geo")
        # What Unity computed from the world inputs matches the scenario.
        local = self._p(first["grab"]["local"])
        self.assertLessEqual(_max_diff(local, sc.local), 1e-4)
        state = self._m(first["grab"]["state"])
        self.assertLessEqual(_max_diff(state, self._read(sc.s0)), 2e-3)
        link0 = m.world(state)[4][1]
        hand0 = self._q(first["hand_rig"])
        self.assertLessEqual(
            _close_quat(hand0, link0), 1e-5, "the scenario's hand starts on the link"
        )
        offset = Q.mul(Q.conj(hand0), link0)
        for n, (step, path_state, wanted) in enumerate(
            zip(steps[1:], sc.path_states(), sc.inputs()["head"]["path"])
        ):
            target = self._p(step["target_rig"])
            self.assertLessEqual(_max_diff(target, m.point(path_state, 4, sc.local)), 1e-4)
            rotation = Q.mul(self._q(step["hand_rig"]), offset)
            self.assertLessEqual(_close_quat(rotation, m.world(path_state)[4][1]), 1e-5)
            state = self._solve(state, 4, local, target, rotation)
            got = self._m(step["state"])
            self.assertLessEqual(_max_diff(got, state), 1e-5, f"step {n}: {got} != {state}")
            self._assert_lands(
                step["point_world"], wanted["target"], state, 4, local, target, f"step {n}"
            )
            self.assertLessEqual(
                _max_diff(step["grab_point_world"], step["point_world"]), 2e-5
            )
            # The link follows the hand's rotation (the wrist split).
            angle = Q.angle(step["link_world_q"], wanted["hand"])
            logger.info("hand step %d: link %.4f deg off the hand", n, angle)
            self.assertLessEqual(angle, 0.5, f"step {n}")

    def _assert_lands(
        self,
        point_world,
        target_world,
        state,
        joint,
        local,
        target_rig,
        where,
        model=None,
        scale=None,
    ):
        """The held point, through the real transforms, is where the
        reference's solve leaves it: its rig-space miss (converged: within
        the solver's tolerance) carried to world units by *scale* (world
        units per rig unit; default the arm scenario's root scale), plus float
        slop. Returns the world miss."""
        m = model or self.scenario.model
        scale = self.scenario.ROOT["s"] if scale is None else scale
        miss = math.dist(m.point(state, joint, local), target_rig)
        world_miss = math.dist(point_world, target_world)
        self.assertLessEqual(
            abs(world_miss - miss * scale),
            5e-5 * max(1.0, scale),
            f"{where}: {world_miss} vs {miss * scale}",
        )
        reach = math.hypot(*local) + sum(
            math.hypot(*m.joints[j]["t"]) for j in m.chain(joint)[1:]
        )
        self.assertLessEqual(
            miss,
            ptk.ArticulationModel.TOLERANCE * reach * 1.0001 + 1e-6,
            f"{where}: not converged",
        )
        return world_miss

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_11_keep_free_and_hinge_grabs(self):
        """Keep: the link's rotation as taken; Free: position only; a hinge
        grab by a DESCENDANT of its part ignores the hand's rotation."""
        m = self.scenario.model
        keep = self._section("ctrl_keep")
        start = self._m(keep["from"])
        link0 = m.world(start)[4][1]
        expected = self._solve(
            start, 4, self._p(keep["grab"]["local"]), self._p(keep["target_rig"]), link0
        )
        self.assertLessEqual(_max_diff(self._m(keep["state"]), expected), 1e-5, "Keep")

        free = self._section("ctrl_free")
        expected = self._solve(
            self._m(free["from"]), 4, self._p(free["grab"]["local"]), self._p(free["target_rig"]), None
        )
        self.assertLessEqual(_max_diff(self._m(free["state"]), expected), 1e-5, "Free")

        hinge = self._section("ctrl_hinge")
        self.assertTrue(hinge["began"])
        self.assertEqual(
            hinge["grab"]["node"], "elbow_geo", "the descendant maps to its grab part"
        )
        self.assertEqual(hinge["grab"]["joint"], 1)
        start = self._m(hinge["from"])
        link0 = m.world(start)[1][1]
        rotation = Q.mul(
            self._q(hinge["hand_rig"]), Q.mul(Q.conj(self._q(hinge["hand0_rig"])), link0)
        )
        local = self._p(hinge["grab"]["local"])
        target = self._p(hinge["target_rig"])
        expected = self._solve(start, 1, local, target, rotation)
        self.assertLessEqual(_max_diff(self._m(hinge["state"]), expected), 1e-5, "Hinge")
        # A hinge's solve ignores the rotation: the same without it.
        bare = self._solve(start, 1, local, target, None)
        self.assertLessEqual(_max_diff(expected, bare), 1e-12)
        # Only the chain above the elbow moved.
        for slot, (joint, _c) in enumerate(m.channels):
            if joint not in m.chain(1):
                self.assertEqual(hinge["state"][slot], hinge["from"][slot])
        self._assert_lands(
            hinge["point_world"],
            self.scenario.inputs()["elbow"]["target"],
            self._m(hinge["state"]),
            1,
            local,
            target,
            "hinge",
        )

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_12_release_holds_until_return(self):
        """EndGrab keeps the pose over the animation; ReturnToAnimation lets
        go; ApplyState holds over a followed animation, and the state reads
        back off the transforms, unwrapped toward it."""
        unity = self._results()
        self.assertEqual(
            self._section("ctrl_after_end"), {"grabbing": False, "holding": True}
        )
        self.assertEqual(unity["ctrl_after_return"], {"holding": False})
        rt = unity["ctrl_apply_read"]
        self.assertTrue(rt["held"], "ApplyState holds over the followed animation")
        self.assertGreater(
            abs(rt["applied"][0]), 180.0, "the scenario winds the first channel"
        )
        self.assertLessEqual(
            _max_diff(rt["applied"], rt["read"]),
            2e-3,
            "read did not unwrap toward the state",
        )

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_12b_held_pose_wins_over_the_animator(self):
        """The LateUpdate path, without play mode: each frame is the
        Animator's edit-mode ``Update(dt)`` (which writes the clip into the
        transforms) followed by the rig's own ``LateUpdate`` (by reflection).
        Following the animation, the rig reads the clip into its values; a
        grab starts from the ANIMATED pose; while held and after release the
        rig's pose wins over the clip every frame; ``ReturnToAnimation`` hands
        the parts back; with ``resumeOnAnimatorChange`` a release holds while
        the Animator stands still and hands back when its time moves."""
        unity = self._results()
        self.assertTrue(self._section("anim_late_found"), "no LateUpdate to invoke")
        self.assertTrue(unity["anim_initialized"])
        writes = unity["anim_writes_in_edit_mode"]
        self.assertAlmostEqual(
            writes["moved_by"], 30.0, delta=0.1, msg="Animator.Update wrote nothing"
        )
        self.assertLess(writes["to_key0"], 0.1)
        self.assertLess(writes["followed"], 1e-2, "the value did not follow the clip")
        start = unity["anim_grab_start"]
        self.assertTrue(start["began"])
        self.assertAlmostEqual(
            start["elbow"],
            start["clip_elbow"],
            delta=1e-3,
            msg="the grab did not start from the animated pose",
        )
        self.assertGreater(abs(start["elbow"] - start["s0_elbow"]), 29.0)
        frames = {f["label"]: f for f in unity["anim_frames"]}
        self.assertEqual(
            list(frames),
            [
                "start",
                "held_a",
                "held_b",
                "released_a",
                "released_b",
                "returned",
                "auto_still",
                "auto_moved",
            ],
        )
        same, differs = 0.1, 0.5  # degrees
        self.assertLess(
            frames["start"]["anim_vs_final"], same, "nothing held: the clip stands"
        )
        for label, grabbing in (
            ("held_a", True),
            ("held_b", True),
            ("released_a", False),
            ("released_b", False),
        ):
            f = frames[label]
            self.assertEqual((f["grabbing"], f["holding"]), (grabbing, True), label)
            self.assertGreater(
                f["anim_vs_final"],
                differs,
                f"{label}: the clip never differed -- nothing to override",
            )
            self.assertLess(
                f["final_vs_held"], same, f"{label}: the held pose did not win"
            )
        self.assertGreater(
            frames["held_b"]["time"],
            frames["held_a"]["time"],
            "the clip kept playing underneath",
        )
        read = unity["anim_read_while_held"]
        self.assertEqual(
            read["diff"], 0.0, "ReadState during a hold read the clip's pose"
        )
        self.assertLess(
            read["final_vs_held"], same, "a read during a hold dropped the held pose"
        )
        f = frames["returned"]
        self.assertFalse(f["holding"])
        self.assertLess(f["anim_vs_final"], same, "ReturnToAnimation did not hand back")
        f = frames["auto_still"]
        self.assertTrue(f["holding"], "a still Animator must not resume")
        self.assertLess(f["final_vs_held"], same)
        f = frames["auto_moved"]
        self.assertFalse(f["holding"], "the Animator's time moved: resume")
        self.assertLess(f["anim_vs_final"], same)

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_13_pointer_grab(self):
        """A ray through real physics takes the part its collider belongs to;
        the drag target lies on the new ray at the grab depth; a scroll step
        pushes the plane by the step fraction; End lets go."""
        p = self._section("pointer")
        self.assertFalse(p["miss_began"], "a ray away from the rig grabs nothing")
        self.assertTrue(p["began"])
        self.assertEqual(p["node"], "head_geo")
        self.assertTrue(p["released"], "End released the rig")
        self.assertGreater(p["depth0"], 0.0)
        o, d, f, v = p["drag_o"], p["drag_d"], p["view_f"], p["view_p"]
        point = p["plane_point"]
        rel = [point[i] - o[i] for i in range(3)]
        along = sum(a * b for a, b in zip(rel, d))
        off = [rel[i] - along * d[i] for i in range(3)]
        self.assertLess(max(map(abs, off)), 1e-4, "the drag target is on the ray")
        depth = sum((point[i] - v[i]) * f[i] for i in range(3))
        self.assertAlmostEqual(depth, p["depth0"], delta=1e-4)
        self.assertAlmostEqual(p["depth1"], p["depth0"] * (1.0 + p["step"]), delta=1e-4)
        # The drag moved the held point toward the plane point.
        self.assertNotEqual(p["after_drag"], p["after_scroll"])

    # ---- Step 5: the importer ------------------------------------------------

    def _imported(self):
        rigs = self._section("imp_rigs")
        return {r["space"]: r for r in rigs}

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_14_importer_fills_the_components_by_reference(self):
        """The data_export channel lands each rig as components: an
        ArticulatedRig on the node its roots hang under, following the parts'
        animation, an ArticulatedJoint on each joint (its parent joint by
        reference), its grab part, and a convex collider on the grab parts'
        meshes only."""
        unity = self._results()
        self._section("imp_loaded")
        self.assertTrue(unity["imp_loaded"], "the ASCII FBX fixture did not import")
        rigs = self._imported()
        self.assertEqual(sorted(rigs), ["arm_RIG", "tree_RIG"])
        for name, rig in self.fixture.rigs.items():
            got = rigs[f"{name}_RIG"]
            self.assertTrue(got["valid"], name)
            self.assertTrue(got["follows"], name)
            self.assertEqual(got["joints"], [j["name"] for j in rig["joints"]])
            self.assertEqual(
                got["parents"], [-1 if j["parent"] is None else j["parent"] for j in rig["joints"]]
            )
            self.assertEqual(got["grabs"], {g["node"]: g["joint"] for g in rig["grab"]})
        col = unity["imp_head_collider"]
        self.assertEqual(
            col, {"found": True, "collider": True, "convex": True, "mesh": True}
        )
        self.assertTrue(unity["imp_cover_found"])
        self.assertFalse(
            unity["imp_cover_collider"], "a mesh outside the grab parts got a collider"
        )

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_15_imported_joints_are_the_records_metres_and_read_back(self):
        """Unity's FBX import, measured on a CENTIMETRE file whose record is in
        METRES: every imported joint's local rotation and position is the
        model's local pose of the written state, mirrored (rotate orders xyz,
        zyx, yxz, xzy, zxy, yzx; orients; slides along a rotated frame) -- the
        import frames the file in metres, so the record applies as it is,
        nothing measured -- and the rig reads the state back."""
        rigs = self._imported()
        expected = self.fixture.expected()
        for name, model in self.fixture.models.items():
            rig, exp = rigs[f"{name}_RIG"], expected[name]
            for joint, ((t, q), (ut, uq)) in enumerate(zip(exp["pose"], rig["unity_locals"])):
                where = f"{name} joint {joint}"
                self.assertLessEqual(
                    _close_quat(Unity.rotation(uq), q), 1e-5, f"{where}: rotation"
                )
                self.assertLessEqual(
                    _max_diff(ut, Unity.point(t)), 1e-6, f"{where}: position {ut} vs {Unity.point(t)} m"
                )
            self.assertLessEqual(
                _max_diff(Unity.state(model, rig["state"]), exp["state"]), 2e-3, name
            )

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_16_a_version_1_reexport_is_refused_and_one_without_the_carrier_drops_the_rig(self):
        """A re-export carrying a version 1 record is refused with its fix (no
        rig: version 1 has no anchors and the file's own units), the original
        brings the components back, and one without the carrier drops them."""
        unity = self._results()
        v1 = self._section("imp_after_v1")
        self.assertEqual((v1["loaded"], v1["rigs"], v1["joints"]), (True, 0, 0))
        self.assertTrue(
            any("articulation record v1" in w and "re-export" in w for w in v1["warnings"]),
            v1["warnings"],
        )
        restored = unity["imp_after_restore"]
        self.assertEqual((restored["loaded"], restored["rigs"]), (True, 2))
        gone = unity["imp_after_nocarrier"]
        self.assertEqual((gone["loaded"], gone["rigs"], gone["joints"]), (True, 0, 0))

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_17_xr_adapter_inert_without_xri(self):
        """Without the XR Interaction Toolkit the XR assemblies are excluded by
        their define constraint -- the project still compiles (these results
        exist) and the assembly is simply absent."""
        self.assertFalse(self._results()["xr_assembly_loaded"])

    # ---- Step 6: a real DCC export (env-gated) -------------------------------

    def _live(self):
        if not _live_fbx():
            self.skipTest(EnvSkip.reason(f"no live DCC export (set {LIVE_FBX_ENV})"))
        cls = self.__class__
        if getattr(cls, "live_record", None) is None:
            cls.live_record = _live_record(_live_fbx())
        return cls.live_record

    def _live_model(self):
        """The reference model of the live rig, as its components hold it."""
        return ptk.ArticulationModel(_f32(self._live()["rigs"][0]))

    @staticmethod
    def _residual(model, rows, state):
        """Max |pose(state) - the imported locals| (metres; quaternion
        components, sign-insensitive), the locals mirrored back."""
        dt = dq = 0.0
        for row, (et, eq) in zip(rows, model.pose(state)):
            dt = max(dt, _max_diff(Unity.point(row["unity_t"]), et))
            dq = max(dq, _close_quat(Unity.rotation(row["unity_q"]), eq))
        return dt, dq

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_18_live_export_imports_bound(self):
        """The importer, on a real export: one rig per record rig, every joint
        filled, every grab part given a convex collider."""
        record = self._live()
        unity = self._results()
        self._section("live_loaded")
        self.assertTrue(unity["live_loaded"])
        rigs = unity["live_rigs"]
        spec = record["rigs"][0]
        logger.info(
            "live export: %d rig(s), %d joints, %d grab parts, space scale %s",
            len(rigs), len(spec["joints"]), len(spec["grab"]),
            unity.get("live_rig", {}).get("space_scale"),
        )  # fmt: skip
        self.assertEqual(len(rigs), len(record["rigs"]))
        self.assertTrue(rigs[0]["valid"])
        self.assertEqual(rigs[0]["joints"], [j["name"] for j in spec["joints"]])
        self.assertEqual(
            sorted(g["name"] for g in unity["live_rig"]["grab"]),
            sorted(g["node"] for g in spec["grab"]),
        )
        for g in unity["live_rig"]["grab"]:
            self.assertGreater(g["colliders"], 0, g["name"])
            self.assertTrue(g["convex"], g["name"])

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_19_live_default_pose_is_a_channel_pose_of_the_record(self):
        """The frame rule on a real export: every imported joint's local,
        mirrored back, IS the record's pose at the state the rig reads off it --
        which holds only where Unity frames each joint as the record states it
        (in metres: a joint framed at another scale misses by its whole rest
        offset, a wrong mirror reads a hinge off its axis)."""
        model = self._live_model()
        default = self._section("live_default")
        state = Unity.state(model, default["state"])
        rt, rq = self._residual(model, default["locals"], state)
        logger.info(
            "live default pose: residual t %.3g m, q %.3g; imported locals %s m, the record's rests %s m",
            rt, rq, [row["unity_t"] for row in default["locals"]],
            [list(Unity.point(j["t"])) for j in self._live()["rigs"][0]["joints"]],
        )  # fmt: skip
        self.assertLessEqual(rt, 2e-5 + 1e-5 * max(
            math.hypot(*j["t"]) for j in self._live()["rigs"][0]["joints"]
        ))
        self.assertLessEqual(rq, 2e-5)

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_20_live_clip_samples_are_channel_poses(self):
        """Each clip, sampled into the transforms at its start and the frame
        nearest its middle: every sample reads back as a state whose pose IS
        the sample. Mid-clip, Unity's default Keyframe Reduction (0.5 degree
        rotation error) and its component-wise quaternion interpolation bound
        the agreement, not the port."""
        model = self._live_model()
        samples = self._section("live_clips")
        if not samples:
            self.skipTest(EnvSkip.reason("the live export carries no clip"))
        worst = [0.0, 0.0]
        for sample in samples:
            where = f"{sample['clip']} @ {sample['time']}"
            rt, rq = self._residual(model, sample["locals"], Unity.state(model, sample["state"]))
            worst = [max(worst[0], rt), max(worst[1], rq)]
            self.assertLessEqual(rt, 1e-4, where)
            self.assertLessEqual(rq, 5e-4, where)
        logger.info("live clip samples (%d): residual t %.3g q %.3g", len(samples), *worst)

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_21_live_hand_grab_lands(self):
        """A Hand grab on a grab part of the real export: every step equals
        pythontk's solve from the same inputs, and the held point lands on each
        world target through the imported transforms."""
        model = self._live_model()
        grab = self._section("live_grab")
        unity = self._results()
        self.assertTrue(grab["began"])
        joint = grab["joint"]
        start = Unity.state(model, grab["from"])
        link0 = model.world(start)[joint][1]
        hand0 = Unity.rotation(grab["hand0_rig"])
        self.assertLess(Q.angle(hand0, link0), 1e-3, "the hand starts on the link")
        offset = Q.mul(Q.conj(hand0), link0)
        state = start
        local = Unity.point(grab["local"])
        scale = unity["live_rig"]["space_scale"]
        ball = {c for j, c in model.channels if j == joint and c[0] == "r"} == {"rx", "ry", "rz"}
        misses = []
        for n, step in enumerate(grab["steps"]):
            rotation = Q.mul(Unity.rotation(step["hand_rig"]), offset)
            target = Unity.point(step["target_rig"])
            state = model.solve(state, joint, local, target, rotation)
            self.assertLessEqual(
                _max_diff(Unity.state(model, step["state"]), state), 1e-5, f"step {n}"
            )
            if ball:
                self.assertLessEqual(
                    Q.angle(step["link_world_q"], step["hand_world"]),
                    0.5,
                    f"step {n}: the link left the hand",
                )
            misses.append(
                self._assert_lands(
                    step["point_world"], step["target_world"], state, joint, local,
                    target, f"step {n}", model=model, scale=scale,
                )  # fmt: skip
            )
        logger.info("live hand grab: %d steps, world miss max %.3g", len(misses), max(misses))


# ======================================================================
# Test class: a project WITH the XR Interaction Toolkit
# ======================================================================

#: The XRI release the door is compiled against (3.x: the namespaces moved
#: in 3.0, which is what the versionDefines expression pins). 3.3.1 is the
#: release this suite was first measured green with on Unity 6000.3.
XRI_VERSION = os.environ.get("UNITYTK_TEST_XRI_VERSION", "3.3.1")

#: The XR verifier's own assembly: a door is ``autoReferenced: false``, so only
#: an assembly that names it sees its types.
UNITY_XR_VERIFIER_ASMDEF = {
    "name": "ArticulatedRigXRVerifier",
    "references": [
        "M3trik.Unitytk.Core",
        "M3trik.Unitytk.Kernel",
        "M3trik.Unitytk.ArticulatedRig.Model",
        "M3trik.Unitytk.ArticulatedRig",
        "M3trik.Unitytk.ArticulatedRig.Editor",
        "M3trik.Unitytk.ArticulatedRig.XRI",
        "Unity.XR.Interaction.Toolkit",
        "Unity.XR.CoreUtils",
    ],
    "includePlatforms": ["Editor"],
}

UNITY_XR_VERIFIER_CS = r"""
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using M3trik.Unitytk.ArticulatedRig;
using M3trik.Unitytk.ArticulatedRig.XRI;
using UnityEditor;
using UnityEngine;
using Rig = M3trik.Unitytk.ArticulatedRig.ArticulatedRig;

public class ArticulatedRigXRVerifier
{
    const string RESULTS = @"__RESULTS_PATH__";
    const string INPUTS = @"__INPUTS_PATH__";

    static string B(bool b) => b ? "true" : "false";
    static string N(double v) => M3trik.Unitytk.Core.Json.Number(v);
    static IList<object> L(object o) => (IList<object>)o;
    static IDictionary<string, object> O(object o) => (IDictionary<string, object>)o;
    static float F(object o) => (float)(double)o;
    static Vector3 V3(object o) { var l = L(o); return new Vector3(F(l[0]), F(l[1]), F(l[2])); }
    static Quaternion Q4(object o) { var l = L(o); return new Quaternion(F(l[0]), F(l[1]), F(l[2]), F(l[3])); }

    public static void Verify()
    {
        var sb = new StringBuilder("{\n");
        try
        {
            var inputs = O(M3trik.Unitytk.Core.Json.Parse(File.ReadAllText(INPUTS), false));
            var joints = L(O(inputs["rig"])["joints"]);
            var root = new GameObject("ArmModel");
            var space = new GameObject("arm_RIG").transform;
            space.SetParent(root.transform, false);
            var rig = space.gameObject.AddComponent<Rig>();
            var parts = new Transform[joints.Count];
            for (int j = 0; j < joints.Count; j++)
            {
                var js = O(joints[j]);
                parts[j] = new GameObject((string)js["name"]).transform;
                parts[j].SetParent(js["parent"] == null ? space : parts[(int)(double)js["parent"]], false);
                parts[j].localPosition = V3(js["t"]);
                parts[j].localRotation = Q4(js["q"]);
                var joint = parts[j].gameObject.AddComponent<ArticulatedJoint>();
                joint.parent = js["parent"] == null ? null : rig.joints[(int)(double)js["parent"]];
                joint.restPosition = V3(js["t"]);
                joint.restRotation = Q4(js["q"]);
                joint.rotateOrder = (ArticulatedJoint.RotateOrder)Array.IndexOf(ArticulationModel.RotateOrders, (string)js["rotate_order"]);
                foreach (var raw in L(js["channels"]))
                {
                    var c = O(raw);
                    joint.channels.Add(new ArticulatedJoint.Channel
                    {
                        axis = (ArticulatedJoint.Axis)Array.IndexOf(ArticulationModel.ChannelNames, (string)c["channel"]),
                        hasMin = c["min"] != null, min = c["min"] == null ? 0f : F(c["min"]),
                        hasMax = c["max"] != null, max = c["max"] == null ? 0f : F(c["max"]),
                        weight = F(c["weight"]),
                    });
                }
                rig.joints.Add(joint);
            }
            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(head.GetComponent<Collider>());
            head.name = "head_geo";
            head.transform.SetParent(parts[4], false);
            head.transform.localScale = Vector3.one * 0.02f;
            rig.grabs.Add(new Rig.GrabPart { part = head.transform, joint = rig.joints[4] });
            rig.Rebuild();

            // The action, through the door's editor half.
            bool made = ArticulatedRigActions.MakeXRGrabbable(rig);
            var setup = rig.GetComponent<ArticulatedRigXRSetup>();
            sb.Append("\"made\": " + B(made && setup != null && setup.rig == rig) + ",\n");
            var grabs = setup.Build();
            var again = setup.Build();
            sb.Append("\"grabs\": " + grabs.Count + ",\n");
            sb.Append("\"idempotent\": " + B(again.Count == 1 && head.GetComponents<ArticulatedRigXRGrab>().Length == 1) + ",\n");
            sb.Append("\"on_part\": " + B(grabs.Count == 1 && grabs[0].gameObject == head) + ",\n");
            sb.Append("\"collider\": " + B(head.GetComponent<MeshCollider>() != null) + ",\n");
            var grab = grabs[0];
            var before = rig.GetState();
            var start = head.transform.position;
            bool began = grab.GrabAt(start, head.transform.rotation);
            sb.Append("\"began\": " + B(began) + ",\n");
            sb.Append("\"holding\": " + B(grab.IsHolding) + ",\n");
            grab.FollowTo(start + new Vector3(0.01f, 0.015f, 0f), head.transform.rotation);
            var after = rig.GetState();
            sb.Append("\"moved\": " + B(!before.SequenceEqual(after)) + ",\n");
            sb.Append("\"moved_by\": " + N(Vector3.Distance(rig.GrabPointWorld, start)) + ",\n");
            grab.Release();
            sb.Append("\"released\": " + B(!rig.IsGrabbing && rig.IsHoldingPose) + ",\n");
            sb.Append("\"is_xri\": " + B(grab is UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable) + ",\n");
            sb.Append("\"not_grab_interactable\": " + B(head.GetComponent<Rigidbody>() == null) + ",\n");
            // The Input System door: compiled in (its define fired), and its
            // registration -- which runs as the player loads -- puts its mouse
            // behind the grabber's pointer seam.
            var door = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "M3trik.Unitytk.ArticulatedRig.InputSystem");
            var pointerType = door == null ? null : door.GetType("M3trik.Unitytk.ArticulatedRig.InputSystem.InputSystemPointer");
            var register = pointerType == null ? null : pointerType.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic);
            if (register != null) register.Invoke(null, null);
            sb.Append("\"input_system_door\": " + B(door != null) + ",\n");
            sb.Append("\"pointer_from_input_system\": " + B(pointerType != null && ArticulatedRigPointerGrab.Pointer.GetType() == pointerType) + ",\n");
            ArticulatedRigPointerGrab.UsePointer(null);
            UnityEngine.Object.DestroyImmediate(root);
        }
        catch (Exception e)
        {
            sb.Append("\"error\": \"" + e.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\",\n");
        }
        var names = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToList();
        sb.Append("\"xr_editor_assembly_loaded\": " + B(names.Contains("M3trik.Unitytk.ArticulatedRig.XRI.Editor")) + ",\n");
        sb.Append("\"xr_assembly_loaded\": " + B(names.Contains("M3trik.Unitytk.ArticulatedRig.XRI")) + "\n}\n");
        File.WriteAllText(RESULTS, sb.ToString());
        EditorApplication.Exit(0);
    }
}
"""


class TestArticulatedRigXRI(unittest.TestCase):
    """The XRI and Input System doors in a project that has both packages."""

    @classmethod
    def setUpClass(cls):
        cls.temp_dir = TempProjects.allocate("articulated_rig_xri", cls)
        cls.unity = None

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_01_xr_door_compiles_and_drives_the_rig(self):
        """XRI present: both halves of the XRI door compile (their define
        fires), MakeXRGrabbable puts the setup on the rig, the setup makes one
        interactable per grab part (idempotently), and the interactable's grab
        forwarding poses the rig. The project runs both input backends, so the
        Input System door compiles too, and its registration puts its mouse
        behind the pointer grabber's seam."""
        doc = ptk.ArticulationConformance.cases(seed=SEED, per_rig=1)
        rig = _metres(doc["rigs"]["arm"], 0.01)
        cls = self.__class__
        xr_inputs = {"rig": Unity.rig(rig)}

        def prepare(project_path, results_path):
            manifest_path = os.path.join(project_path, "Packages", "manifest.json")
            with open(manifest_path, encoding="utf-8") as fh:
                manifest = json.load(fh)
            manifest["dependencies"]["com.unity.xr.interaction.toolkit"] = XRI_VERSION
            with open(manifest_path, "w", encoding="utf-8") as fh:
                json.dump(manifest, fh, indent=2)
            # Both input backends: ENABLE_INPUT_SYSTEM and the legacy define.
            settings = os.path.join(
                project_path, "ProjectSettings", "ProjectSettings.asset"
            )
            with open(settings, encoding="utf-8") as fh:
                text = fh.read()
            text, n = re.subn(r"activeInputHandler: \d", "activeInputHandler: 2", text)
            if not n:
                text = text.replace(
                    "  productName:", "  activeInputHandler: 2\n  productName:", 1
                )
            with open(settings, "w", encoding="utf-8") as fh:
                fh.write(text)
            UnityHost.install(project_path, "local")
            inputs_path = os.path.join(cls.temp_dir, "xr_inputs.json")
            with open(inputs_path, "w", encoding="utf-8") as fh:
                json.dump(xr_inputs, fh)
            editor_dir = os.path.join(project_path, "Assets", "Editor")
            os.makedirs(editor_dir, exist_ok=True)
            with open(
                os.path.join(editor_dir, "ArticulatedRigXRVerifier.asmdef"),
                "w",
                encoding="utf-8",
            ) as fh:
                json.dump(UNITY_XR_VERIFIER_ASMDEF, fh, indent=4)
            with open(
                os.path.join(editor_dir, "ArticulatedRigXRVerifier.cs"),
                "w",
                encoding="utf-8",
            ) as fh:
                fh.write(
                    UNITY_XR_VERIFIER_CS.replace(
                        "__RESULTS_PATH__", results_path
                    ).replace("__INPUTS_PATH__", inputs_path)
                )

        unity, rc, log_path, log_text = _run_unity(
            self, cls.temp_dir, "ArticulatedRigXRVerifier.Verify", prepare, timeout=1500
        )
        if unity is None:
            if re.search(
                r"(Cannot|Unable to) (connect|resolve|fetch|add)|"
                r"com\.unity\.xr\.interaction\.toolkit.*(not found|cannot be found)|"
                r"An error occurred while resolving packages",
                log_text,
                flags=re.I,
            ):
                self.skipTest(
                    EnvSkip.reason(
                        "the XR Interaction Toolkit package could not be resolved "
                        "(offline, or the version is not on the registry)"
                    )
                )
            self.fail(
                f"XRI results not created (Unity rc={rc}) — check {log_path}\n"
                + _compile_errors(log_path)
            )
        self.assertNotIn("error", unity, unity.get("error"))
        self.assertTrue(unity["xr_assembly_loaded"], "the XRI door did not compile in")
        self.assertTrue(
            unity["xr_editor_assembly_loaded"], "the XRI door's editor half did not compile in"
        )
        self.assertTrue(unity["made"], "MakeXRGrabbable put no setup on the rig")
        self.assertEqual(unity["grabs"], 1)
        self.assertTrue(unity["idempotent"])
        self.assertTrue(unity["on_part"])
        self.assertTrue(unity["collider"])
        self.assertTrue(unity["is_xri"])
        self.assertTrue(
            unity["not_grab_interactable"], "the part must not get a Rigidbody"
        )
        self.assertTrue(unity["began"])
        self.assertTrue(unity["holding"])
        self.assertTrue(unity["moved"], "FollowTo did not solve")
        self.assertGreater(unity["moved_by"], 1e-3)
        self.assertTrue(unity["released"])
        self.assertTrue(
            unity["input_system_door"], "the Input System door did not compile in"
        )
        self.assertTrue(
            unity["pointer_from_input_system"],
            "the Input System door did not register its pointer",
        )


# ======================================================================
# Test class: the model under `dotnet test` (no Unity)
# ======================================================================

#: The engine-free models' test project: Core and every feature's Model part.
MODELS_DOTNET = os.path.join(os.path.dirname(os.path.abspath(__file__)), "models_dotnet")


class TestArticulationModelDotnet(unittest.TestCase):
    """The port held to the same golden cases on plain .NET: its Model part
    has no engine reference, so it builds and runs without Unity."""

    @EnvSkip.unless(shutil.which("dotnet") is not None, "the .NET SDK is not installed")
    def test_the_model_passes_its_conformance_cases_under_dotnet(self):
        """``dotnet test`` in ``tests/models_dotnet``, its cases generated by
        this interpreter's pythontk (``UNITYTK_PYTHON``)."""
        env = dict(os.environ, UNITYTK_PYTHON=sys.executable)
        done = subprocess.run(
            ["dotnet", "test", MODELS_DOTNET, "--nologo"],
            capture_output=True,
            text=True,
            timeout=900,
            env=env,
        )
        self.assertEqual(done.returncode, 0, done.stdout[-4000:] + done.stderr[-2000:])
        passed = re.search(r"Passed:\s+(\d+)", done.stdout)
        self.assertTrue(passed and int(passed.group(1)) > 0, done.stdout[-2000:])
        self.assertIsNone(re.search(r"Skipped:\s+[1-9]", done.stdout), "a conformance test skipped")


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
    unittest.main(verbosity=2)
