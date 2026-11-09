# A door

**Belongs:** the only code that references an outside package (URP, the Input System, the XR Interaction Toolkit, Timeline), in its own assembly beside the part it serves: `Features/<Feature>/<Door>/` or `Kernel/<Door>/`. **Does not:** hold logic the part needs without the package: the part declares a seam (an interface or a hook) and the door implements it. A new door is a stop-and-ask change.

**Shapes:** a runtime seam the door registers itself into (`RenderPipelineHosts.Register` from `[RuntimeInitializeOnLoadMethod]`, the editor's `[InitializeOnLoadMethod]` beside it; `[AlwaysLinkAssembly]` when that registration is the assembly's only player code), or an editor seam the door `[Provides]` and the part asks `Provided.One<T>()` for. A door with editor code too keeps it in its own editor-only assembly, `<Door>/Editor/` (`M3trik.Unitytk.<Feature>.<Door>.Editor`).

**Add one:** `python m3trik/scripts/scaffold_unity_part.py door <Feature> <Door> <package> <version>` writes the assembly: `autoReferenced: false`, a `versionDefines` row on the package (its floor: the version Unity 6.3 ships, `Data/Resources/PackageManager/Editor/manifest.json`), the same define in `defineConstraints`, so it compiles only when the package is present. Reference the package's assemblies from it alone.

**Checked by:** `check_layers.py` (an outside reference outside a door fails); `unity_package.compile`, whose package builds compile it against the owned project's packages, and whose plain builds compile everything without it.
