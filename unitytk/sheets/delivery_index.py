# !/usr/bin/python
# coding=utf-8
"""DeliveryIndex -- a delivery's nodes and takes, as a plan names them.

A plan names nodes by their path in the delivery (``A|B|C``, from the file's top
level down) and clips by their take's name. This holds both, read from the FBX
(:meth:`from_fbx`, pythontk's ``FbxFile``: no DCC, no Unity), a GLB
(:meth:`from_glb`), several files as one (:meth:`merged`) or handed in
(:meth:`__init__`, from any other reader: Unity's imported hierarchy, a test).

The objects a sheet's words can name are grouped by their **object name**: a node
name with its rig and layout suffixes taken off (``DOOR_GRP``, ``DOOR_LOC`` and
``DOOR_GEO`` are all the object ``DOOR``), each object standing for the top-most
node of its chain, whose subtree is the object (its geometry, its pivot). Two
separate chains of one object name are two objects, which a phrase alone cannot
tell apart. That is the tolerant reading's (:meth:`DeliveryIndex.find`); a sheet in
cells names a node exactly (:meth:`DeliveryIndex.named`): its own name, the end of its
path, or a declared alias.
"""

import re
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Optional, Tuple, Union

#: Node-name endings that say how a node is built, not what it is: a chain's
#: group, geometry, pivot, rig and joints share its object's name.
SUFFIXES = (
    "GRP",
    "GROUP",
    "GEO",
    "GEOM",
    "MESH",
    "LOC",
    "PIVOT",
    "RIG",
    "JNT",
    "JOINT",
    "CTRL",
    "CTL",
    "SHAPE",
)

#: The channels whose change of value moves a node in a take.
MOTION = ("Lcl Translation", "Lcl Rotation", "Lcl Scaling", "Visibility")
#: The change of value under which a keyed channel holds still.
MOTION_TOLERANCE = 1e-5


class _DeliveryIndexInternal:
    """Internal helpers for :class:`DeliveryIndex`."""

    @staticmethod
    def _leaf(path: str) -> str:
        return path.rsplit("|", 1)[-1]

    @staticmethod
    def _is_carrier(path: str) -> bool:
        """Whether the node *path* is a carrier of the delivery's records (the scene's own, a module's
        ``NS:data_export``, an import's numbered copy): never an object a sheet names."""
        import pythontk as ptk

        return ptk.SceneStoreBase.is_carrier(path)

    @classmethod
    def _top_most(cls, paths: List[str]) -> List[str]:
        """The paths of *paths* with no other of them above (one per chain)."""
        ordered = sorted(paths, key=lambda p: p.count("|"))
        tops: List[str] = []
        for path in ordered:
            if not any(path.startswith(top + "|") for top in tops):
                tops.append(path)
        return tops

    @staticmethod
    def _records(fbx) -> Dict[str, str]:
        """``{channel: JSON text}`` the carriers' user properties hold, read with their paths: the
        scene's own carrier first, then a module's (``SceneStoreBase.is_carrier``), the first to hold
        a channel giving it."""
        import pythontk as ptk

        store = ptk.SceneStoreBase
        found = [
            item
            for item in fbx.user_property_items(paths=True)
            if store.is_carrier(item[0])
        ]
        found.sort(key=lambda item: not store.is_scene_carrier(item[0]))
        out: Dict[str, str] = {}
        for _, name, value in found:
            text = (
                value.decode("utf-8", "replace") if isinstance(value, bytes) else value
            )
            if isinstance(text, str):
                out.setdefault(name, text)
        return out

    @classmethod
    def _motion(
        cls, fbx, paths: List[str]
    ) -> Tuple[Dict[str, Tuple[str, ...]], List[Dict[str, Any]], List[str]]:
        """``(moved, jumps, issues)`` of *fbx*'s takes: what each moves (:meth:`_moved`) and the takes that end
        on the next take's setup (``FbxFile.take_setup_jumps``); where its keys cannot be read, neither, and
        the issue saying so."""
        try:
            return cls._moved(fbx.take_keys(MOTION), paths), fbx.take_setup_jumps(), []
        except ValueError as exc:  # a malformed curve: nothing of the takes' motion can be told
            return (
                {},
                [],
                [
                    f"the delivery's keys cannot be read ({exc}): what each take moves and where it ends "
                    "are unchecked; export it again"
                ],
            )

    @classmethod
    def _moved(cls, keys, paths: List[str]) -> Dict[str, Tuple[str, ...]]:
        """``{take: node paths}`` each take moves: a keyed channel of :data:`MOTION` whose values differ, from
        *keys* (``FbxFile.take_keys``); ``{}`` where no take keys any (such a file states no motion)."""
        if not any(keys.values()):
            return {}
        by_leaf: Dict[str, List[str]] = {}
        for path in paths:
            by_leaf.setdefault(cls._leaf(path), []).append(path)
        out: Dict[str, Tuple[str, ...]] = {}
        for take, channels in keys.items():
            found: List[str] = []
            for (target, _prop, _comp), curve in channels.items():
                values = [key[1] for key in curve]
                if values and max(values) - min(values) > MOTION_TOLERANCE:
                    # ``name#uid`` where names repeat: every node of that name.
                    found += by_leaf.get(target.split("#", 1)[0], [])
            out[take] = tuple(dict.fromkeys(found))
        return out


class DeliveryIndex(_DeliveryIndexInternal):
    """A delivery's node paths and take names.

    Parameters:
        paths: Every node's path, ``|`` joined from the file's top level down.
        takes: The take (clip) names, in the file's order.
        name: The delivery's file name.
        descriptions: ``{take: description}`` where the delivery states them
            (its ``animation`` record).
        kinds: ``{path: node kind}`` where known (an FBX Model's: ``Mesh``,
            ``Null``, ``Camera``, ``LimbNode``).
        keyed: ``{step ID: (object name, behavior) pairs}`` the delivery's own build keyed into the
            step's takes (each Shot Manifest behaviors entry's ``mode``): a plan of the same sheet
            leaves them to the delivery.
        steps: ``{take: step ID}`` where the delivery records the step each take
            plays (its ``animation`` record's shot metadata).
        moved: ``{take: node paths}`` the nodes each take moves (a keyed
            transform or visibility whose value changes); a take absent from it
            moves what is not known.
        jumps: The takes that end on the next take's setup, as
            ``FbxFile.take_setup_jumps`` reports them (``{take, next, target,
            frame, shown, properties}``); none where the delivery was not read
            for them.
        aliases: ``{old name: new name}`` for renamed nodes: :meth:`find` reads
            an old name as its new one (a project's sheet settings hold them).
        suffixes: The build suffixes an object name drops (:data:`SUFFIXES` if
            None): a project naming its nodes ``DOOR_MSH`` states ``["MSH"]``.
        issues: What the delivery could not be read for, each a sentence with its fix (its keys: then
            :attr:`moved` and :attr:`jumps` state nothing); a plan read against it carries them.
        worn: ``{path: material names}`` each node wears, where known (an FBX's materials).
        states: ``{material name: state names}`` the delivery's ``render_effects`` record gives a
            material's emission (its emissive group's states), by the name after the last ``:``.
    """

    def __init__(
        self,
        paths: Iterable[str],
        takes: Iterable[str] = (),
        name: str = "",
        descriptions: Optional[Mapping[str, str]] = None,
        kinds: Optional[Mapping[str, str]] = None,
        steps: Optional[Mapping[str, str]] = None,
        moved: Optional[Mapping[str, Iterable[str]]] = None,
        aliases: Optional[Mapping[str, str]] = None,
        suffixes: Optional[Iterable[str]] = None,
        jumps: Optional[Iterable[Mapping[str, Any]]] = None,
        keyed: Optional[Mapping[str, Iterable[Tuple[str, str]]]] = None,
        issues: Iterable[str] = (),
        worn: Optional[Mapping[str, Iterable[str]]] = None,
        states: Optional[Mapping[str, Iterable[str]]] = None,
    ):
        self.issues: Tuple[str, ...] = tuple(issues)
        self.worn: Dict[str, Tuple[str, ...]] = {p: tuple(m) for p, m in (worn or {}).items()}
        self.states: Dict[str, Tuple[str, ...]] = {
            str(m).rsplit(":", 1)[-1]: tuple(s) for m, s in (states or {}).items()
        }
        self.keyed: Dict[str, Tuple[Tuple[str, str], ...]] = {
            s: tuple((str(n).lower(), str(b)) for n, b in pairs)
            for s, pairs in (keyed or {}).items()
        }
        self.jumps: Tuple[Dict[str, Any], ...] = tuple(dict(j) for j in jumps or ())
        self.paths: Tuple[str, ...] = tuple(
            p for p in paths if p and not self._is_carrier(p)
        )
        self.takes: Tuple[str, ...] = tuple(takes)
        self.name = name
        self.descriptions: Dict[str, str] = dict(descriptions or {})
        self.kinds: Dict[str, str] = dict(kinds or {})
        self.steps: Dict[str, str] = dict(steps or {})
        self.moved: Dict[str, Tuple[str, ...]] = {
            t: tuple(p) for t, p in (moved or {}).items()
        }
        self.aliases: Dict[str, str] = dict(aliases or {})
        self.suffixes: Tuple[str, ...] = (
            tuple(suffixes) if suffixes is not None else SUFFIXES
        )
        self._suffix_rx = self._suffix_pattern(self.suffixes)
        chains: Dict[str, List[str]] = {}
        for path in self.paths:
            chains.setdefault(self.name_of(path), []).append(path)
        self.objects: Dict[str, Tuple[str, ...]] = {
            obj: tuple(self._top_most(found)) for obj, found in chains.items() if obj
        }
        # Built once, so a name is looked up among the nodes that share its last name, never every path.
        self._known = frozenset(self.paths)
        self._by_leaf: Dict[str, List[str]] = {}
        for path in self.paths:
            self._by_leaf.setdefault(self._leaf(path).casefold(), []).append(path)
        self._by_object: Dict[str, List[str]] = {}
        for obj, tops in self.objects.items():
            self._by_object.setdefault(obj.casefold(), []).extend(tops)
        self._renamed = {old.casefold(): new for old, new in self.aliases.items()}
        # Each name a sheet may write for a node, as the delivery (or the settings' aliases) spells it.
        self._spelled: Dict[str, List[str]] = {}
        for name in [self._leaf(p) for p in self.paths] + list(self.aliases):
            spelled = self._spelled.setdefault(name.casefold(), [])
            if name not in spelled:
                spelled.append(name)

    @classmethod
    def from_fbx(cls, path: Union[str, Path]) -> "DeliveryIndex":
        """Read *path*'s nodes, takes, what each take moves (its keys) and
        (where its ``animation`` record, of the version ``SceneRecords.SHOTS``
        declares, has them) the takes' descriptions and steps."""
        import pythontk as ptk

        fbx = ptk.FbxFile.load(
            str(path), raw_payloads=False
        )  # its media are never read here
        paths, kinds, by_id, materials = [], {}, {}, {}
        for record in fbx.iter_objects():
            if record.get("name") == "Material" and record.get("props"):
                materials[record["props"][0]] = fbx.object_name(record) or ""
            if record.get("name") != "Model":
                continue
            node = fbx.object_path(record)
            if not node:
                continue
            paths.append(node)
            if record.get("props"):
                by_id[record["props"][0]] = node
            props = record.get("props") or []
            if len(props) > 2 and isinstance(props[2], (bytes, str)):
                kind = (
                    props[2].decode("utf-8", "replace")
                    if isinstance(props[2], bytes)
                    else props[2]
                )
                kinds[node] = kind
        worn: Dict[str, List[str]] = {}
        for kind, child, parent, _prop in fbx.connections():
            if kind == "OO" and child in materials and parent in by_id:
                worn.setdefault(by_id[parent], []).append(materials[child])
        records = cls._records(fbx)
        descriptions: Dict[str, str] = {}
        steps: Dict[str, str] = {}
        keyed: Dict[str, List[Tuple[str, str]]] = {}
        record = ptk.SceneRecords.SHOTS.decode(records.get("animation"))
        # Only the declared version's shots are the file's takes (an older record's are not: the
        # contract has its reader refuse it); one this cannot read states nothing.
        if (
            isinstance(record, dict)
            and record.get("version") == ptk.SceneRecords.SHOTS.version
        ):
            for shot in record.get("shots") or []:
                if not isinstance(shot, dict) or not shot.get("clip"):
                    continue
                if shot.get("description"):
                    descriptions[shot["clip"]] = shot["description"]
                metadata = (
                    shot.get("metadata")
                    if isinstance(shot.get("metadata"), dict)
                    else {}
                )
                step = metadata.get("step")
                if isinstance(step, str) and step:
                    legal = ptk.StrUtils.to_legal_name(step)
                    steps[shot["clip"]] = legal
                    # What the delivery's own build keyed into the step's takes, object by object
                    # (each behaviors entry's mode): a plan of the same sheet leaves those to it.
                    for entry in metadata.get("behaviors") or []:
                        if (
                            isinstance(entry, dict)
                            and entry.get("mode") == "key"
                            and entry.get("name")
                            and entry.get("behavior")
                        ):
                            keyed.setdefault(legal, []).append(
                                (entry["name"], entry["behavior"])
                            )
        moved, jumps, issues = cls._motion(fbx, paths)
        return cls(
            paths,
            fbx.take_names(),
            Path(path).name,
            descriptions,
            kinds,
            steps,
            moved,
            jumps=jumps,
            keyed=keyed,
            issues=[f"{Path(path).name}: {i}" for i in issues],
            worn=worn,
            states=cls._states(records.get("render_effects")),
        )

    @staticmethod
    def _states(text: Optional[str]) -> Dict[str, Tuple[str, ...]]:
        """``{material: state names}`` the ``render_effects`` record *text* gives each material's emission, of the
        version ``SceneRecords.VISIBILITY`` declares (one this cannot read states none)."""
        import pythontk as ptk

        record = ptk.SceneRecords.VISIBILITY.decode(text)
        if not isinstance(record, dict) or record.get("version") != ptk.SceneRecords.VISIBILITY.version:
            return {}
        out: Dict[str, Tuple[str, ...]] = {}
        for entry in record.get("materials") or []:
            if not isinstance(entry, dict) or not entry.get("material"):
                continue
            names = tuple(s["name"] for s in entry.get("states") or [] if isinstance(s, dict) and s.get("name"))
            if names:
                out[str(entry["material"])] = names
        return out

    @classmethod
    def from_glb(cls, path: Union[str, Path]) -> "DeliveryIndex":
        """Read a GLB's nodes (by path, from its top level down) and its animations as the takes, through
        pythontk's ``GlbReader``; a node holding a mesh is a ``Mesh``, a camera a ``Camera``."""
        import pythontk as ptk

        reader = ptk.GlbReader.load(str(path))
        nodes = reader.gltf.get("nodes") or []
        names = [n.get("name") or str(i) for i, n in enumerate(nodes)]
        paths, kinds = [], {}
        for i, node in enumerate(nodes):
            chain, at = [names[i]], reader.parent_of(i)
            while at is not None and len(chain) <= len(nodes):
                chain.append(names[at])
                at = reader.parent_of(at)
            paths.append("|".join(reversed(chain)))
            kinds[paths[-1]] = (
                "Mesh" if "mesh" in node else "Camera" if "camera" in node else "Null"
            )
        return cls(paths, reader.animations(), Path(path).name, kinds=kinds)

    @classmethod
    def from_file(cls, path: Union[str, Path]) -> "DeliveryIndex":
        """:meth:`from_glb` for a ``.glb``, else :meth:`from_fbx`."""
        return (
            cls.from_glb(path)
            if str(path).lower().endswith(".glb")
            else cls.from_fbx(path)
        )

    @staticmethod
    def _suffix_pattern(suffixes: Iterable[str]) -> "re.Pattern":
        words = (
            "|".join(re.escape(s.strip("_")) for s in suffixes if s.strip("_"))
            or "(?!)"
        )
        return re.compile(r"(?:_(?:%s)(?:_?\d+)?)+$" % words, re.I)

    @classmethod
    def object_name(cls, node: str, suffixes: Optional[Iterable[str]] = None) -> str:
        """*node*'s object name: its build suffixes off (``DOOR_GEO`` -> ``DOOR``).

        Parameters:
            node: A node's name or path.
            suffixes: The build suffixes (:data:`SUFFIXES` if None).
        """
        name = node.rsplit("|", 1)[-1].rsplit(":", 1)[-1]
        stripped = cls._suffix_pattern(SUFFIXES if suffixes is None else suffixes).sub(
            "", name
        )
        return stripped or name

    def name_of(self, node: str) -> str:
        """*node*'s object name by this delivery's own suffixes (:meth:`object_name`)."""
        name = node.rsplit("|", 1)[-1].rsplit(":", 1)[-1]
        return self._suffix_rx.sub("", name) or name

    def with_suffixes(self, suffixes: Iterable[str]) -> "DeliveryIndex":
        """This delivery grouped by other build *suffixes*: everything else kept.

        Parameters:
            suffixes: The build suffixes an object name drops.
        """
        return self._copy(suffixes=suffixes)

    def subset(self, paths: Iterable[str]) -> "DeliveryIndex":
        """The nodes of *paths* alone, as this delivery reads them: its build suffixes and renamed
        nodes kept (what a row's own names make of the delivery).

        Parameters:
            paths: Node paths of this delivery.
        """
        return self._copy(paths=paths)

    def _copy(self, **changed) -> "DeliveryIndex":
        """This index with *changed* constructor arguments, every other one kept."""
        args = dict(
            paths=self.paths,
            takes=self.takes,
            name=self.name,
            descriptions=self.descriptions,
            kinds=self.kinds,
            steps=self.steps,
            moved=self.moved,
            aliases=self.aliases,
            suffixes=self.suffixes,
            jumps=self.jumps,
            keyed=self.keyed,
            issues=self.issues,
            worn=self.worn,
            states=self.states,
        )
        args.update(changed)
        return type(self)(**args)

    def named(self, node: str) -> List[str]:
        """The paths *node* names exactly, as a sheet in cells names a node: a path as it stands, else a
        path's tail (``SOLENOID_LOC|PCB_LIGHTS_GRP``: each node whose path ends with those names,
        ``ptk.NodeNames.ends``), else a node's own name (a node and the nodes of its name inside it are one,
        the outermost), else what a renamed node's old name (:attr:`aliases`) now names. Names ignore case,
        and where several nodes answer, those spelled as written win (``bolt`` beside ``BOLT``); nothing is
        inferred (a build suffix is a name's own: ``DOOR`` names no ``DOOR_GRP``).

        Parameters:
            node: A name, a path's tail or a path.
        """
        return self._lookup(node, tolerant=False)

    def find(self, node: str) -> List[str]:
        """The object paths *node* names, the tolerant way a sheet of sentences and the converter read a name:
        :meth:`named`'s, else an object name (its build suffixes off, :meth:`name_of`) and every chain it
        heads; names ignoring case.

        Parameters:
            node: A name, an object name, a path's tail or a path.
        """
        return self._lookup(node, tolerant=True)

    def _lookup(self, node: str, tolerant: bool) -> List[str]:
        """:meth:`named` (*tolerant*: :meth:`find`), a renamed node's old name followed to its new one; a
        name met twice on the way (an alias cycle, which :class:`SheetSettings` refuses) names nothing."""
        seen = set()
        while node:
            found = self._exact(node) or (self._object(node) if tolerant else [])
            if found:
                return found
            seen.add(node.casefold())
            node = self._renamed.get(node.casefold(), "")
            if node.casefold() in seen:
                return []
        return []

    def _exact(self, node: str) -> List[str]:
        """The paths *node* names as a path, a path's tail or a node's own name (:meth:`named`, no alias)."""
        if node in self._known:
            return [node]
        names = [n for n in node.split("|") if n]
        if not names:
            return []
        candidates = self._by_leaf.get(names[-1].casefold(), [])
        if len(names) > 1:
            import pythontk as ptk

            wanted = "|".join(names).casefold()
            found = [p for p in candidates if ptk.NodeNames.ends(wanted, p.casefold().split("|"))]
            same = [p for p in found if p.split("|")[-len(names):] == names]
            return same or found
        same = [p for p in candidates if self._leaf(p) == node]
        return self._top_most(same or candidates)

    def _object(self, node: str) -> List[str]:
        """The tops of the chains whose object name *node* is (ignoring case; one spelled as written wins)."""
        if node in self.objects:
            return list(self.objects[node])
        return list(self._by_object.get(node.casefold(), []))

    def spelling(self, node: str) -> str:
        """*node* as the delivery spells it, where :meth:`named` found it by ignoring case alone
        (``bolt`` for ``BOLT``); empty where it is written as the delivery spells it, or names
        nothing. A DCC's tools tell names apart by case, so a sheet read by both writes it as
        the scene does."""
        if node in self._known:
            return ""
        if "|" in node:
            found = self.named(node)
            if len(found) != 1:
                return ""
            spelled = "|".join(found[0].split("|")[-len([n for n in node.split("|") if n]):])
            return "" if spelled == node.strip("|") else spelled
        own = self._spelled.get(node.casefold(), [])
        return "" if not own or node in own else own[0]

    def states_of(self, path: str) -> List[str]:
        """The states of the indicator light the node at *path* shows: those the delivery's ``render_effects``
        record gives the first material its subtree wears that has some (the node's own first, then the
        nodes under it in the file's order); empty where it gives none of them (the record absent, or a
        material with no states: Unity's build gives a track it makes the defaults).

        Parameters:
            path: A node's path in this delivery.
        """
        for node in [path] + [p for p in self.paths if p.startswith(path + "|")]:
            for material in self.worn.get(node, ()):
                found = self.states.get(material.rsplit(":", 1)[-1])
                if found:
                    return list(found)
        return []

    def shortest(self, path: str) -> str:
        """The node at *path* as a sheet names it (what :meth:`named` reads back as it): its own name where no
        other node shares it, else the fewest trailing names of its path that no other node's
        path ends with (``SOLENOID_LOC|PCB_LIGHTS_GRP``), else the path.

        Parameters:
            path: A node's path in this delivery.
        """
        names = path.split("|")
        for n in range(1, len(names)):
            tail = "|".join(names[-n:])
            if self.named(tail) == [path]:
                return tail
        return path

    def arrows_at(
        self, path: str, pattern: Optional[str] = None, step: str = ""
    ) -> List[str]:
        """The authored arrows pointing at the node *path*: each object whose name *pattern* matches, its
        ``part`` group naming *path*'s object (renamed nodes read), by its chain's top; where the pattern
        has a ``step`` group, those for *step* first.

        Parameters:
            path: The node an arrow points at.
            pattern: The arrow names' regex, its ``part`` group the part (none: no arrow is known).
            step: The step's ID (legal-named), for a pattern with a ``step`` group.
        """
        import pythontk as ptk

        if not pattern:
            return []
        rx = re.compile(pattern)
        found: List[str] = []
        mine: List[str] = []
        for obj, tops in self.objects.items():
            match = rx.search(obj)
            part = match.groupdict().get("part") if match else None
            if not part or path not in self.find(part):
                continue
            found += tops
            said = match.groupdict().get("step")
            if (
                said
                and step
                and ptk.StrUtils.to_legal_name(said.upper()).startswith(step)
            ):
                mine += tops
        return mine or found

    @classmethod
    def merged(
        cls,
        indexes: Iterable["DeliveryIndex"],
        aliases: Optional[Mapping[str, str]] = None,
    ) -> "DeliveryIndex":
        """One delivery of several files: every file's nodes, takes (in the files' order), descriptions,
        kinds, steps, motion, take-end jumps, keyed behaviors, the materials each node wears, each material's
        states and issues; named by its files' names.

        Parameters:
            indexes: Each file's index (:meth:`from_fbx`).
            aliases: As :class:`DeliveryIndex`'s.
        """
        indexes = list(indexes)
        joined: Dict[str, Dict] = {
            "descriptions": {},
            "kinds": {},
            "steps": {},
            "moved": {},
            "keyed": {},
            "worn": {},
            "states": {},
        }
        for index in indexes:
            for key, into in joined.items():
                if (
                    key == "keyed"
                ):  # a step two files state keyed pairs for keeps both files' pairs
                    for step, pairs in index.keyed.items():
                        into[step] = tuple(
                            dict.fromkeys(into.get(step, ()) + tuple(pairs))
                        )
                else:
                    into.update(getattr(index, key))
        return cls(
            [p for i in indexes for p in i.paths],
            [t for i in indexes for t in i.takes],
            ", ".join(i.name for i in indexes if i.name),
            aliases=aliases,
            suffixes=indexes[0].suffixes if indexes else None,
            jumps=[j for i in indexes for j in i.jumps],
            issues=[issue for i in indexes for issue in i.issues],
            **joined,
        )
