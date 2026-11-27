# unitytk/com.m3trik.unitytk/Kits/Tasks

**Role**: The Tasks kit: the builders that turn a plan's rows into a task's behaviors by composing the features (Interaction, Steps, RenderEffects, Indicators, Placement, Viewpoints, Panels). Editor composition alone; where code goes is [the package's map](../../AGENTS.md).

**Nav**: [← the package](../../AGENTS.md) · [← unitytk](../../../AGENTS.md) · [the kit's guide](../../../docs/tasks.md) · where its rows come from: [the sheet reader's rules](../../../unitytk/sheets/AGENTS.md)

## Rules

- **A behavior is a combination, never a one-off.** A row is a trigger, a requirement, an effect, a look and a place; a builder wires the feature that owns each through its action. A new word is built from the primitives the kit already builds; a builder is added only for a kind of primitive none covers, never for one word or one setting's value.
- **A setting belongs to its primitive**: one name, one meaning and one editor in every behavior, spelled as the sheet's template spells it (`when`, `hold`, `until`, `state`, `speed`, `color`, `size`, `style`, `fade`). A builder reads its row's settings by those names and takes no second name for one.
- **A setting resolves in one order**: the kit's default (`TaskSettings`), the manifest's, the row's Settings, an edit made in the Manifest window (`Manifest.Tune`); each level is shown where its value is.
- **Placing is the Placement feature's, never a behavior's setting.** The search for a first spot (stand-off, view angle, what an arrow merges with) runs once, where nothing is placed yet, and again only when a person resets it. A behavior exposes what a person dials in afterwards; no new search setting goes on a behavior or in `TaskSettings`.
- **The helper in the scene is the truth.** Anything placed (a viewpoint, an arrow, a picture's camera, a page's spot) is a named object a person moves like any other; a row's `where` names one an artist made (an arrow's `where` is where its tip rests, pointing along it; one that draws its own mesh is the arrow). A build reads a helper as it stands and never moves one a person or the delivery placed; it makes one only where none exists. No new offset is kept in a file, and no position in a setting.
- **A cue is no part of what it points at**: an arrow the kit makes is left out of the fades and highlights above it (`RenderEffectsActions.Exempt`), so it shows while its target is hidden and keeps its own look while its target glows.
- **No behavior is known by its name.** What a row is, the kit reads from its traits (`[TaskBehavior]`: `ActsOn`, `Offers`, `EndsStep`, `Teleports` and the rest) and its builder's interfaces (`IStepGate`, `ITaskPageContent`); never `behavior == "x"`.
- **Order is the plan's**: a step's user rows are open together and the step waits for all of them; what happens as a step starts or ends is the row's moment; a row's position decides nothing else.
- **A shown thing has a span** (its step, or until a named step starts), carried by the row that shows it.
- **A row a build cannot do says so**: `Check` returns what is wrong with its fix (a state the delivery lacks, a slot inside what it places); never a silent skip, a guess or a default that hides it.
- **A second build changes nothing, and a person's adjustment survives every build.**
- **A task is its settings file.** What a build names or keys (its top-level objects, a placed spot's key) goes by what stays across versions of the task's inputs: the settings file, a node's path under the delivery. Never a sheet's or a delivery's file name: a renamed sheet or a dated re-export is the same task.
- **The kit assumes no one task**: its defaults fit any project, a project's choices are its manifest's settings, and a client's name, path or content never enters the package.

**Proof**: the sample task built and played through as a user would (`TaskSampleTests`): `python -m pythontk ops run unity_package.compile`, then `python unitytk/tests/run_tests.py --edit-mode -k M3trik.Unitytk.Kits.Tasks`.
