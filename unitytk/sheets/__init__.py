# !/usr/bin/python
# coding=utf-8
"""The sheet's reader: a build sheet and its delivery, read into the plan Unity builds from.

:class:`SheetPlan` is the contract (the Unity package's manifest reads it);
``SheetPlan.from_sheet(sheet, delivery)`` reads one, through the Shot
Manifest's sheet parser, the template's reading (:class:`ReadingSpec`), the
clause templates (:class:`ClauseBook`), the delivery's nodes and takes
(:class:`DeliveryIndex`) and the phrase matcher (:class:`NodeMatcher`). :class:`SheetCheck` is the strict check of a sheet,
alone or against its delivery, and the take list the artist builds from;
:class:`SheetSettings` is a project's sheet settings file (its sheet, deliveries,
name rules, renamed nodes, retired step IDs, clause templates and each kit's
data); :class:`SheetConverter` rewrites a sheet written before a format in it.
"""

# A declaration, never an import (CODE_STANDARD section 4): each name loads its
# module on first use.
from pythontk.core_utils.module_resolver import lazy_exports as _lazy_exports

_lazy_exports(
    globals(),
    {
        "sheet_plan": ["SheetPlan", "PlanStep", "PlanAction", "PlanSource"],
        "reading_spec": "ReadingSpec",
        "clause_spec": "ClauseSpec",
        "clause_book": ["ClauseBook", "Clause"],
        "delivery_index": "DeliveryIndex",
        "node_matcher": ["NodeMatcher", "NodeMatch"],
        "sheet_check": ["SheetCheck", "CheckItem"],
        "sheet_settings": "SheetSettings",
        "sheet_converter": "SheetConverter",
        "sheet_sync": "SheetSync",
    },
)
