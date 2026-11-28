# unitytk/unitytk/sheets

**Role**: A build sheet and its delivery read into the plan Unity builds from (`SheetPlan`), the strict check (`SheetCheck`), the staging (`SheetSync`) and the converter of older sheets (`SheetConverter`). No Unity and no DCC runs here.

**Nav**: [← unitytk](../../AGENTS.md) · [← root](../../../AGENTS.md) · [the manifest guide](../../docs/manifest.md) · the format and its rules: [build sheets](../../../pythontk/docs/build_sheets.md), [pythontk's manifest rules](../../../pythontk/pythontk/core_utils/engines/shots/manifest/AGENTS.md)

## Rules

- **The format is pythontk's.** A word, its cells and its settings are declared in pythontk's templates and checked by `ptk.ManifestActions`; nothing here declares, renames or re-checks them. A change to the format starts there.
- **The plan speaks the sheet's words.** A row's behavior is its word, its fields its cells by their names (`with`, `label`, `where`), its settings its settings as written; `_cell_reading.py` translates nothing and names no word. No other name reaches a plan: an older name is never read, aliased or migrated.
- **A sheet of sentences reads into the same words.** Each clause template names a word, the settings and cells it gives in the sheet's names, and its roles; its plan is the plan of its conversion to cells (`tests/test_sheet_cells.py` holds it). A sentence the format needs no row for stays a `note` of its words, folded into the row or step it belongs to by a template's own role (`ClauseSpec`: `OWN_ROLES`), never by code that names a word.
- **What the reading says of a row is data.** Where a word's row stands among its step's (`order_by`) and when a setting does something (`applies`) are the reading's; a new kind of key is a design change: stop and ask.
- **Everything the template declares is carried.** `tests/test_sheet_spec.py` holds each setting to the plan, each word to a kit builder and each setting to its field in the word's group of the kit's settings.
- **Names are exact in a cell sheet**: a name is found as written or it is an issue. `NodeMatcher`, the phrase templates (`clauses/`) and the hand-following serve the tolerant reading and the converter alone.
- **Row order carries no meaning**: a travel's `when` (start, end) and a take's `after` place a row; never its position.
- **A sentence becomes cells in one place**: `SheetConverter` (`_cell_writing.py`), writing the plan as it reads, no word translated. It lists by row what it could not decide (each row held to `ptk.ManifestActions.check_row`) and never invents a value the old sheet does not say (a state, a viewpoint).
- **An issue names its cell** by the sheet's own header, with its fix; a warning (a row built anyway) is listed apart.
- **The plan is the contract** (`SheetPlan`, a `SchemaSpec` naming no kit's data): what Unity needs is added there and generated (`m3trik/scripts/sync_unity_contracts.py`), never passed beside it. A kit's own data rides in `extensions`.
- **The reader places nothing**: a row's helper is the node its `where` names (else the one every tool names it by, where the delivery has it); no position is computed here or written into the plan.
- **A project's sheet stays the project's**: a real task lives outside the repo (`UNITYTK_TEST_ASSETS`), its names and paths in no file or test; fixtures are synthetic.

**Proof**: `python unitytk/tests/run_tests.py test_sheet_cells test_sheet_spec test_sheet_converter test_sheet_plan test_sheet_check` after any change here; a change the plan carries differently also runs the kit's Edit Mode tests ([the package's map](../../com.m3trik.unitytk/AGENTS.md)).
