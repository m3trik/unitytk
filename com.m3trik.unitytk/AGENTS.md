# unitytk/com.m3trik.unitytk

**Role**: The Unity package (this folder, `com.m3trik.unitytk`, MIT, C#): the foundation (Core, Kernel, Contracts) and the features, tools and kits on it. Rules: the [csharp pack](../.claude/rules/csharp.md) and the [principles](../.agents/packs/principles.md).

**Nav**: [← parent](../AGENTS.md) · [← root](../../AGENTS.md) · [the package README](README.md) · architecture: the *unitytk Base Architecture* (Rev G)

Parts, lowest first, an assembly each, references pointing down: Core → Kernel → Contracts → Features → Tools → Kits; the sandbox on top (layout: [its anatomy](README.md#extending-the-package-its-anatomy)). The foundation (Core, Kernel, Contracts) changes only with the maintainer's approval.

**Guides**, half a page per kind of part: [feature](../docs/parts/feature.md) · [contract](../docs/parts/contract.md) · [door](../docs/parts/door.md) · [tool](../docs/parts/tool.md) · [foundation](../docs/parts/foundation.md). A part starts from its scaffold, which writes a failing test: `python m3trik/scripts/scaffold_unity_part.py feature|contract|door|tool <Name>`.

**Where code goes** (first match wins, once per part):

| Ask | Home |
|:---|:---|
| Already solved? | Adopt: our API (`api.search`), Unity, an open standard, a library; record it under `Prior art:` in the part's main type. Stop if it adds a dependency |
| Still exploring? | The owned project's `Assets/Sandbox/<idea>` (`unity_project.build`); it compiles with the package's tests, so a broken prototype fails `ProjectBuilder.test`, as any script in a Unity project does |
| Content for a project? | A kit, or the client's own project; never this repo |
| Needs another feature? | A contract with a stand-in. Stop: propose it |
| Touches URP, XRI, Timeline or another package? | That part's door. Stop if the door is new |
| Data a DCC writes, Unity reads? | A record in pythontk's `SceneRecords`, generated here; pythontk first |
| Something done to a project? | An action: a public static method with an optional `OpContext` |
| An inspector, drawer or handle over its own fields? | The feature's `Editor/`, through `SerializedObject` |
| Runs in a player? | A component in the feature's `Runtime/` |
| A reaction to an import or build? | An importer or build step in the feature, through its kernel seam |
| UI beyond that? | A tool, over actions |
| A helper? | `internal` in its part; on second use, move it down. Stop if it lands in the foundation |
| Nothing fits? | Stop: a home is missing |

**Never → instead (caught by)**: reference a peer feature → a contract (compiler, layers check) · write from a tool, kit or front end → a feature's action · call `Undo.*`, progress bars or `Debug.Log` from editor code → `OpContext`, the import log (kernel-bypass check) · write without asking `ctx` → `ctx.Should*` (the action's undo and plan tests) · declare an operation outside the kernel → a public action (compiler) · reference an outside package outside its door → the door (layers check) · edit a `*.g.cs` → change the source, regenerate (hook, generated-files check) · save a `[SerializeReference]` field → a plain field, or ask · rename or remove a public name → a CHANGELOG line; no moved-names row or forwarder until a project uses unitytk (`check_api_surface.py` reports it) · add a file without its `.meta` → scaffold it · copy code → move it down · silence a check or grow a baseline → fix the cause, or a BACKLOG `decision` · guess an API → `api.search`, `api.describe` · work in your open editor → the kept editor or a project copy (`UnityHost` refuses) · name a client or product → a kit, the client project (`check_public_hygiene.py`) · name a producer (a DCC or its toolkit) in the package → the contract's terms: the record, the carrier, glTF's frame (`check_layers.py`) · add a part without its prior art (prior-art check).

**Stop and ask** when a change touches the foundation, adds a door or an outside package, renames or removes a published name, fits no home, hits a check that looks wrong, or reaches past the part asked for.

**The working loop**: orient (this map, `api.search`, then Unity and the web; record what you weighed) → place (the table) → scaffold (it writes a failing test) → build (pass it; declare needs and provides) → verify (`unity_package.compile`, `dotnet test` in `test/core_dotnet`, then `unity.csharp.exec` and `unity.scene.view` in the kept editor, then the checks and affected tests; review your diff) → hand off (one part per change, its CHANGELOG line, follow-ups to the BACKLOG).

**Commands**: `python -m pythontk ops run unity_package.compile` (every assembly, editor and player, the test assemblies with them once the owned project is imported; no Unity running) · `dotnet test` in `../test/core_dotnet` (the engine-free core) · `python m3trik/scripts/check_layers.py --check unitytk` and `check_csharp.py --check unitytk` (layers, doors, kernel bypass, prior art, generated files) · `ProjectBuilder.test()` (the package's Edit Mode tests in the owned project; `pipeline="builtin"` for the Built-in pipeline's, inconclusive in the URP one).
