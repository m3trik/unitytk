# !/usr/bin/python
# coding=utf-8
"""NodeMatcher -- which object of a delivery a sheet's phrase names.

A sheet says "the top latch", "2 hex bolts", "the door"; a delivery has
``LATCH_UPPER_GRP``, ``BOLTS``, ``DOOR_RED`` and ``DOOR_GRAY``. Both are read as
sets of words (a name split at ``_`` and case changes, plurals made singular, a
few synonyms made one: top/upper, bottom/lower, electric/electrical; a phrase's
words after "on", "at", "in" ... say where its object is, not what, and are
dropped: "the new lamp on the table"), and the
phrase's object is found by ordered tiers, each a complete pass that either
binds one object, sees a tie, or finds nothing (``FuzzyMatcher.find_with_fallbacks``:
a tie halts, never guessed):

1. **alias** -- the caller's ``{phrase: node}``, for words no rule reads.
2. **context** -- an object the step already names (what it highlights or points
   at) that the phrase is, or is a vaguer word for: "the door" in a step that
   highlights the red door.
3. **exact** -- an object of exactly the phrase's words: "the top latch" is
   ``LATCH_UPPER``.
4. **specific** -- the object whose words are the most of the phrase's, its head
   noun among them: "hex head wrench" is ``WRENCH``, but "door latches" is
   no ``DOORS``. A phrase word that makes it another thing (new,
   empty, invisible...) the object lacks rules it out.
5. **context head** -- the step's one named object holding the phrase's last
   word: "both door latches" in a step that highlights the cabinet latches.
6. **referent** -- "the highlighted area" in a step that names one object (a
   phrase with a referent word, :attr:`~NodeMatcher.REFERENTS`).
7. **general** -- objects the phrase is only a vaguer word for ("door latches"
   of ``DOOR_RELEASE_LEVER``... a lever, not a latch): named, never bound, since a guess puts one
   object's action on another; an alias says which it is.

A word of the phrase the delivery never uses, one edit from exactly one it does
(five letters or more), is read as that word and stated: a sheet's typo
("cabinet lotches") still finds its object, and the plan says so.

The words are data: :attr:`NodeMatcher.SYNONYMS`, :attr:`~NodeMatcher.STOP`, :attr:`~NodeMatcher.DISTINCT`,
the qualifiers, the :attr:`~NodeMatcher.OPPOSITES` and the :attr:`~NodeMatcher.REFERENTS` are the matcher's
own English, and a reading replaces any of them (``vocabulary``: :attr:`ReadingSpec.vocabulary`); a
matcher builds its word sets once, from both.

Prior art: :class:`pythontk.FuzzyMatcher` runs the tiers and keeps a tie a tie;
its own strategies compare spellings (edit distance, substrings), which an
object's description ("the top latch") does not share with its name
(``LATCH_UPPER``), so the tiers here compare words.
"""

import re
from dataclasses import dataclass, field
from typing import Callable, Dict, FrozenSet, List, Mapping, Optional, Sequence, Tuple

import pythontk as ptk

from unitytk.sheets.delivery_index import DeliveryIndex

_CAMEL = re.compile(r"([a-z0-9])([A-Z])")
_WORD = re.compile(r"[a-z0-9]+")


@dataclass
class NodeMatch:
    """What a phrase named.

    Attributes:
        phrase: The phrase as the sheet writes it.
        paths: The object's node path; several where its object name heads
            separate chains (then unbound); empty when nothing matched.
        how: The tier that decided (``alias``, ``context``, ``exact`` ...),
            ``ambiguous`` or ``none``.
        candidates: The tied objects' names, for an ambiguous phrase.
    """

    phrase: str
    paths: List[str] = field(default_factory=list)
    how: str = "none"
    candidates: List[str] = field(default_factory=list)
    #: ``{sheet word: delivery word}`` for each word read as a one-letter typo.
    corrections: Dict[str, str] = field(default_factory=dict)

    @property
    def path(self) -> Optional[str]:
        """The one node path, or None (no match, or a tie)."""
        if len(self.paths) == 1 and self.how not in ("ambiguous", "vague"):
            return self.paths[0]
        return None

    def issues(self) -> List[str]:
        """What a plan states about the match: why it bound nothing, and each
        typo it read past; empty for a clean bind."""
        out = [
            f'warning: read "{w}" as "{v}": correct the typo'
            for w, v in self.corrections.items()
        ]
        if self.path is not None:
            return out
        names = ", ".join(self.candidates or [p.rsplit("|", 1)[-1] for p in self.paths])
        if self.how == "vague":
            out.append(
                f'"{self.phrase}" only partly names {names}: alias it if it is that'
            )
        elif self.how == "ambiguous" or len(self.paths) > 1:
            out.append(f'"{self.phrase}" could be any of {names}')
        else:
            out.append(f'no node is "{self.phrase}"')
        return out


class _NodeMatcherInternal:
    """Internal helpers for :class:`NodeMatcher`."""

    #: Words read as one (the left as the right), in phrases and names alike.
    SYNONYMS: Dict[str, str] = {
        "top": "upper",
        "bottom": "lower",
        "electric": "electrical",
        "btn": "button",
    }
    #: The words where a phrase stops naming its object and starts saying where it is
    #: ("the new lamp | on the table"): the words after are not its name.
    QUALIFIERS: Tuple[str, ...] = (
        "on",
        "at",
        "in",
        "from",
        "near",
        "next to",
        "beside",
        "under",
        "above",
        "behind",
        "inside",
    )
    #: Words a phrase carries that name no part of an object.
    STOP: FrozenSet[str] = frozenset(
        "the a an both all each every its their of on at in to from near by with for "
        "this that these those one two three four".split()
    )
    #: Words by which a phrase means the one object its step shows ("the highlighted area"): words naming
    #: no part, which make the phrase that object (the referent tier).
    REFERENTS: FrozenSet[str] = frozenset({"highlighted", "highlight"})
    #: Words that make a phrase another object than one lacking them.
    DISTINCT: FrozenSet[str] = frozenset(
        "new old invisible ghost empty spare replacement other second".split()
    )
    #: Modifier words, each pair's two sides opposite: a phrase's modifier must not contradict the
    #: name it matched ("the bottom latch" against ``LATCH_UPPER``).
    OPPOSITES: Tuple[Tuple[FrozenSet[str], FrozenSet[str]], ...] = (
        (
            frozenset({"top", "upper", "up"}),
            frozenset({"bottom", "lower", "low", "down"}),
        ),
        (frozenset({"left"}), frozenset({"right"})),
        (frozenset({"front", "fore"}), frozenset({"back", "rear"})),
        (frozenset({"old"}), frozenset({"new"})),
    )

    @staticmethod
    def _singular(word: str) -> str:
        if len(word) > 4 and word.endswith("ies"):
            return word[:-3] + "y"
        if len(word) > 4 and word.endswith(("ches", "shes", "xes", "sses", "zes")):
            return word[:-2]
        if (
            len(word) > 3
            and word.endswith("s")
            and not word.endswith(("ss", "us", "is"))
        ):
            return word[:-1]
        return word

    def words(self, text: str, stop: bool = True) -> FrozenSet[str]:
        """*text* as the set of words a match compares (``LATCH_UPPER`` and "the
        top latch" both read ``{latch, upper}``)."""
        spaced = _CAMEL.sub(r"\1 \2", text.replace("_", " ").replace("|", " "))
        out = set()
        for word in _WORD.findall(spaced.lower()):
            if word.isdigit() and stop:
                continue  # a count, not a name ("2 hex bolts")
            if stop and word in self._stop:
                continue
            word = self._singular(word)
            out.add(self._synonyms.get(word, word))
        return frozenset(out)

    def _head(self, phrase: str) -> str:
        """*phrase* without the words saying where its object is."""
        head = (
            self._qualifier.sub("", phrase.strip()) if self._qualifier else phrase.strip()
        )
        return head or phrase

    def last_word(self, phrase: str) -> str:
        """The phrase's head noun: its name's last word ("door latches" -> latch)."""
        found = _WORD.findall(self._head(phrase).lower())
        if not found:
            return ""
        word = self._singular(found[-1])
        return self._synonyms.get(word, word)

    def contradiction(
        self, said: str, named: str
    ) -> Optional[Tuple[FrozenSet[str], FrozenSet[str]]]:
        """``(the phrase's words, the name's words)`` of the first pair of opposites (:attr:`OPPOSITES`) where
        *said* holds one side and *named* the other (and not the first): its modifier contradicts
        the name; None where none does.

        Parameters:
            said: The phrase ("the bottom latch").
            named: The node's name (``LATCH_UPPER``).
        """
        phrase = {w.lower() for w in re.findall(r"[A-Za-z]+", said)}
        name = {w.lower() for w in re.findall(r"[A-Za-z]+", named.replace("_", " "))}
        for a, b in self._opposites:
            for one, other in ((a, b), (b, a)):
                if phrase & one and name & other and not name & one:
                    return frozenset(phrase & one), frozenset(name & other)
        return None


class NodeMatcher(_NodeMatcherInternal):
    """Find the delivery's object a sheet's phrase names.

    Parameters:
        index: The delivery's nodes.
        aliases: ``{phrase: node}`` for words no tier reads; a node is a name, an
            object name or a path; phrases are matched ignoring case and space.
        vocabulary: Words over the matcher's own, each key replacing its list: ``synonyms``
            ``{word: word}``, ``stop``, ``distinct``, ``qualifiers`` and ``referents`` (lists of words) and
            ``opposites`` (pairs of word lists): a reading's (:attr:`ReadingSpec.vocabulary`).
    """

    def __init__(
        self,
        index: DeliveryIndex,
        aliases: Optional[Mapping[str, str]] = None,
        vocabulary: Optional[Mapping[str, object]] = None,
    ):
        vocabulary = dict(vocabulary or {})

        def words(key: str, own) -> FrozenSet[str]:
            return frozenset(str(w).lower() for w in vocabulary.get(key, own))

        # The words this matcher reads by, built once: each key of *vocabulary* over the class's own.
        self._synonyms: Dict[str, str] = {
            str(k).lower(): str(v).lower()
            for k, v in dict(vocabulary.get("synonyms", self.SYNONYMS)).items()
        }
        self._referents = words("referents", self.REFERENTS)
        self._stop = words("stop", self.STOP) | self._referents
        self._distinct = words("distinct", self.DISTINCT)
        self._opposites: Tuple[Tuple[FrozenSet[str], FrozenSet[str]], ...] = tuple(
            (frozenset(w.lower() for w in a), frozenset(w.lower() for w in b))
            for a, b in vocabulary.get("opposites", self.OPPOSITES)
        )
        qualifiers = sorted(words("qualifiers", self.QUALIFIERS), key=lambda w: (-len(w), w))
        #: Where a phrase starts saying where its object is: a qualifier word to its end.
        self._qualifier: Optional["re.Pattern"] = (
            re.compile(
                r"\s+(?:%s)\s+.*$"
                % "|".join(r"\s+".join(map(re.escape, w.split())) for w in qualifiers),
                re.I,
            )
            if qualifiers
            else None
        )
        self.vocabulary = vocabulary
        self.index = index
        self.aliases = {
            " ".join(k.casefold().split()): v for k, v in (aliases or {}).items()
        }
        self._words: Dict[str, FrozenSet[str]] = {
            obj: self.words(obj, stop=False) for obj in index.objects
        }
        #: Every word the delivery's object names use.
        self._vocabulary: FrozenSet[str] = frozenset().union(*self._words.values())
        #: The objects the last tier to tie tied on (a match reports them).
        self._last_tie: List[str] = []

    def _correct(self, words: FrozenSet[str]) -> Tuple[FrozenSet[str], Dict[str, str]]:
        """*words* with each one the delivery never uses read as the one word it
        does that is a single edit away (five letters or more: "lotches" ->
        "latches"); ``(words, {typo: word})``."""
        fixes: Dict[str, str] = {}
        for word in words:
            if word in self._vocabulary or len(word) < 5:
                continue
            near = [
                v
                for v in self._vocabulary
                if abs(len(v) - len(word)) <= 1
                and ptk.FuzzyMatcher.calculate_levenshtein_distance(word, v) == 1
            ]
            if len(near) == 1:
                fixes[word] = near[0]
        return frozenset(fixes.get(w, w) for w in words), fixes

    def match(self, phrase: str, context: Sequence[str] = ()) -> NodeMatch:
        """The object *phrase* names.

        Parameters:
            phrase: The sheet's words for it.
            context: Node paths the step already names (what it highlights or
                points at), which a vague phrase is read against first.

        Returns:
            A :class:`NodeMatch`; ``path`` is the node where one object matched.
        """
        key = " ".join(phrase.casefold().split())
        for prefix in ("", "the "):
            node = self.aliases.get(
                key[len(prefix) :] if key.startswith(prefix) else key
            )
            if node:
                return NodeMatch(phrase, self.index.find(node), "alias")
        wanted, fixes = self._correct(self.words(self._head(phrase)))
        head = self.last_word(phrase)
        head = fixes.get(head, head)
        context_objects = [self.index.name_of(p) for p in context if p]
        context_objects = [
            o for o in dict.fromkeys(context_objects) if o in self._words
        ]
        strategies = [
            self._tier(
                "context",
                lambda n: n == wanted or (wanted and wanted < n),
                context_objects,
            ),
            self._tier("exact", lambda n: n == wanted),
            self._specific(wanted, head),
            self._tier(
                "context_head", lambda n: bool(head) and head in n, context_objects
            ),
            self._referent(phrase, context_objects),
            self._general(wanted),
        ]
        name, _, status, tier = ptk.FuzzyMatcher.find_with_fallbacks(
            phrase, list(self._words), strategies
        )
        if status == "ambiguous":
            how = "vague" if tier == "general" else "ambiguous"
            return NodeMatch(phrase, [], how, self._last_tie, fixes)
        if status != "unique" or name is None:
            return NodeMatch(phrase, [], "none", corrections=fixes)
        return NodeMatch(
            phrase, list(self.index.objects.get(name, ())), tier, corrections=fixes
        )

    # -- tiers: each a FuzzyMatcher strategy (target, candidates) -> (name, score, status)
    def _decide(self, hits: List[str]) -> Tuple[Optional[str], float, str]:
        self._last_tie = sorted(hits)
        if len(hits) == 1:
            return hits[0], 1.0, "unique"
        if hits:
            return None, 0.0, "ambiguous"
        return None, 0.0, "no_match"

    def _tier(
        self,
        name: str,
        rule: Callable[[FrozenSet[str]], bool],
        among: Optional[Sequence[str]] = None,
    ) -> Callable:
        def strategy(target, candidates):
            pool = candidates if among is None else among
            return self._decide([c for c in pool if rule(self._words[c])])

        strategy.__name__ = name
        return strategy

    def _specific(self, wanted: FrozenSet[str], head: str) -> Callable:
        def specific(target, candidates):
            best, hits = 0, []
            for c in candidates:
                n = self._words[c]
                if (
                    not n
                    or not n < wanted
                    or head not in n
                    or (wanted - n) & self._distinct
                ):
                    continue
                if len(n) > best:
                    best, hits = len(n), [c]
                elif len(n) == best:
                    hits.append(c)
            return self._decide(hits)

        return specific

    def _general(self, wanted: FrozenSet[str]) -> Callable:
        """The objects the phrase is a vaguer word for, never bound (a guess
        puts one object's action on another): a tie that names them."""

        def general(target, candidates):
            hits = [c for c in candidates if wanted and wanted < self._words[c]]
            self._last_tie = sorted(hits)
            return (None, 0.0, "ambiguous") if hits else (None, 0.0, "no_match")

        return general

    def _referent(self, phrase: str, among: Sequence[str]) -> Callable:
        def referent(target, candidates):
            if not self._referents.intersection(_WORD.findall(phrase.lower())):
                return None, 0.0, "no_match"
            return self._decide(list(among))

        return referent
