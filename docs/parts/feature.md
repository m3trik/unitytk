# A feature

**Belongs:** one capability: its actions (public static methods with an optional `OpContext`), its components, its importer, its ported model and its doors. **Does not:** name another feature (use a contract), hold a menu or window beyond an inspector over its own fields (use a tool), call `Undo`, a progress bar or `Debug.Log` from editor code (use `OpContext` and `ImportLog`), tell the render pipeline by its asset's type (ask `RenderPipelineHosts.Current`, the kernel's host), or put a glTF material on a shader itself (write a `LitSurface` through the kernel's `MaterialWriter`).

**Layout** (`Features/<Feature>/`, each folder its own assembly):

| Folder | Assembly | Holds |
|:---|:---|:---|
| `Model/` | `M3trik.Unitytk.<Feature>.Model`, `noEngineReferences` | a port of a pythontk model, held to its `ptk.Conformance` cases under `dotnet test` |
| `Runtime/` | `M3trik.Unitytk.<Feature>` | its components (they ship in players) |
| `Editor/` | `M3trik.Unitytk.<Feature>.Editor` | its importer, its actions (`<Feature>s.cs`; `<Feature>Actions.cs` where that plural is the feature's own name, which its namespace would hide, or no word at all: `RenderEffectsActions`, `AnimationAudioActions`), its generated record (`<Shape>.g.cs`, beside the importer; in `Model/` when the runtime reads the record too, as ArticulatedRig's does) |
| `<Door>/` | `M3trik.Unitytk.<Feature>.<Door>` | the code that touches one outside package ([door](door.md)) |
| `Tests/` | `M3trik.Unitytk.<Feature>.Tests` | Edit Mode tests; `UNITY_INCLUDE_TESTS` and `UNITYTK_TEST_FRAMEWORK` |

References point down: Model < Runtime < Editor; every part may use Core, Kernel and Contracts, never a peer feature (tests included).

**Add one:** `python m3trik/scripts/scaffold_unity_part.py feature <Feature>` writes the folders, assemblies, `.meta` files, an actions class and a failing test; then:

1. **Records.** Read each through the carrier: `Provided.One<IRecordSource>()` (`TryGetRecord`, `Collect`, `Read`), the media through `IModelMedia`, the model importer settings a channel needs (animated custom properties, unresampled curves) through `IModelImportSettings`. Declare it once: `[assembly: ReadsRecord(UnitytkRecords.<Key>, typeof(<Shape>))]`, and name the importer on its line in `Core/UnitytkRecords.cs` (`// channel → <Importer>`), where `sync_scene_records.py` writes the `.g.cs` beside it.
2. **Gate and order.** `Imports.IsDelivery(assetPath)`, the kernel's one gate (an FBX is a delivery); past it the feature acts when its record is present and leaves a file without it alone: no per-feature switch, no file list. A texture's first-import pinning keys on its own name; `GetPostprocessOrder` from the contract it provides or reads (`ImportOrder.Provider` / `.Reader`), or, for an importer that rewrites the materials Unity's own description preprocessors made, `ImportOrder.AfterMaterialDescriptions`; never a bare number.
3. **Actions.** Each change asks `ctx` first; edits go through `OpContextEdits` (`Created`, `Destroy`, `AddComponent`), and a problem through `ctx.Raise(code, message)`, which a null context writes to the Console (never `ctx?.Issue`, which drops it, nor `ctx?.` before one of these extensions, which skips it: `check_csharp.py` refuses both). Its tests run the three kernel checks: one `Undo.PerformUndo()` restores the scene, the dry run's `Changes` equal the live run's, and a second run plans nothing.
4. **Its sample.** The slice ships one: a recipe in the workspace (`m3trik/scripts/unity_samples/<feature>.py`) builds a small synthetic scene through the producer's public API and exports it as a user does; `python m3trik/scripts/sync_unity_samples.py` writes it to `Samples~/<Feature>/` with its `.meta` and `package.json`'s entry (never by hand). Beside it, a C# demo recipe (`Editor/<Feature>DemoRecipe.cs`, the sample's own assemblies) builds the demo scene through the feature's public API. The feature's Edit Mode tests check the imported sample and run that recipe: the owned project imports every sample, so an absent one fails, never skips.
5. **Splitting today's code.** Move each component's file with its `.meta` (`git mv`), so its GUID, serialized fields and name stay; extract the `#if UNITY_EDITOR` half into `Editor/`. A project gets the move with the package's next version (its manifest line); drop the feature's overdue `[Obsolete]` members; move a verifier's `Type.GetType("<Type>, <assembly>")` to the type's new assembly.

**Checked by:** the compiler; `check_layers.py` (references); `check_csharp.py` (kernel bypass, prior art, generated files); its Edit Mode tests (`ProjectBuilder.test`), its sample's among them; `sync_unity_samples.py --check` (where Maya is); the feature's integration suites.
