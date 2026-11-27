# unitytk/unitytk/sheets

**Role**: A build sheet and its delivery read into the plan Unity builds from (`SheetPlan`), the strict check (`SheetCheck`), the staging (`SheetSync`) and the converter of older sheets (`SheetConverter`). No Unity and no DCC runs here.

**Nav**: [← unitytk](../../AGENTS.md) · [← root](../../../AGENTS.md) · [the manifest guide](../../docs/manifest.md) · the format and its rules: [build sheets](../../../pythontk/docs/build_sheets.md), [pythontk's manifest rules](../../../pythontk/pythontk/core_utils/engines/shots/manifest/AGENTS.md)

## Rules

- **The format is pythontk's.** A word, its cells and its settings are declared in pythontk's templates and checked by `ptk.ManifestActions`; nothing here declares, renames or re-checks them. A change to the format starts there.
- **A cell row is read by data.** `ReadingSpec.words` (`readings/default.json`) says which behavior each word is and where each cell goes; `_cell_reading.py` names no word and no behavior.
- **The words' reading only shrinks.** It translates the words onto the builders' older names, so it is deleted case by case as a builder takes a word as written. A new word or setting is carried through by name (`pass`); a new kind of case or key is a design change: stop and ask.
- **Everything the template declares is carried or listed.** `tests/test_sheet_spec.py` holds each word to a binding and each setting to the plan; a setting no builder takes yet is a named gap there (`NOT_CARRIED`), which only shrinks.
- **Names are exact in a cell sheet**: a name is found as written or it is an issue. `NodeMatcher`, the phrase templates (`clauses/`) and the hand-following serve the tolerant reading and the converter alone.
- **Row order carries no meaning**: a case's `order` (start, end) and a take's `after` place a row; never its position.
- **A sentence becomes cells in one place**: `SheetConverter` (`_cell_writing.py`). It lists by cell what it could not decide and never invents a value the old sheet does not say (a state, a viewpoint).
- **An issue names its cell** by the sheet's own header, with its fix; a warning (a row built anyway) is listed apart.
- **The plan is the contract** (`SheetPlan`, a `SchemaSpec` naming no kit's data): what Unity needs is added there and generated (`m3trik/scripts/sync_unity_contracts.py`), never passed beside it. A kit's own data rides in `extensions`.
- **The reader places nothing**: a row's helper is the node its With names; no position is computed here or written into the plan.
- **A project's sheet stays the project's**: a real task lives outside the repo (`UNITYTK_TEST_ASSETS`), its names and paths in no file or test; fixtures are synthetic.

**Proof**: `python unitytk/tests/run_tests.py test_sheet_cells test_sheet_spec test_sheet_converter test_sheet_plan test_sheet_check` after any change here; a change the plan carries differently also runs the kit's Edit Mode tests ([the package's map](../../com.m3trik.unitytk/AGENTS.md)).
