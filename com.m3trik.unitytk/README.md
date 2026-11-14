# unitytk — Unity import automation for DCC deliveries

C# components that automatically wire up **render effects** (fades, highlights, visibility and animated emission), **animation audio** (sound cues in the clips), **per-shot metadata**, **externally-baked lightmaps**, **ground shadow planes**, **grabbable articulated rigs**, and **materials** (stated as literal glTF 2.0, written onto the render pipeline's own Lit) when you import a delivery into Unity: an FBX, its media embedded in it, and the records on its `data_export` carrier. unitytk reads the delivery alone, whichever tool wrote it. Zero configuration required.

Ships as one UPM package (`com.m3trik.unitytk`), installed by one line in a project's `Packages/manifest.json` (this version's git URL, `https://github.com/m3trik/unitytk.git?path=/com.m3trik.unitytk#v<version>`, or a checkout's folder, `file:`; `UnityHost.install` writes it) — visible in Unity's **Package Manager**, with all actions under **Tools ▸ unitytk**. Uninstall = remove that line (or remove the package in the Package Manager).

---

## How the metadata arrives (the import contract)

Every producer stamps its data as string attributes on one shared, hidden
carrier node named **`data_export`**; those attributes ride into the FBX as **user
properties** and land on the `data_export` GameObject of the imported prefab. Each
controller here is an `AssetPostprocessor` that reads its channel via
`OnPostprocessGameObjectWithUserProperties` (attr names matched case-insensitively) -- but for the
`materials` record, which Unity hands over only after every material description it shades: it is read
off the binary FBX itself in `OnPreprocessModel` (`CarrierFile`, by pythontk's rules for the same file). An assembly export carries one `NS:data_export` per referenced module (or a grouped module's own `data_export` under its group) beside the scene's own: the scene's own carrier -- the root-level `data_export` -- is read, and a module's or a numbered copy's (`data_export1`, `data_export.001`) only where the scene's carries no such channel -- the GLB pipeline's rule, so both deliverables read one record. Any other node carrying a property of a channel's name (an artist's custom attribute) is no carrier, on every channel:

| Channel (user property) | Format | Consumer |
|---|---|---|
| `animation` | JSON `{version: 2, shots:[{clip, description, metadata}]}` — per shot what a clip cannot hold: `clip` is the name of the file's take for that shot (one take per shot, spanning its range), `description` and `metadata` (any JSON values by key) are optional; a v1 record is refused (re-export) | `ShotList` |
| `lightmaps` | JSON `{version: 3, unit_scale, objects:[{name, map, scaleOffset, hierarchy}], probe:{map, position, box}}` -- each baked object's map, read through its second UV set, and its rect in it; `unit_scale` metres per unit of every length; `probe` optional, `box` `{min, max}` or null for an open scene. The probe's `highlights` and the record's `lights` are optional extensions only some readers use, not read here (see [Lightmaps](#lightmaps)); a v2 record is refused (re-export) | `LightmapsImporter`, `LightmapSet` |
| `audio` | JSON `{version: 3, cues:[{clip, sound, time, offset, stop}], media:{sound: sha256}}` — per clip (`clip`: the name of the file's take for it, one per shot; `""` in a file with no shots, whose one take is the clip) each sound it plays: `sound` the delivered audio file's name, extension included; `time` and `stop` in seconds from the clip's start; `offset` seconds into the sound (a sound a cut divides plays on from where it had reached); `media` (optional: written by the exporters' dependency pass, so none in a raw export) the SHA-256 of each audio file the FBX embeds, by its sound; v1 and v2 are refused (re-export) | `AnimationAudioImporter` (PlayAudio / StopAudio events in the clips), `AnimationAudioPlayer` |
| `materials` | JSON: literal glTF 2.0 -- `{version: 2, asset, extensionsUsed, materials:[{name, pbrMetallicRoughness:{baseColorFactor, baseColorTexture, metallicFactor, roughnessFactor, metallicRoughnessTexture}, normalTexture, occlusionTexture, emissiveTexture, emissiveFactor, alphaMode, alphaCutoff, doubleSided, extensions:{KHR_materials_unlit, KHR_materials_emissive_strength}}], textures, images:[{uri}], samplers}` -- a glTF document holding only its materials, each `name` the FBX material it describes, each image by the file name the FBX embeds it under, metal and roughness packed in glTF's layout, a map's UV transform as `KHR_texture_transform`; a v1 record (the flat shape) is refused (re-export) | `MaterialsImporter` (on import, see [Materials](#materials)) |
| `render_effects` | JSON `{version: 2, tracks:[{node, channels, highlight_color, highlight_color_dim}], materials:[{material, channels, emission_color, emission_strength}]}` — per node the channels its subtree carries (`visibility`, `opacity`, `highlight`, the colours linear and optional), per material an animated `emission` (its curve a weight from 0 to 1 on `emission_color` x `emission_strength`, glTF's `emissiveFactor` and `KHR_materials_emissive_strength`); no curve: each rides the file's curve transport, a child transform `<target>__<channel>` (marked `curveProxy`, its `scale.x` the curve) of its node, or of the carrier for a material, in each take; v1 is refused (re-export) | `RenderEffectsImporter` (see [Render effects](#render-effects)): `Fade`, `Highlight`, `EmissionTrack` |
| `shadows` | JSON `{version: 3, unit_scale, planes:[{name, source, contact, intensity, source_type, source_size, source_angle, follow_source, ground, radius, height, max_stretch, canvas, falloff_power, fade_height, source_ramp, horizon:{texture, size, spans, bounds, height_scale, frame_a, frame_b, rect}}]}` -- each plane's nodes by name, its horizon map (required), the canvas it is placed on and the fade's terms; `unit_scale` metres per unit of every length. A plane's `silhouette` is an optional extension the GLB's viewer draws, not read here (Unity plays the horizon map); a v2 record is refused (re-export) | `ShadowPlaneImporter`, `ShadowPlane` |
| `articulation` | JSON `{version: 2, rigs:[{name, joints:[{name, parent, t, q, anchor:{t, q}, rotate_order, channels:[{channel, min, max, weight}]}], grab:[{node, joint}]}]}` — in glTF's frame: metres, quaternions `(x, y, z, w)`, +Y up; `anchor` the point and axes each joint turns about and slides along, in its rest frame (the identity on its pivot); `min`/`max` null = unbounded; a v1 record is refused (re-export) | `ArticulatedRig`, `ArticulatedJoint` |
| `lods` | JSON `{version: 1, groups:[{name, levels, heights}]}` -- per LOD group (a node `<base>` over `<base>_LOD0..n`, LOD0 the most detailed) its node, its level nodes LOD0 first, and one screen-relative switch height per level, the cull last (Unity's `screenRelativeTransitionHeight`) | `LodsImporter` |

Each importer reads its record under the key above -- declared once, in `UnitytkRecords` (`Core/UnitytkRecords.cs`) -- through the record's typed reader, generated from pythontk's declaration beside the importer (`<Shape>.g.cs`: its `Shape` types and `CurrentVersion`), at that version alone and the one way every reader reads a record (`UnitytkRecords.Read`): unitytk reads the current standard only. A record of any other version, or one that fails its declared shape -- a key it requires left out, a null where it admits none, a value of another type or outside its choices -- is refused whole, with a warning that says to re-export the file from a current exporter (or, for a newer version, to update the package), and a file that carries a record under a key from before 2026-10-20 (`shot_metadata`, `audio_manifest`, `lightmap_metadata`, `shadow_metadata`) is re-exported too: no reader looks for those.

The records are declared once, in pythontk's `SceneRecords`, and published as JSON
Schema with a version per record: the readers here are generated from it, and any
producer that writes a delivery to it delivers to unitytk. Every value is in glTF's
frame (metres, +Y up, quaternions), and whatever a reader would otherwise convert,
guess or match arrives fixed at the source (literal glTF materials, lightmaps on the
second UV set, one take per shot, times in seconds). What only one reader uses is a
declared, optional extension the others ignore.

> **Important:** the records ship only when the export includes the carrier. An FBX
> written without `data_export` carries no records, and a reimport of such an FBX
> removes the components an earlier import added, as stale.

---

## Setup (One Time)

1. Install the package with Unity's Package Manager: **Window ▸ Package Manager ▸
   + ▸ Install package from git URL**, then

   ```
   https://github.com/m3trik/unitytk.git?path=/com.m3trik.unitytk#v0.7.0
   ```

   That adds one line to the project's `Packages/manifest.json`, naming a version;
   Unity downloads that version into the project's read-only `Library/PackageCache/`,
   and `packages-lock.json` pins it. Updating is changing the tag, when the project's
   owner chooses. From a checkout, a `file:` line names the folder instead
   (`"com.m3trik.unitytk": "file:C:/path/to/unitytk/com.m3trik.unitytk"`), which
   `python -m pythontk ops apply unity_project.install project=<project>` writes.

   Every file and folder ships with its `.meta`, so each script carries the same
   **fixed GUID** in every project: a prefab or scene carried from one project to
   another keeps its scripts.

2. Audio needs no placing: a delivery embeds each sound's file in the FBX under its name, with its SHA-256 in the record, and the importer extracts it beside the FBX (reimporting that FBX once). A same-named library clip already in the project (MP3/WAV: beside the FBX, an `Audio/` subfolder, `Assets/Audio/`) is used instead only when it is the same file -- its bytes hash alike; another model's extraction is never taken, nor a `door.wav` that only shares the name. An FBX that embeds no audio still binds a same-named library clip by name.

That's it — drop FBX files anywhere under `Assets/` as usual. There is nothing
to configure: every FBX is a delivery, and each feature acts when its record is
present on the file and leaves a file without it alone (the kernel's one gate,
`Imports.IsDelivery`).

### Safety: unrelated assets are never modified

The importers **never touch a model's import settings up front**. Channel
detection works with Unity's default settings, so an FBX that carries no
unitytk metadata comes through the pipeline byte-identical to a project
without unitytk installed. Only when an asset carries a `render_effects`
record is the one setting its curves need — *Resample Curves* off, so a stepped
key stays a step — enabled **on that one asset**, with a single automatic
reimport (logged). Nothing is ever switched off, and no other model's settings
are read or written. Render effects never edit a material: a renderer wears its
material's twin while an effect over it is active, and its own material again
at rest.
The materials are a carrier channel too, read off the FBX itself (it arrives
before any other, ahead of the material descriptions): a model whose
`data_export` carries a `materials` record has each material it names written
from its glTF material (see [Materials](#materials)), and the images those
materials name are extracted beside it (one reimport). A model without one, and
a material the record does not name, keep Unity's own import, untouched.

### Where embedded media land

An FBX's embedded files -- its maps, its
reflection probe and the probe's highlight copies, its shadow maps, its
audio -- are extracted beside it, or, when its folder holds another FBX, into
a folder of its own beside it, `<model>_Media/` (`CarrierImport.MediaFolder`).
Records name their files by file name, and two deliverables can name
different files alike -- a standard and a moody bake of one room both embed
`Room_Lightmap.exr` -- so in one folder the second bound the first's.
Every lookup reads the model's own folder first; a model that comes to share
its folder moves its media there on its next import, and the copies it left
beside it can be deleted. Audio is matched by content: the record carries
each embedded clip's SHA-256, and a same-named clip is reused only when its
bytes hash alike and nothing will rewrite or take it away -- a library's,
never another model's extraction (that model's re-export overwrites it in
place, or its `<model>_Media/` goes), nor an unmarked file lying beside
another FBX (an older release's extraction, which may be deleted) -- else the
model's own is extracted. With no digest, the model's own copy binds first,
then a same-named clip in its own folder, then one elsewhere by name.

An extraction never writes over a file the model did not extract. Unity
extracts every file an FBX embeds -- a material's own maps too, which no
record names -- in place, keeping the GUID of a file already there, so a
library's `wood.png` or another model's `door.wav` beside a lone model would
take its bytes for everything bound to it. So a lone model's media land first
in `<model>_Media/`, are imported there, and the exact names that landed
decide: when one names a file beside the model that it did not extract, they
stay in `<model>_Media/` (as do its later extractions); else each moves beside
it -- a new file marked its own, one it extracted before given the new bytes
in place, GUID and settings kept. A file a model extracted beside itself is
marked in its importer's `userData` (`unitytk.extracted-by:<model GUID>`, in
the `.meta`, so every checkout agrees). A file extracted before 2026-10-31
carries no mark, so it is not the model's own: the model's next extraction
goes into `<model>_Media/`, and the old copy beside it can be deleted -- no
other model binds an unmarked file lying beside an FBX.

One texture convention gets sensible **defaults on first import only** — the
shadow horizon map (`*_horizon`: linear, alpha kept as data, no
mips, clamp, point, NPOT size kept, uncompressed, RGBA32 on Standalone and
Android — the shader bit-tests its texels). These apply when the file has no
`.meta` yet; afterwards the settings
are yours, and a reimport will never revert an Inspector change you made.
Textures are matched purely by that filename suffix, so rename a texture if you
want it left alone entirely.

---

## Importing an FBX

1. Drop your FBX file into any folder under `Assets/`.
2. Nothing else to place: the FBX carries its maps, lightmaps and audio inside it, and the importers extract them beside it (see Setup step 2 and [where embedded media land](#where-embedded-media-land)).
3. Unity reimports automatically. When it finishes:
   - The nodes the **render_effects** record names get a `Fade` or a `Highlight`, and each animated material an `EmissionTrack` — they fade, glow and switch on and off during playback (see [Render effects](#render-effects)).
   - The scene-wide **data_export** child carries the **audio** record: each cue is written into its clip as a `PlayAudio` and a `StopAudio` AnimationEvent (the clip's own events, never the Events tab's), and an `AnimationAudioPlayer` is attached to the **prefab root**, where Unity sends the clips' events once an Animator on the root plays them.

### How to verify it worked

1. Drag the FBX from the Project window into your Scene.
2. Select the prefab root and its children — check the Inspector for:
   - **Fade** / **Highlight** components (on the nodes that fade or glow)
   - **Animation Audio Player** component (one per imported FBX whose clips cue sounds, on the prefab root — same GameObject as the Animator); select a clip sub-asset to see its `PlayAudio` / `StopAudio` events
3. Press **Play** — opacity fades and the clips' sounds should play automatically.

---

## Samples

Each feature ships a sample: a small delivery to try it on, and the recipe that
builds its demo scene. Import one from **Window ▸ Package Manager ▸ unitytk ▸
Samples ▸ Import**: Unity copies it into
`Assets/Samples/unitytk/<version>/<Sample>/`, where its FBX imports like any
delivery.

| Sample | Holds | Try it |
|:---|:---|:---|
| AnimationAudio | `AnimationAudio.fbx`: a box moving through two shots, Approach and Exit, and a chime keyed across the cut, embedded in the FBX. Its import writes the chime into each clip as `PlayAudio` / `StopAudio` events (Approach stops it at the cut, Exit plays it on from where it had reached) and gives the model an Animation Audio Player | **Tools ▸ unitytk ▸ Samples ▸ Animation Audio Demo** builds `AnimationAudioDemo.unity` and its Animator Controller beside the model and opens it; press Play: the two clips play in turn, the chime across the cut |
| ArticulatedRig | `ArticulatedRig.fbx`: a desk lamp delivered with its articulation record: a post swivelling and tilting on its base, an upper arm hinged on the post, an extension telescoping out of the arm and a shade hinged at its tip, each joint's limits taken from a pose, and one two-second take, Reach, written at its first frame. Its import gives `lamp_RIG` an `ArticulatedRig` and each joint an `ArticulatedJoint`, by reference, in Unity's axes, and every moving part a convex collider to grab by | **Tools ▸ unitytk ▸ Samples ▸ Articulated Rig Demo** builds `ArticulatedRigDemo.unity` and its Animator Controller beside the model and opens it, the camera carrying an `ArticulatedRigPointerGrab`; press Play: the take plays, and a drag on any part poses the chain, the released pose holding over the take |
| Lightmaps | `Lightmaps.fbx`: a floor and three boxes lit by a bake delivered with the file (synthetic EXRs, linear): the floor by its own map, the boxes by one atlas, each its own quarter of it (`scaleOffset`), read through the second UV set; and the bake's reflection probe, captured inside the room's box, its panorama holding the bake's lamp. Its import gives the model's root a Lightmap Set of the four renderers and the probe as a box-projected `ReflectionProbe` | **Tools ▸ unitytk ▸ Samples ▸ Lightmaps Demo** builds `LightmapsDemo.unity` beside the model and opens it: the default sun taken out, the set its scene's owner (Apply), the boxes lit by the set alone and the metal one reflecting the probe; **Generate Lighting** there makes the set stand down |
| Materials | `Materials.fbx`: four spheres, one material each, its images embedded: `Metal_MAT` (colour, metal / roughness packed by the export in glTF's layout, normal), `Glow_MAT` (emission four times its colour), `Flat_MAT` (unlit) and `Tiled_MAT` (a colour map tiled 3 x 2 and offset). Its import writes each onto the pipeline's own Lit, the metal / roughness repacked into Unity's mask | **Tools ▸ unitytk ▸ Samples ▸ Materials Demo** builds `MaterialsDemo.unity` beside the model: the four spheres under the scene's light, and a fifth whose gold material `MaterialsActions.Apply` writes from a glTF material |
| RenderEffects | `RenderEffects.fbx`: two shots, FadeOut and FadeIn, one take each: a box fading out and back in (a `Fade`), a sphere whose mapped emission takes a pulsing blue highlight (a `Highlight`, the map set aside while it glows), and a two-material panel that blinks off for a moment (its active state) and whose lamp face, wearing a material of its own (`Lamp_MAT_Signal`), switches on (that material's animated emission: an `EmissionTrack`). Its import adds the components, binds each curve onto its field and makes the twins into the model | **Tools ▸ unitytk ▸ Samples ▸ Render Effects Demo** builds `RenderEffectsDemo.unity` and its Animator Controller beside the model and opens it; press Play: the two clips play in turn |
| ShadowPlane | `ShadowPlane.fbx`: a box, a chair on four legs and an awning cantilevered past its post on a ground, each with one shadow plane its record describes: the box's and the awning's follow the point light `Lamp`, keyed to sweep low across the scene and over it for three seconds, and the chair's follows the directional `Sun`; every plane's horizon map is embedded, and the fade is keyed nowhere. Its import gives each plane a `ShadowPlane` bound by reference to its target, its source Light and its contact, its delivered map bound; the Edit Mode tests also rebake each map in Unity (Bake Horizon) and hold it to the delivered one | **Tools ▸ unitytk ▸ Samples ▸ Shadow Plane Demo** builds `ShadowPlaneDemo.unity` and its Animator Controller beside the model and opens it: the model's lamp and sun light it (Unity's shadow maps off: the planes are the shadows); press Play: the lamp's two shadows swing, lengthen and fade as it sweeps over, the chair's holds where the sun casts it |
| Shots | `Shots.fbx`: a box moving through three shots, one take each. Its import makes one clip per shot, named after it, and a `ShotList` giving each clip its description and its `section` | **Tools ▸ unitytk ▸ Samples ▸ Shots Demo** builds `ShotsDemo.unity` beside the model and opens it; press Play: the shots play in order, the playing shot's description on screen |

A sample is one set of content with three uses: what you import to try a
feature, what its demo scene is built from, and what the package's Edit Mode
tests check (the owned test project imports every sample). Its delivery is
generated, never edited by hand: the workspace's
`m3trik/scripts/sync_unity_samples.py` exports it from its recipe and writes
`package.json`'s `samples`; its `--check` holds the committed one to a fresh
export.

---

## Retroactive Fix (Already-Imported FBX)

If the FBX was imported *before* these scripts existed in the project:

1. Select the FBX in the Project window and right-click → **Reimport**: every
   channel's importer runs again.

An instance of a model plays its sounds through the player its import put on
the model's root; an object of your own whose clips cue sounds gets one with
`AnimationAudioActions.AddPlayer` (or Add Component ▸ Audio ▸ Animation Audio Player).

---

## What These Files Do

| File | Runtime | On Import |
|---|---|---|
| `Features/Carrier/` (`M3trik.Unitytk.Carrier.Editor`, editor only; in every install, beside the core) | -- | The record carrier: `CarrierImport` (a node's channels by the carrier precedence, a model's media folder and embedded files, the import settings a channel asks for; `CarrierImport.Read`: a record read the one way, its refusal warned), `CarrierFile` (a record read off the binary FBX itself, for an importer that needs it before the user properties arrive), `UnitytkImportConfigurator` (applies the asked import settings after the batch, one reimport per model), all global names, and `CarrierRecords`, the provider every importer reads them through (`IRecordSource`, `IModelMedia`, `IModelImportSettings`). The core's `Contracts/` holds what two features read: `MaterialRecord.cs` (+ the generated `MaterialRecord.g.cs`) reads the `materials` record, which the Materials importer writes its materials from and the Render Effects importer joins its material entries by; how a material stores a colour (`MaterialColor.Encode`) is the kernel's, beside its one material writer. |
| `Core/` (`M3trik.Unitytk.Core`: no engine reference; beside `Kernel/`, not in it) | `Json` (the one JSON reader and writer; a record's reader parses every number as a double), pythontk's operation payloads (`OpRequest`, `Outcome`, `OperationEntry`, ...) generated by `m3trik/scripts/sync_operation_contract.py`, and `RunRules`: how a call is answered (statuses, held work, which scenes are saved), behind `ISceneFiles` and `IOperationTarget`, so `dotnet test` holds it without an editor. | -- |
| `Kernel/Editor/` (`M3trik.Unitytk.Kernel`, editor only) | -- | The editor kernel: `[Operation]` static methods (`unity.csharp.exec`, `unity.scene.view`, `unity.project.doctor`) found by `OperationRegistry`, each call a pythontk run folder `RunFolder` answers through `RunRules` with this editor's scenes (`UnitySceneFiles`) (per call: `-executeMethod M3trik.Unitytk.Kernel.BatchServer.Run -unitytkRun <run>`), or a kept editor serving a pythontk `JobQueue` folder (`-unitytkServe <folder> [-unitytkOwner <pid>]`, `SessionServer`: re-armed after each domain reload, ended by the `stop` file, a lapsed lease or its owner's exit). `Catalog` writes `editor_operations.json`. |
| `RenderEffects/Runtime/` (`Fade`, `Highlight`, `EmissionTrack` on `RenderEffect`; `MaterialTwins`, the twins' table; the importer, the generated `RenderEffectsRecord.g.cs`, the actions `RenderEffectsActions` and the inspector in `Editor/`; the menu in `Tools/RenderEffects/`) | A `Fade` (alpha, nesting as a `CanvasGroup`'s) or `Highlight` (a glow between two colours, with a procedural pulse) on a subtree root, and an `EmissionTrack` on a material: the outermost one writes every renderer under it, putting each on its material's twin (one per material and effect state, shared by every renderer it covers) while the effect is active and back on the shared original at rest; hidden at alpha 0. No property block, no shader of its own. | Reads the `render_effects` record, adds the components, rebinds each curve proxy per clip onto the component's own field (a visibility onto the node's active state), deletes the proxies and makes the twins into the model. **Tools → unitytk → Render Effects → Prepare Materials** makes the twins of effects a person adds. See [Render effects](#render-effects). |
| `AnimationAudio/Runtime/AnimationAudioPlayer.cs` (`M3trik.Unitytk.AnimationAudio`; the importer, the generated `AudioRecord.g.cs` and the actions `AnimationAudioActions` in `Editor/`) | `AnimationAudioPlayer` plays the sounds a clip cues: `PlayAudio(AnimationEvent)` starts the event's object (an `AudioClip` from its float parameter's seconds in; any other `AudioResource`, such as an Audio Random Container, from its start), each sound on a source of its own set as the player's own `AudioSource`; `StopAudio(AnimationEvent)` stops it where the clip firing it started it. A sound also stops when the clip that started it stops playing (an interrupted clip fires no more events): an Animator's states play it on no layer, or a legacy Animation's state is disabled; a sound an Animator with no controller started (a Playables graph, Timeline) plays to its stop event or its end. Delivered events and those a Unity user authors (the Events tab, the Animation window) play alike. `AnimationAudioActions.AddCue(clip, sound, time, stop, offset)` cues a sound in a clip with no DCC behind it (a model's clip: its Events tab; a clip of the project's own: the clip) and `AddPlayer(target)` gives an object a player, each with a dry run and one undo step. | Reads the `audio` record (v3) off `data_export` through its generated shape (`AudioRecord`) and writes each cue into the clip cut from its take (`ModelImporterClipAnimation.takeName`, ignoring case: a renamed clip still plays its cues, and a split take gives each part the cues inside it, re-timed from its start) as `PlayAudio` at its time (object: the sound's AudioClip, float: the offset) and `StopAudio` at its stop, into the imported clip itself (`OnPostprocessAnimation`), never the importer's clip list: the Events tab stays the project's, and its events stay beside the delivered. Resolves each sound by its file name and content (the FBX's embedded copy, extracted beside it once, or a same-named library clip whose bytes hash to the record's `media` SHA-256, never another model's extraction), and adds the player to the prefab root of a model whose clips cue sounds. |
| `Shots/Runtime/ShotList.cs` (`M3trik.Unitytk.Shots`; the importer, the generated `AnimationRecord.g.cs` and the action `ShotsActions.AddEntry` in `Editor/`) | `ShotList`, a model's shots: `entries`, each a clip reference, its description and its metadata (`ShotField`: a key and its value as JSON text, `Shot.TryGetMetadata`); `Entry(clip)` finds a clip's shot (null when none). No frames, windows or player: an Animator, an Animation or a Timeline plays the clips. `ShotsActions.AddEntry(target, clip)` (dry run, one undo step) gives a clip a shot in a project with no DCC behind it, on an object of its own (an imported model and its instances are refused: their entries are the source's). | Reads the `animation` record off `data_export` through its generated shape (`AnimationRecord`) and rewrites the model's `ShotList` at every import: each shot joins **every clip cut from its take** (`ModelImporterClipAnimation.takeName`, ignoring case), so a clip renamed or split in the Animation tab still joins; a shot whose take the file lacks, or that no clip is cut from, is warned. |
| `Lightmaps/Runtime/LightmapSet.cs` (`M3trik.Unitytk.Lightmaps`; the importer, its texture roles `LightmapsTextureRoles`, the actions `LightmapsActions` and the generated `LightmapRecord.g.cs` in `Editor/`; the menu in `Tools/Lightmaps/`) | `LightmapSet`, a model's lightmaps as references: the maps, each renderer with its map and rect, the mode and the reflection probe, bound through Unity's own lightmap fields whenever it is enabled and given back when it is disabled; it stands down in a scene Unity's bake owns. See [Lightmaps](#lightmaps). | Reads the `lightmaps` record (v3) off `data_export` through its generated shape (`LightmapRecord`) and gives the model's root its set: each object's renderer by name and hierarchy, its map (extracted from the FBX, imported as a Lightmap on its first import) and its rect, and the bake's probe, a custom `ReflectionProbe` reading a cubemap the import makes of the record's panorama. Warns if `generateSecondaryUV` would overwrite the DCC's lightmap UVs. |
| `ShadowPlane/Runtime/ShadowPlane.cs` (`M3trik.Unitytk.ShadowPlane`; + `ShadowPlaneCache.cs`, `ShadowPlane.shader`, `ShadowPlaneHorizon.hlsl`; the ported model `ShadowPlaneModel`, the horizon baker `ShadowHorizonBaker` and `HeightFieldMap` in `Model/`; the importer, the actions `ShadowPlaneActions`, the inspector and the generated `ShadowRecord.g.cs` in `Editor/`; the menu in `Tools/ShadowPlane/`) | `ShadowPlane`, a ground shadow in Unity's terms: its `target` (the occluder whose meshes its map is baked from), `source` (a `Light`; the scene's sun, `RenderSettings.sun`, when none is set: a Directional light shines along its +Z, a Point or Spot light from its position) and `contact` by reference, every length in metres in its `space` (moving, turning or scaling that transform carries the shadow, its ground included), and its horizon map and layout. In `LateUpdate` it places the plane on its `canvas` (where the quad's edges sit around the projected shadow; Unity's own planes the default one) from the projection model (pythontk `ShadowProjection.model`, ported in `ShadowPlaneModel`, double precision) while `followSource` is on and the contact and a source resolve, and **computes the fade** each frame (`ShadowProjection.fade`, ported in the same class: the stretch's falloff, the source's height over the contact, in by `sourceRamp` metres of it (1 on Unity's own planes), the target's rise off the ground; `Fade`, never above `intensity`), which the shader multiplies the march by; a plane missing its contact or source draws at its intensity. `ApplyNow()` for scripts. The march is cached (`cacheShadow`, on by default): marched into one shared atlas only when the source, the placement or the ground moves, and sampled at one fetch a pixel. A layout the shader cannot march, or no map, draws nothing, and so does a plane nothing has placed yet (the material's fade is 0 until the component writes its own). A contact from a Z-up producer is read on its +Z as up (`zUpFrames`, which the import sets from the contact's own frame); how the quad lies (XZ, or XY as a Z-up producer's and Unity's own Quad) is read off its mesh. The inspector shows the plane placed in the Scene view while it is open (given back as it closes, before a scene or a prefab is saved, before a reload and as play starts), with handles for its radius and ground; a plane that does not follow its source is only faded there, and a move made to it stays. Actions (`ShadowPlaneActions`, each with an optional `OpContext`, reporting progress): `Create` (a plane, its contact and its baked map under a target), `BakeHorizon` (the map baked from the target's meshes in the plane's frame by the ported baker, a PNG beside the scene imported as data, the target's geometry signature on the plane), `RebakeStale` (every plane whose target changed). The meshes are those that draw: each `MeshRenderer`'s, each `SkinnedMeshRenderer`'s in its current pose, LOD 0's of a `LODGroup`; a mesh on an inactive object, with no renderer (a collider's proxy) or a disabled one, or in a later LOD casts nothing and is named. The signature is the same wherever the instance stands, so moving, turning or scaling a placed model never reads as a change: an exact digest of the meshes' triangles (and a mesh asset's own points), and the ground and each mesh's place in the plane's frame, compared within 1e-4 of the target's extent. A plane whose map another plane holds too (a duplicate's) is baked to a new file, never over the shared one. The menu (Tools ▸ unitytk ▸ Shadow Plane) asks before it writes over a map on disk, and names the planes that get a new map. | Reads the `shadows` record off `data_export` through its generated shape (`ShadowRecord`) and fills each plane's `ShadowPlane` **by reference**: the plane, its source and its contact found by name as the GLB's reader finds them (exact, else the namespace-stripped leaf; a tie is left unbound and said in the import log), a source node holding a Light taken as that Light and a bare source node given a switched-off stand-in `Light` under it (`ShadowSource`, turned to shine along the node's -Z, as a bare node shines), the target the node the contact hangs on, the lengths in metres (times `unit_scale`, the source ramp too; one that is not over 0 fades over one unit, said in the import log), the angle in degrees, the frame vectors in Unity's axes and the canvas in the component's terms (a Y-up producer's sides swapped by the import's X mirror, a Z-up producer's kept); binds the delivered horizon map from the model's media (a file the FBX embeds, extracted beside it; a source dependency of the import), puts every plane on **one shared, instanced material** on `m3trik/ShadowPlane` where the render pipeline's host draws it (Built-in and URP; a missing shader falls back to the host's own unlit-transparent shader per plane, at the record's intensity, and a pipeline no host serves (HDRP) keeps Unity's material, said in the import log), turns off shadow casting/receiving + light/reflection probes, pins `*_horizon` (data) PNG imports, and removes stale planes when a re-exported FBX no longer carries the channel. The runtime never reads the record. |
| `ArticulatedRig/Runtime/ArticulatedRig.cs` (`M3trik.Unitytk.ArticulatedRig`; + `ArticulatedJoint.cs`, `ArticulatedRigPointerGrab.cs`; the ported model `ArticulationModel` in `Model/` with the generated `ArticulationRecord.g.cs`; the importer, the actions `ArticulatedRigActions`, the inspectors and their edit-mode preview in `Editor/`; the doors `XRI/` (with its editor half) and `InputSystem/`; the menu in `Tools/ArticulatedRig/`) | An `ArticulatedJoint` on each part (its parent joint, rest, anchor, channels with their limits and weights, and the values, keyable) and an `ArticulatedRig` on the rig's root listing them and its grab parts: every part posed each frame from the joint chain, a flat model's too, and an input-agnostic grab solved by `ArticulationModel` (pythontk's, ported and held to its golden cases). See [Articulated rigs](#articulated-rigs). | Reads the `articulation` record off `data_export` through its generated shape (`ArticulationRecord`) and fills the components by reference: joints and grab parts found by name (exact, else the namespace-stripped leaf; a tie left out and reported), the rig on the node its roots hang under (the nearest above them all where they hang under several; each root's chain poses in its own root's space), each rest and anchor in Unity's axes, the values where the file stands, and a convex collider on each grab part's bare meshes; a v1 record is refused, and a re-export without the record drops the components |
| `Features/Lods/Editor/LodsImporter.cs` (`M3trik.Unitytk.Lods.Editor`, editor only; the generated `LodsRecord.g.cs`) | -- (Unity's own `LODGroup`) | Unity's model importer reads the `_LOD<n>` names alone: it gives the model's root ONE `LODGroup` merging every level-named mesh of the file by index, and ignores the FBX's LodGroup thresholds (measured, Unity 6000.3). Reads the `lods` record off `data_export` through its generated shape (`LodsRecord`) and gives each group its own `LODGroup` on its node, each level's renderers one LOD at the record's heights: the node bound by name (exact, else the namespace-stripped leaf; a tie is left as Unity made it and said in the import log), each level by name under it the same way (a tie is said, and the level left out). The merged group Unity made keeps only what no group claims, and goes when that is nothing. A level the file lacks is said, the next one kept drawing its range (the last kept down to the group's cull); heights that miss a level are said, and the group switches at the default ladder (each half the last, LOD0 at half the screen). A group whose node, or two of whose levels, the file lacks is said and left as Unity imported it. Never enables Unity's Mesh LOD (a different, index-buffer mechanism Unity advises against combining with a `LODGroup`). |
| `Materials/Editor/MaterialsImporter.cs` (+ `GltfSurfaces.cs`, a glTF material read into the kernel's `LitSurface`, with the generated `MapAbsence.g.cs`, what a map that is not there stands for; `MaskSource.cs` and `MaskTextures.cs`, the repacked mask; `MaterialsTextureRoles.cs`, an image's first-import role; the actions `MaterialsActions`) | -- (editor-only) | Writes every material the file's `materials` record names from its glTF material, on import, through the kernel's one writer (`MaterialWriter`) onto the render pipeline's own Lit, or its Unlit for an unlit one: base colour and opacity, metal and roughness, occlusion, normal, emission with its strength (above 1 kept), the alpha mode (BLEND blended, MASK cut at its cutoff), double-sided and the maps' UV transform. A file without a record, and a material it does not name, keep Unity's own import. The actions do the same on any material, with a dry run and one undo step. See [Materials](#materials). |

### What a delivery carries

- **Render effects**: the `render_effects` record names each node's channels and each material's animated emission, and each curve rides its proxy (`<target>__<channel>`, its `scale.x` the curve) in every take, so it arrives as keyed. A material keeps its authored alpha mode, blended only while its opacity is below 1.
- **Audio**: the `audio` record (v3, the table above) rides the `data_export` carrier as a plain string, and each sound's file is embedded in the FBX with its SHA-256 in the record. Each sound is cut into one cue per clip at the source, in seconds from the clip's start with its offset and stop: a sound across a cut between shots stops at the cut and the next clip plays it on from where it had reached, so in Unity every clip plays its own cues and a sequence of clips plays them in turn.
- **Shots**: one FBX take per shot, named after it and spanning its range on the timeline; the `animation` record keeps per shot only its description and metadata. Each take becomes one Unity AnimationClip, in seconds from the shot's start; the importer joins each shot to the clips cut from its take (`ShotList`, on the model root, under unitytk ▸ Shots in the inspector's Add Component menu), so a clip renamed or split in the Animation tab keeps its shot. A file with no shots ships one take of the whole timeline and no record. A project with no DCC behind it gives a clip a shot with `ShotsActions.AddEntry` (or the `ShotList` inspector), on an object of its own. The delivered entries are the source's, so `AddEntry` refuses an imported model and its instances: an entry added on an instance would be a prefab override Unity matches by list position, which the next import that changes the delivered list misaligns. Give the clip its shot on a `ShotList` of your own object instead (one whose Animator or Animation plays it).
- **Lightmaps**: the `lightmaps` record (v3) states each map on its mesh's second UV set (a producer refuses to publish a bake whose lightmap UVs are another set), and the maps and the probe's panorama are embedded in the FBX, so only the FBX is copied into the project. Keep **Generate Lightmap UVs** off on the model and the project Linear. See [Lightmaps](#lightmaps).
- **Shadows**: every plane in the `shadows` record (v3) carries its horizon map, which rides inside the FBX as a named file and is extracted beside it. **Unity plays the horizon map only**: a plane's silhouette is an extension only some readers draw; its `canvas`, the edges the producer fitted to it, places the plane here too. **The fade is computed**, not keyed: the record states its terms (`intensity`, `falloff_power`, `fade_height`, `source_ramp`, `ground`), and the component evaluates the same model every reader does from where the source and the contact stand each frame, so no opacity keys ride the file for a shadow plane. The source (a light or a bare node) and the contact travel in the delivery and resolve by name; a bare source node casts through a switched-off stand-in Light the importer hangs under it. The record's lengths are in its own units, and `unit_scale` (metres per unit) turns them into metres. Set `followSource` off on the component to play the plane's own transform instead. In Unity, **Tools ▸ unitytk ▸ Shadow Plane** makes a plane under any selected object (Create Shadow Plane), bakes a plane's map from its target (Bake Horizon), and rebakes every plane whose target changed (Rebake Stale Planes). **Try it** on the [ShadowPlane sample](#samples): three planes, a moving lamp and a sun.

### Lightmaps

A baked delivery arrives lit: import the FBX, place the model, and its lightmaps show on the
render pipeline's own Lit materials, with no shader of unitytk's and nothing to run.

- **What you get.** The model's root carries a **Lightmap Set** (`LightmapSet`; Add Component ▸
  unitytk ▸ Lightmap Set): the maps, each renderer they light with its map and its rect, the lightmap
  mode (Non-Directional for a delivery) and the bake's reflection probe, all as references. Whenever it
  is enabled, in the editor and in a player, it binds them through Unity's own lightmap fields
  (`LightmapSettings.lightmaps`, `Renderer.lightmapIndex`, `Renderer.lightmapScaleOffset`), sets the
  scene's lightmap mode to its own and switches its probe on; disabled, it gives each slot and each
  renderer's index and rect back and switches the probe off (the mode is the scene's, and stays). Unity
  keeps no lightmap it did not bake itself, so this is what brings them back on every scene load; a set
  kept across a scene load (`DontDestroyOnLoad`) binds again after it. No material or property block is
  touched, so the SRP Batcher keeps batching.
- **The maps** ride inside the FBX. The import extracts them beside the model, imports each as a
  Lightmap the first time (Unity encodes it for the project's Lightmap Encoding) and applies it as
  stored: linear light, any intensity the producer folded in. Objects sharing an atlas share one slot.
  Each object is found by name and hierarchy, namespace-tolerant: same-named objects (two machine bodies
  both called `BODY`) are told apart by their parents, and one that still ties is left unbound and named
  in the import log, never guessed. A mesh with no second UV set is left unbound and named.
- **The reflection probe** is a native custom `ReflectionProbe` on a child of the root, `Reflection Probe`,
  at the capture point and box-projected onto the room it was captured in (an open scene's is read as
  distant), in metres: the record's units times its `unit_scale`, X mirrored as the FBX import mirrors it.
  Its cubemap, a sub-asset of the model, is made of the record's panorama by Unity's own texture pipeline
  (Latitude-Longitude, Specular convolution), turned the quarter turn between the record's layout and
  Unity's. The panorama already holds the bake's lights as the capture point sees them, so the highlights
  come with it. The record's per-group highlight copies and its lights are optional extensions only some
  readers use: Unity picks reflection probes by volume, not per renderer, and has no light that adds
  only its specular, so it reads neither. Keep a probed model unrotated: a reflection probe and its box
  stay aligned to the world axes (**Validate Scene Lightmaps** names a rotated one). Scaling it is fine:
  the set sizes the probe's box by the instance's scale whenever it binds.
- **One owner per scene.** A scene's baked lighting is Unity's bake's or its Lightmap Sets', never both.
  While the scene has a Lighting Data Asset (Unity baked it), its sets stand down: they bind nothing and
  switch their probes off. Otherwise they bind. Whichever ran last owns the scene: **Generate Lighting**
  writes a Lighting Data Asset, so the sets stand down; **Tools ▸ unitytk ▸ Lightmaps ▸ Apply Scene
  Lightmaps** (`LightmapsActions.Apply`) clears the bake (Unity's own `Lightmapping.Clear` and
  `ClearLightingDataAsset`: the Lighting Data Asset, its lightmaps, light probes and baked reflection
  probes, for every open scene), imports each set's maps as Lightmaps and its probe's panorama as a
  readable linear source where they are not, and binds the sets. Its dry run names everything the clear
  takes, and it asks before each; the menu plans it first and lists the files the clear deletes for you
  to confirm, since no undo brings a bake back (re-bake to return to it). It never clears a bake whose
  maps a set holds: it names them, an error. A player keeps the same rule: its build marks each scene
  Unity's bake owns, and every set in it -- a prefab instantiated there at runtime too -- stands down.
- **Fill a set yourself.** **Capture Scene Lightmaps** (`LightmapsActions.Capture`) fills the selected
  object's set from the lightmaps the scene shows on the renderers under it, whichever baker made them.
  A map of Unity's bake would go with that bake when Apply clears it, so the set holds a copy of it, in
  `<scene>_Lightmaps/<object>/` beside the scene (copied again only after a re-bake).
  `LightmapsActions.Assign(root, renderer, map, scaleOffset)` sets one renderer by hand. **Validate
  Scene Lightmaps** (`LightmapsActions.Validate`) refuses sets in one scene whose modes disagree (a scene
  reads one), names a scene Unity's bake owns that still holds sets, and names a rotated probed model.
  Each action has a dry run, asks before each change, and a menu's run is one undo step.
- **Keep** a Linear colour-space project (the maps are linear; the import warns otherwise) and
  **Generate Lightmap UVs** off on the model (it would overwrite the DCC's lightmap UVs; the import
  warns).
- **Try it** on the [Lightmaps sample](#samples): a floor and three boxes, their maps and the probe
  inside the FBX, and a demo scene its set owns.

---

## Render effects

Three components, in `M3trik.Unitytk.RenderEffects`, each keyed in the Animation window on its own field
or filled by a delivery's `render_effects` record (the [RenderEffects sample](#samples) carries every channel):

| Component | On | Field a clip keys | What it does |
|:---|:---|:---|:---|
| **Fade** | a subtree root | `alpha` (0..1) | The opacity of every renderer under it, multiplied by every Fade above (a `CanvasGroup`'s nesting). At 1 the materials are as authored: opaque, sorted with the opaque pass, writing depth, casting shadows. Below 1 each renderer wears its material's blended twin; at 0 it is hidden. |
| **Highlight** | a subtree root | `intensity` (0..1) | A glow from `colorDim` (at 0) to `color` (at 1) added to each material's uniform emission. Over a mapped emission the map is set aside while the intensity is above 0 and is back at rest. `pulse` swings the glow on its own within the intensity. |
| **Emission Track** | any object, naming a `material` | `weight` (0..1) | The material's emission, `color` x `strength` x `weight`, its map kept, on every renderer in scope wearing it (the nearest Animator's hierarchy). Each material a delivery's record animates the emission of arrives with a track. |

A visibility is the node's own active state (`GameObject.m_IsActive`, Unity's own binding), stepped: one curve
hides the whole subtree, lights and colliders included.

**The writer.** The outermost enabled Fade or Highlight writes every renderer under it; a nested one holds
its value. A renderer wears a twin of its material only while an effect over it is active: one twin per
material and state, shared by every renderer the root covers (a 2,000-renderer building with 20 materials
wears 20), instanced from a template made in the editor, so a player build keeps the shader variants it
draws with. At rest every renderer is back on its shared material. No property block, no shader of its own:
batching is kept. `Apply()` puts the values on screen now, for a script that cannot wait for the frame's end.

**Edit Mode.** Outside play mode an effect shows only when asked: an inspector edit, `Apply()` (from a script
or an agent), or a clip previewed in the Animation window or a Timeline, during which every effect in the open
scenes shows the values it samples. One editor-side keeper holds what each shows and gives the renderers
their materials back (and shows what it hid) when the effect is removed, disabled or deactivated, its
inspector closes or the preview stops, and before a scene is saved or closed, a prefab is written (dragged
into the Project window, or an instance's overrides applied), scripts reload or play mode starts: a preview
never reaches a saved scene or prefab. An effect in a prefab or model asset shows nothing (open the
prefab, or place it in a scene).

**The import** reads the record (never a node's attributes), adds the components, rebinds each curve proxy in
each clip onto the component's field (a visibility onto the node's active state, where Unity's own
renderer-enabled curves under the node are dropped), deletes the proxies, asks the carrier for unresampled
curves (a resampled step is a one-frame ramp) and makes the twins into the model, with the table its effects
reference (`MaterialTwins`). A file whose one top-level node holds the carrier has that node folded into the
model's root by Unity's import (the root takes the file's name), so the channels the record names on it are
left out, said in the import log: their proxies are deleted and Unity's own renderer curves kept. A delivery
with the carrier at the top level beside its nodes is never folded. Each track joins its node by the name
after its namespace; two tracks (or material entries) of one such name are a tie, left out and said.

**Actions** (`RenderEffectsActions`, each with a dry run and one undo step): `KeyFade` and `KeyPulse` key a
fade or a pulse on a clip of the project's own, adding the component (a target in a prefab asset is refused:
key it in Prefab Mode or on an instance); `PrepareMaterials` makes the twins of a
hierarchy's effects into an asset and gives it to them (**Tools → unitytk → Render Effects → Prepare
Materials**, on the selection; the inspector offers it where twins are missing).

**Pipelines.** Built-in and URP, through their hosts' blend switches and their rows of the kernel's table
(`LitTable`). HDRP's row names its emission slots, so a twin there sets an emissive map aside and writes
its emission colour; its blend switch belongs to an HDRP host, which no door registers yet, so in an HDRP
project no twin is made: an effect shows only as hidden at 0, said once at import.

## Materials

A delivery states its materials in its `materials` record as **literal glTF 2.0**
(the [channel table](#how-the-metadata-arrives-the-import-contract) has its shape):
any glTF reader reads it, and the Khronos glTF Validator checks it as it stands.
On import, `MaterialsImporter` finds each record material by name among the model's
materials (Unity's import writes a namespaced `ns:Mat` as `ns_Mat`; `MaterialRecord.Find`
joins either spelling) and writes it through the kernel's **one material writer**
(`MaterialWriter`, grown from the render-pipeline host): the pipeline's own Lit, by
a small declared table of each pipeline's property and keyword names (`LitTable`:
Built-in `Standard` and `Unlit/*`, URP `Lit` and `Unlit`, HDRP `HDRP/Lit` and
`HDRP/Unlit`). No custom shader and no property block touch the material, so SRP
batching, Unity's inspectors and every pipeline tool keep working.

| glTF | Unity, through the pipeline's own Lit |
|---|---|
| `baseColorFactor`, `baseColorTexture` | the base colour (linear, stored sRGB-encoded) and base map; its alpha the opacity |
| `metallicRoughnessTexture` (roughness G, metal B), `occlusionTexture` (R), the factors | one **mask** in Unity's layout, R metal, G occlusion, B 1, A smoothness, the factors baked in: Standard's and URP Lit's metallic map and occlusion map, HDRP Lit's mask map. Repacked at import into a texture the import owns (a sub-asset of the model, rebuilt with it), which depends on its source images |
| `normalTexture` (+ `scale`) | the normal map (imported as one on its first import) and its strength |
| `emissiveFactor` x `KHR_materials_emissive_strength` | the HDR emission colour, above 1 kept, and the emission map |
| `alphaMode` OPAQUE / MASK / BLEND, `alphaCutoff` | opaque; cut out at the cutoff; blended through the host's blend switch (Built-in Fade, URP Transparent) |
| `doubleSided` | both faces (URP `_Cull`, HDRP `_DoubleSidedEnable`); Built-in's Standard draws front faces alone, said |
| `KHR_materials_unlit` | the pipeline's Unlit (Built-in's four `Unlit/*`; theirs take no tint over a map, said) |
| `KHR_texture_transform` | the maps' tiling and offset, turned from glTF's UV space (origin top left) into Unity's; one transform per material (the base colour map's) |

An image the record names is found by file name in the model's media folders,
after the one extraction its first import asks for; one still missing reads as
absent (a colour or emission its factor alone, a flat normal, no occlusion, metal
and roughness dielectric and matte) and is warned. A material the record names that
the model has none of is warned. A v1 record is refused with its fix (re-export). A
pipeline no unitytk host serves (HDRP, until its door registers one) keeps Unity's
own import, said once per session. Samplers (wrap, filter) are the texture
importer's settings in Unity, not the material's: they are not read.

**Actions on any material** (`MaterialsActions`, each with an optional `OpContext`:
a dry run, one undo step, issues), for a Unity user with no DCC as much as for a
delivery:

- `Apply(material, gltf, name, imageFolder)` -- write a glTF material (a `.gltf`
  file's JSON, its defaults filled, or a `materials` record) onto a material of the
  project's own; its mask is written beside it as a PNG.
- `PackMetallicSmoothness(material, metallicRoughness, occlusion)` -- pack glTF's
  layout into Unity's mask, written beside the image, and bind it.
- `FixTextureRoles(material)` -- import each bound texture in its slot's role
  (normal map, linear data, sRGB colour).
- `Reapply(modelPath)` -- write a model's record onto the project materials its
  import is remapped to (extracted materials).

Prior art: glTFast's material generators and UnityGLTF's importer map the same glTF
model per pipeline, but onto shader graphs of their own, which a Unity user would
not recognise and the effects writer would have to learn; glTFast stays an opt-in
door for reading GLB files, not built. The [Materials sample](#samples) shows each
row.

## Articulated rigs

A prop delivered with an `articulation` record (a desk lamp, a magnifier arm:
rigid parts on hinge, swivel, ball and slide joints) imports ready to pose
and grab, in Unity's own terms:

- **`ArticulatedJoint`**, on each part: its parent joint (a reference), its
  rest pose in that joint's frame, its **anchor** -- the point and axes it
  turns about and slides along, in the rest frame (the part's own pivot by
  default) -- its ordered channels (`Rotate X/Y/Z`, `Slide X/Y/Z`) with their
  limits and weights, and the values themselves (`rx` ... `tz`: degrees, and
  for a slide the rig's units), plain fields the Animation window keys.
  `changed` fires as a value moves.
- **`ArticulatedRig`**, on the rig's root: its joints, parents first, and the
  parts a hand grabs, by reference. Every frame it poses each part from the
  **joint chain** -- its parent joint's pose, then its rest, anchor and
  channels -- never from its scene parent, so a flat model (every part a
  child of the root) chains as a nested one does. Each root joint's chain
  poses and grabs in its own **space**, the node its part hangs under
  (`SpaceOf`): roots under different nodes need no node they share, and one
  node may hold several rigs.

The values are Unity's: a turn reads as Unity's rotate tool gives it about
that axis, and an imported rig's values are the record's with `ry`, `rz` and `tx`
negated -- Unity's FBX import mirrors X, and so does every rest and anchor
the importer fills. Lengths are in **the rig's units**: each space's own,
metres for an unscaled space. The record states metres as the file holds each
joint's parent frame, and Unity's FBX import lands them so, with a space's own
scale above the rig -- under a root that carries a scale of 100, `tx = 0.5`
slides 50 m in the world. Nothing is measured.

**Animation and posing.** An imported rig follows its animation
(`followAnimation`): the delivery's clips move the parts, and each frame the
rig reads the values off them, so the values and their events track the
clip. A rig made in Unity poses its parts from its values, so a clip keying
the values plays it. Either way a pose -- a grab, `ApplyState`, a value a
script sets over a playing clip -- holds, written after the Animator every
frame, until `ReturnToAnimation()` (or, with `resumeOnAnimatorChange`, until
the Animator's state or time moves after a release).

**Grab API** (input-agnostic, world space):

```csharp
var rig = model.GetComponentInChildren<ArticulatedRig>();
rig.BeginGrab(hitTransform, hitPoint, handRotation); // or BeginGrab(hitTransform, hitPoint)
rig.UpdateGrab(newPoint, newHandRotation);           // each frame while held
rig.EndGrab();                                       // the released pose holds...
rig.ReturnToAnimation();                             // ...until the animation takes over again
```

A grab starts from the pose on screen, solves only the channels between the
root and the held link, never leaves a limit, and holds its pose over the
animation. `grabRotation` picks how a link on a **ball** joint turns: `Hand`
follows the hand's rotation (XR), `Keep` holds the rotation it was taken with
(a mouse), `Free` leaves it to the solver; hinges and slides follow the held
point. `GetState` / `ReadState` / `ApplyState` read and write every value at
once (slot order: joint by joint, each in its channel order).

**In the editor.** A joint's inspector draws Scene handles for its anchor (a
position and a rotation handle) and its limits (an arc from min to max for
each turn, a span for each slide, each with its value); while a rig's or a
joint's inspector is open the rig poses the parts its values move off their
rest, and gives each such part's transform back as the last of those
inspectors closes, before a save or a prefab write (showing again after while
an inspector is open), a script reload or Play -- the values stay. A part at
its rest is never held, so a move made to it stays; a posed part the person
moves (the Move tool, an undo) is theirs until a value above it moves again,
and *Capture Rest From Transform* (a button on the joint's inspector, and the
menu) makes where it stands its rest, so play poses it there. A rig that
follows its animation is left to the Animation window and Timeline.
**Tools ▸ unitytk ▸
Articulated Rig** makes one in Unity: *Create Rig* on the root, *Add Hinge /
Slide / Ball Joint* on the selected parts (each hanging from the joint above
it, turning about its own pivot), *Set Limits From Pose* (each value away
from rest becomes its side's limit; it asks first where it narrows one),
*Widen Limits To Pose*, *Capture Rest From Transform* and *Make XR Grabbable*
-- each the action of the same name on `ArticulatedRigActions`
(`SetLimitsFromPose(widen: true)` for the widening, `CaptureRest` for the
capture), which a script calls too.

- **Mouse** — add `ArticulatedRigPointerGrab` anywhere (e.g. the camera): press
  on a part, drag in the camera-facing plane through the grab point, scroll to
  push / pull along the view ray. It reads the pointer registered behind its
  seam (`ArticulatedRigPointerGrab.UsePointer`, an `IPointerInput`): the
  Input System door (`ArticulatedRig/InputSystem/`, its own assembly,
  compiled only with `com.unity.inputsystem`) registers the Input System's
  mouse where that is the active handler; with nothing registered it reads
  the legacy Input Manager. A touch or a custom pointer registers its own.
- **XR** — with the XR Interaction Toolkit 3.x installed, *Make XR Grabbable*
  (`ArticulatedRigActions.MakeXRGrabbable`) puts an `ArticulatedRigXRSetup` on
  the rig's root: every grab part becomes an `ArticulatedRigXRGrab` (an
  `XRBaseInteractable` — not a grab interactable, the part never moves
  itself) that hands select / update / exit to the rig at the interactor's
  attach pose. The `ArticulatedRig/XRI/` door is its own assemblies (not
  auto-referenced), compiled only when XRI is present, so a project without
  XRI carries them inert (and the action says so).

**Try it** on the [ArticulatedRig sample](#samples): a desk lamp on two
hinged arms, a telescoping section and a hinged shade, its take playing and
every part grabbable with the mouse.

**Physics.** Unity's `ArticulationBody` simulates jointed bodies under
forces; a rig posed by a hand and its clips is not one. A physics-driven
articulation is a later door.

---

## Extending the package: its anatomy

This section is for whoever adds to the package. The package is laid out the way the
workspace's code standard lays out every unit (`m3trik/docs/CODE_STANDARD.md`
section 0, "one holon at every scale"), in Unity's spelling. The architecture
of record is the *unitytk Base Architecture* (Rev G). The layout below is the
package's: 0.7.0 moved every feature into it, and nothing is left at the root
but `package.json`, this README and the licence.

**Parts, lowest first.** Each part is its own assembly, and a reference points down only.
The compiler enforces the order, and `check_layers.py` refuses a reference
that would make a peer compile.

| Part | Holds | May use | Assembly |
|:---|:---|:---|:---|
| `Core/` | Pure building blocks: JSON, the version-checked record reader, the run contract's types (`Outcome`, `Issue`, `Change`, `OpCall`, `OpRequest`, generated from pythontk), `RunRules`, the import-order table and the persisted-strings list | nothing | `M3trik.Unitytk.Core` (`noEngineReferences`, tested by `dotnet test`) |
| `Kernel/` | `OpContext`; the three operations (`unity.csharp.exec`, `unity.scene.view`, `unity.project.doctor`) and their batch and session servers; the provider registry (`[Provides]`, `Provided`); the import gate (`Imports.IsDelivery`: an FBX is a delivery); the import log; the render-pipeline host and its stand-in (`PureRenderPipelineHost`) | Core | `M3trik.Unitytk.Kernel` (editor) and a runtime twin; doors such as `M3trik.Unitytk.Kernel.URP` |
| `Contracts/` | The interfaces features meet through (`IRecordSource`, `IModelMedia`, ...), each with its `Pure*` stand-in | Core, Kernel | `M3trik.Unitytk.Contracts` (and `.Editor`) |
| `Features/<Feature>/` | One capability: `Model/` (a host-free port, `noEngineReferences`), `Runtime/` (components), `Editor/` (its actions and importer), a door per outside package (`XRI/`, `URP/`), `Tests/` | Core, Kernel, Contracts; never a peer feature | `M3trik.Unitytk.<Feature>[.Model/.Editor/.<Door>]` |
| `Tools/<family>/` | Menus and windows over the features' actions; view state only | anything below | an editor assembly referencing only the features it serves |
| `Kits/` | Recipes (short scripts over actions) and prefab kits | the features' public API | scripts and prefabs |
| `Samples~/<Sample>/` | A feature's [sample](#samples): its delivery (generated) and its demo recipe | the feature's public API | `M3trik.Unitytk.Samples.<Sample>` (and `.Editor`), compiled where the sample is imported |

The foundation (Core, Kernel and Contracts) changes only with the maintainer's
approval. Agents build everything above it. Prototypes live in the owned
project's `Assets/Sandbox/` (`unity_project.build`), which nothing references; a
prototype graduates by being rebuilt in its home.

**An action** is how a feature does something to a project. It is one public static
method in the feature's editor assembly: plain values in, a plain result out, XML
comments, and an optional `OpContext ctx`. The method asks `ctx` before each change:
`ctx == null || ctx.ShouldChange(obj, what)` before a scene edit, `ShouldCreate`
and `ShouldDelete` before an object is made or removed, and `ShouldWrite(path, what)`
before a file is written. In a dry run each ask lists the change and returns false.
A menu item calls the action in one line through `OpContext.RunMenu(title, ctx => ...)`,
which gives one undo group, a progress bar with Cancel, and the issues in the
Console. An agent finds the action with `api.search` and calls it from code run by
`unity.csharp.exec`. Nothing declares an action: only the kernel declares operations
(`[Operation]` is internal to it). A feature's editor code never calls `Undo.*`,
a progress bar or `Debug.Log` directly; it goes through `OpContext` and the kernel's
import log, and the kernel-bypass check holds that.

**A need** is an action parameter of a contract type that defaults to its provider:
`if (records == null) records = Provided.One<IRecordSource>();`. A provider marks
itself `[Provides(typeof(IRecordSource))]`. A test passes a stand-in, and every
other caller passes nothing.

**A door** is the only assembly that may reference an outside package. It is
`autoReferenced: false` and compiles only when its package is present
(`versionDefines` plus `defineConstraints`), so an upgrade of that package lands
in the door alone.

The package's **surface** is every name a project will depend on, not only the
public types. No project uses unitytk yet, so only the GUIDs are held now; from the
first project on, each of these grows by addition:

- each script's **GUID** (its committed `.meta`);
- the **serialized field names** a saved scene or prefab holds (rename one only
  with `[FormerlySerializedAs]`);
- the public C# API, which `api.search` reads and `check_api_surface.py` reports
  per release;
- the menu paths (Tools ▸ unitytk ...);
- the persisted strings (the `data_export` node, the `_Media` folder suffix, the
  `unitytk.extracted-by:` mark, the `Library/unitytk/` memo, AnimationEvent
  function names `PlayAudio` and `StopAudio`).

**A generated record type** (`<Shape>.g.cs`, beside the code that reads the
record) is one half of a `partial class`. It holds the record's declared shape,
with one nested class per declared object under `<Shape>.Shape` and a field per
key, read off the parsed JSON tree and refusing a value that fails the shape. It
also holds `CurrentVersion`, the declared version, the one the record is read at.
The hand-written half keeps the reader's policy: what a value means once read (a
null where the shape allows one, a unit, a placement). Every reader reads its
record the same way, `UnitytkRecords.Read(key, json, <Shape>.CurrentVersion,
<Shape>.Shape.Payload.Read, ...)`; no importer parses a record or spells a version
of its own. Never edit a `.g.cs`: change the declaration in pythontk and re-run
`m3trik/scripts/sync_scene_records.py` (`sync_operation_contract.py` for the run
contract's types).

**The type toolbox**, in C#:

| Need | C# |
|:---|:---|
| value object | a `readonly struct` |
| vocabulary | an `enum` (generated, where it names channels) |
| JSON payload shape | a class with public fields, generated from the declaration (not `JsonUtility`, which cannot say null) |
| structural contract | an `interface`, with its `Pure*` stand-in |
| distinct ids | a `readonly struct` wrapping the int |
| variants | an abstract class plus a registry |
| identity in saved data | the GUID in the `.meta`; `[FormerlySerializedAs]` on a renamed field |

**A new file** ships with a committed `.meta` that carries a GUID of its own: Unity
mints one for a file without it in each project that imports the package (an
immutable package's file gets none at all), so a reference to it would differ
from project to project. **A new part** names its prior art: what it weighed, and why each was or
was not adopted, goes under `Prior art:` in its main type's `<remarks>`.

## Troubleshooting

| Problem | Fix |
|---|---|
| The prefab imports with only its root, or a channel's JSON arrives cut short | The FBX was written ASCII: Unity's ASCII FBX reader drops the scene or truncates a `data_export` string past about 6 KB. Export binary. |
| No components appear after import | Check the FBX carries the feature's record (a feature acts only when its record is present on the file), then reimport: select the FBX → right-click → Reimport |
| No `animation`/`audio`/`lightmaps` record on the imported prefab | The `data_export` carrier wasn't in the export, or it carries a record of another version, or one that fails its declared shape (the import warns), or under a pre-2026-10-20 key (`shot_metadata`, `audio_manifest`, `lightmap_metadata`, `shadow_metadata`): unitytk reads the current standard only — re-export with the `data_export` carrier included |
| CS compile errors after copying files by hand | Install the package through the Package Manager instead: every feature compiles against the foundation (`Core/`, `Kernel/`, `Contracts/`) and the record carrier (`Features/Carrier/`), which a hand copy must carry whole, each file with its `.meta` |
| A clip's `PlayAudio` events are missing, or the import warns `no AudioClip found` | Re-export the FBX from a current DCC exporter so it embeds its sounds; for an FBX that embeds none, put each audio file near the FBX (same folder, or an `Audio/`/`Sounds/`/`SFX/` folder beside it or under `Assets/`), named exactly as the record's `sound` (extension included), and reimport the FBX |
| Opacity doesn't fade (snaps on/off) | The node's track in the `render_effects` record names `visibility` and not `opacity` (a switch, not a fade), or the file carries no `<node>__opacity` curve proxy under the node (the import log names a record without its proxies): re-export the file with the node's opacity keyed, so the record names the channel and its curve rides the proxy in each take. Select the node: a `Fade` with its `alpha` keyed in the clip is the fade. |
| Objects invisible at start | Normal if opacity starts at 0 — they fade in at the keyed time during playback |
| Object pops off instead of fading, with an import warning naming its material | Its shader has no blend switch the render pipeline's host knows (Built-in Standard / Autodesk Interactive `_Mode`, URP `_Surface`), or the pipeline has no unitytk host (HDRP) — give the object a transparent-capable material, then reimport |
| A Fade shows in Play but not in the Scene view | Outside play mode an effect shows only while asked (see [Edit Mode](#render-effects)): edit it in the inspector, call `Apply()`, or preview the clip in the Animation window or a Timeline |
| Shadow plane renders magenta, or on the pipeline's unlit shader (`Unlit/Transparent`, URP's `Unlit`) instead of `m3trik/ShadowPlane` | `ShadowPlane.shader` failed to compile (check the Console, reimport the package), or the render pipeline's host does not draw the shader (the import says so): each plane falls back to the pipeline's unlit shader, with no horizon march and no fade: the fade is the component's, written to the package shader, so a fallback plane draws its texture at a constant alpha, the record's intensity (URP's Unlit), or at full opacity (Built-in's `Unlit/Transparent` has no colour to carry one). A pipeline no host serves (HDRP) keeps Unity's imported material, unfaded too |
| An articulated rig does not respond to grabs | The import names any joint or grab part it could not find, or that tied with another of its name (all must be in the export selection, each with a name that tells it apart); the rig's inspector says why its joints make no rig; a part needs a collider for the pointer / XR ray -- reimport the FBX, or call `EnsureGrabColliders()` for a rig built in Unity |
| Shadow plane draws nothing / does not follow the light | Its horizon map is missing (the import names it: re-export with the map embedded, or bake one with Tools ▸ unitytk ▸ Shadow Plane ▸ Bake Horizon) or its layout is one the shader cannot march (the inspector says so); or its `contact` or `source` did not resolve (the import names a name that is missing or tied) -- set them on the component, or give the nodes names that tell them apart and re-export; or, outside play mode, nothing has placed it yet: a plane draws while its inspector shows it, and in Play |

## License

The C# in this folder is **MIT**, unlike the rest of unitytk (LGPL-3.0-or-later): it installs into Unity projects and compiles into player builds, and the LGPL's relink requirement cannot be met in an IL2CPP build (Quest, iOS, WebGL), so a shipped app carries no LGPL obligation.

```text
MIT License

Copyright (c) 2026 m3trik@live.com

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
