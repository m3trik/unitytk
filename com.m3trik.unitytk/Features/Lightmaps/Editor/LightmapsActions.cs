using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Lightmaps
{
    /// <summary>
    /// The Lightmaps feature's actions, in Unity's terms: make the open scenes' lightmap
    /// sets their owner (<see cref="Apply"/>, which clears Unity's bake), fill a set from
    /// what the scene shows (<see cref="Capture"/>) or by hand (<see cref="Assign"/>), and
    /// check the scenes' sets against the one-owner rule (<see cref="Validate"/>). Each
    /// takes an optional <see cref="OpContext"/>: a dry run plans, a menu's run is one undo
    /// step for its scene edits, and none just runs. A second run of each changes nothing.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's Lighting window (Generate Lighting, Clear Baked Data), whose
    /// bake writes a Lighting Data Asset and whose clear is <c>Lightmapping.Clear</c> and
    /// <c>Lightmapping.ClearLightingDataAsset</c>: these do the same on the same calls, so
    /// a scene is owned by Unity's bake or by its sets, never both. Unity has no action
    /// that keeps a lightmap it did not bake; the set binds it (<see cref="LightmapSet"/>).
    /// </remarks>
    public static class LightmapsActions
    {
        /// <summary>No lightmap set in the open scenes: nothing to apply.</summary>
        public const string NothingToApplyCode = "unity.lightmaps.nothing_to_apply";
        /// <summary>No renderer under the root shows a lightmap: nothing to capture.</summary>
        public const string NothingToCaptureCode = "unity.lightmaps.nothing_to_capture";
        /// <summary>Sets in one scene state different lightmap modes, which one scene cannot read.</summary>
        public const string ModesDisagreeCode = "unity.lightmaps.modes_disagree";
        /// <summary>Unity's bake owns a scene that holds sets: they stand down.</summary>
        public const string BakeOwnsSetsCode = "unity.lightmaps.bake_owns_sets";
        /// <summary>A set holds a map of Unity's bake, which clearing the bake would delete: the clear is skipped.</summary>
        public const string BakeMapsHeldCode = "unity.lightmaps.bake_maps_held";
        /// <summary>A set's box-projected probe sits under a rotated root, which Unity's axis-aligned box cannot follow.</summary>
        public const string ProbeRotatedCode = "unity.lightmaps.probe_rotated";

        /// <summary>
        /// Make the open scenes' lightmap sets their owner: Unity's bake cleared first
        /// where a scene has one (<c>Lightmapping.Clear</c> and
        /// <c>Lightmapping.ClearLightingDataAsset</c>, which take the whole bake -- its
        /// Lighting Data Asset, its lightmaps, its light probes and its baked reflection
        /// probes -- for every open scene), each set's maps imported as lightmaps and its
        /// probe's panorama as a readable linear source where they are not, and every set
        /// bound. Each is asked of <paramref name="ctx"/> first: a dry run names every file
        /// and the light probes the clear takes and changes nothing. The clear is no scene
        /// edit: no undo brings a bake back, re-bake it. A set holding a map of the bake
        /// (one <see cref="Capture"/> did not copy out) is never left without it: the clear
        /// is skipped, an error that names the maps.
        /// </summary>
        /// <param name="ctx">The run's context: a dry run lists what it takes and writes; null just runs.</param>
        /// <returns>How many renderers the sets bind (in a dry run, would bind).</returns>
        public static int Apply(OpContext ctx = null)
        {
            List<LightmapSet> sets = OpenSets();
            if (sets.Count == 0)
            {
                ctx.Raise(NothingToApplyCode, "No Lightmap Set in the open scene(s): nothing to apply. Import a delivery that " +
                                              "carries lightmaps, or capture one (Capture).");
                return 0;
            }
            // The bake: everything the clear takes, asked first -- unless a set holds one of its maps.
            var bakes = new List<(Scene scene, string path, List<string> textures)>();
            foreach (Scene scene in OpenScenesList())
            {
                LightingDataAsset bake = LightmapOwner.Bake(scene);
                if (bake == null) continue;
                string path = AssetDatabase.GetAssetPath(bake);
                bakes.Add((scene, path, BakeTextures(path)));
            }
            Dictionary<string, List<string>> held = Held(sets);
            List<string> taken = bakes.SelectMany(b => b.textures).Where(held.ContainsKey).Distinct().ToList();
            if (taken.Count > 0)
            {
                ctx.Raise(BakeMapsHeldCode, $"Unity's bake is left as it is: clearing it deletes {taken.Count} map(s) the lightmap " +
                                            $"set(s) {string.Join(", ", taken.SelectMany(t => held[t]).Distinct())} hold " +
                                            $"({string.Join(", ", taken)}). Capture those sets again (Capture copies a bake's maps " +
                                            "out of it), then apply.", "error");
                bakes.Clear();
            }
            bool clear = bakes.Count > 0, allowed = true;
            foreach (var (scene, path, textures) in bakes)
            {
                allowed &= ctx == null || ctx.ShouldDelete(path, $"clear the bake of '{scene.name}': its Lighting Data Asset");
                foreach (string file in textures)
                    allowed &= ctx == null || ctx.ShouldDelete(file, $"clear the bake of '{scene.name}': {What(file, scene)}");
            }
            if (clear && LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0)
                allowed &= ctx == null || ctx.ShouldChange("light probes", $"clear the bake's {LightmapSettings.lightProbes.count} light probe(s)");
            if (clear && allowed)
            {
                Lightmapping.Clear();
                Lightmapping.ClearLightingDataAsset();
                foreach (Scene scene in OpenScenesList()) EditorSceneManager.MarkSceneDirty(scene);
            }

            // The maps, as lightmaps: Unity encodes them for the project's lightmap encoding.
            var asked = new HashSet<string>(StringComparer.Ordinal);
            foreach (LightmapSet set in sets)
                foreach (LightmapSet.Map map in set.maps)
                {
                    string path = map != null && map.color != null ? AssetDatabase.GetAssetPath(map.color) : null;
                    if (string.IsNullOrEmpty(path) || !asked.Add(path)) continue;
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)
                        || !LightmapsTextureRoles.Pin(ti, LightmapsTextureRoles.Role.Lightmap, false))
                        continue;
                    if (ctx != null && !ctx.ShouldWrite(AssetDatabase.GetTextMetaFilePathFromAssetPath(path), "import it as a lightmap"))
                        continue;
                    LightmapsTextureRoles.Pin(ti, LightmapsTextureRoles.Role.Lightmap, true);
                    ti.SaveAndReimport();
                }
            // Each probe's panorama, as the readable linear source its cubemap is made of:
            // reimported, it reimports its model, which makes the cube.
            foreach (LightmapSet set in sets)
            {
                string path = string.IsNullOrEmpty(set.probeSource) ? null : AssetDatabase.GUIDToAssetPath(set.probeSource);
                if (string.IsNullOrEmpty(path) || !asked.Add(path)) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)
                    || !LightmapsTextureRoles.Pin(ti, LightmapsTextureRoles.Role.Panorama, false))
                    continue;
                if (ctx != null && !ctx.ShouldWrite(AssetDatabase.GetTextMetaFilePathFromAssetPath(path),
                                                    "import it as the readable linear panorama its probe's cubemap is made of"))
                    continue;
                LightmapsTextureRoles.Pin(ti, LightmapsTextureRoles.Role.Panorama, true);
                ti.SaveAndReimport();
                // The model's import reads it (a source dependency); where that reimport has
                // not run yet, it is run here, so the set's probe is made now.
                string model = ModelOf(set);
                var imported = model != null ? AssetDatabase.LoadAssetAtPath<GameObject>(model) : null;
                var made = imported != null ? imported.GetComponent<LightmapSet>() : null;
                if (model != null && (made == null || made.probe == null))
                    AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceUpdate);
            }

            int bound = 0;
            bool live = ctx == null || !ctx.DryRun;
            // A reimported map reimports its model: its instances' sets are new ones.
            foreach (LightmapSet set in live ? OpenSets() : sets)
            {
                if (live && set.isActiveAndEnabled) set.Refresh();
                if (live ? set.Bound : set.isActiveAndEnabled && (clear || !set.StandsDown)) bound += Bindable(set);
            }
            if (live) ctx.Log($"Apply Scene Lightmaps: {sets.Count} lightmap set(s) own the open scene(s), binding {bound} renderer(s)" +
                              (clear ? "; Unity's bake cleared." : "."));
            return bound;
        }

        /// <summary>
        /// Fill <paramref name="root"/>'s lightmap set (added where it has none) from the
        /// lightmaps the open scene shows on the renderers under it, whichever baker made
        /// them: each renderer's map (Unity's <see cref="LightmapSettings.lightmaps"/> slot
        /// its <see cref="Renderer.lightmapIndex"/> names), its rect, and the scene's
        /// lightmap mode. A map of Unity's bake would go with that bake when
        /// <see cref="Apply"/> clears it, so the set holds a copy of it instead, in a folder
        /// of its own beside the scene (<c>&lt;scene&gt;_Lightmaps/&lt;root&gt;/</c>, each copy
        /// asked as a write, and made again only when the bake's file changed); any other
        /// map is referenced where it is. The set's probe is kept.
        /// </summary>
        /// <param name="root">A scene object whose renderers show lightmaps.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>How many renderers it captured (in a dry run, would capture); 0 for none.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="root"/> is not in a scene (a prefab asset).</exception>
        public static int Capture(GameObject root, OpContext ctx = null)
        {
            InScene(root);
            LightmapData[] shown = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            var maps = new List<LightmapSet.Map>();
            var receivers = new List<LightmapSet.Receiver>();
            var bySlot = new Dictionary<int, int>();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                int slot = r.lightmapIndex;
                if (slot < 0 || slot >= shown.Length || shown[slot] == null || shown[slot].lightmapColor == null) continue;
                if (!bySlot.TryGetValue(slot, out int map))
                {
                    maps.Add(new LightmapSet.Map
                    {
                        color = shown[slot].lightmapColor, direction = shown[slot].lightmapDir, shadowMask = shown[slot].shadowMask,
                    });
                    bySlot[slot] = map = maps.Count - 1;
                }
                receivers.Add(new LightmapSet.Receiver { renderer = r, map = map, scaleOffset = r.lightmapScaleOffset });
            }
            if (receivers.Count == 0)
            {
                ctx.Raise(NothingToCaptureCode, $"No renderer under '{root.name}' shows a lightmap in the open scene: nothing to " +
                                                "capture. Bake the scene, or bind a set, first.", location: root.name);
                return 0;
            }
            // A map of Unity's bake goes when Apply clears the bake: the set holds its copy.
            var baked = new HashSet<string>(StringComparer.Ordinal);
            foreach (Scene scene in OpenScenesList())
            {
                LightingDataAsset bake = LightmapOwner.Bake(scene);
                if (bake != null) baked.UnionWith(BakeTextures(AssetDatabase.GetAssetPath(bake)));
            }
            string folder = CopiesFolder(root);
            var writes = new List<(string from, string to)>();
            Texture2D Kept(Texture2D texture)
            {
                string from = texture != null ? AssetDatabase.GetAssetPath(texture) : null;
                if (string.IsNullOrEmpty(from) || !baked.Contains(from)) return texture;
                string to = folder + "/" + Path.GetFileName(from);
                if (IsCopyOf(to, from)) return AssetDatabase.LoadAssetAtPath<Texture2D>(to);
                if (!writes.Exists(w => w.to == to)) writes.Add((from, to));
                return texture;
            }
            foreach (LightmapSet.Map m in maps)
            {
                m.color = Kept(m.color);
                m.direction = Kept(m.direction);
                m.shadowMask = Kept(m.shadowMask);
            }
            LightmapsMode mode = LightmapSettings.lightmapsMode;
            LightmapSet set = root.GetComponent<LightmapSet>();
            if (writes.Count == 0 && set != null && set.mode == mode && Same(set.maps, maps) && Same(set.receivers, receivers))
                return receivers.Count;
            foreach (var (from, to) in writes)
            {
                if (ctx != null && !ctx.ShouldWrite(to, $"copy '{from}' out of Unity's bake, which Apply clears")) continue;
                Texture2D copy = Copy(from, to);
                foreach (LightmapSet.Map m in maps)
                {
                    if (m.color != null && AssetDatabase.GetAssetPath(m.color) == from) m.color = copy;
                    if (m.direction != null && AssetDatabase.GetAssetPath(m.direction) == from) m.direction = copy;
                    if (m.shadowMask != null && AssetDatabase.GetAssetPath(m.shadowMask) == from) m.shadowMask = copy;
                }
            }
            string action = $"capture the {receivers.Count} renderer(s) the scene shows lightmapped";
            if (!Ensure(root, ref set, ctx))
            {
                ctx.ShouldChange(root.name, action);  // a dry run: the set it would add, by its name
                return receivers.Count;
            }
            if (ctx == null || ctx.ShouldChange(set, action)) Fill(set, mode, maps, receivers, ctx);
            return receivers.Count;
        }

        /// <summary>
        /// Set by hand that <paramref name="map"/> lights <paramref name="renderer"/>, at
        /// <paramref name="scaleOffset"/> (its rect, uv2 * (x, y) + (z, w)), in
        /// <paramref name="root"/>'s lightmap set (added where it has none): the map
        /// added to the set's maps where it is not one, the renderer's receiver added or
        /// changed. A bound set binds the change at once.
        /// </summary>
        /// <param name="root">The scene object holding the set.</param>
        /// <param name="renderer">A renderer in <paramref name="root"/>'s scene, read through its mesh's uv2.</param>
        /// <param name="map">A texture imported as a lightmap.</param>
        /// <param name="scaleOffset">The renderer's rect in <paramref name="map"/>; (1, 1, 0, 0) for all of it.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>Whether it changed the set (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="root"/> is not in a scene, or
        /// <paramref name="renderer"/> is not in its scene.</exception>
        public static bool Assign(GameObject root, Renderer renderer, Texture2D map, Vector4 scaleOffset, OpContext ctx = null)
        {
            InScene(root);
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (renderer.gameObject.scene != root.scene)
                throw new ArgumentException($"'{renderer.name}' is not in '{root.name}''s scene: a set lights its own scene's renderers",
                                            nameof(renderer));
            LightmapSet set = root.GetComponent<LightmapSet>();
            int index = set != null ? set.maps.FindIndex(m => m != null && m.color == map) : -1;
            LightmapSet.Receiver was = set != null ? set.receivers.Find(x => x != null && x.renderer == renderer) : null;
            if (index >= 0 && was != null && was.map == index && was.scaleOffset == scaleOffset) return false;
            string action = $"light '{renderer.name}' with '{map.name}' at {scaleOffset}";
            if (!Ensure(root, ref set, ctx))
            {
                ctx.ShouldChange(root.name, action);  // a dry run: the set it would add, by its name
                return true;
            }
            if (ctx != null && !ctx.ShouldChange(set, action)) return true;
            var maps = new List<LightmapSet.Map>(set.maps);
            var receivers = new List<LightmapSet.Receiver>(set.receivers);
            if (index < 0)
            {
                maps.Add(new LightmapSet.Map { color = map });
                index = maps.Count - 1;
            }
            receivers.RemoveAll(x => x != null && x.renderer == renderer);
            receivers.Add(new LightmapSet.Receiver { renderer = renderer, map = index, scaleOffset = scaleOffset });
            Fill(set, set.mode, maps, receivers, ctx);
            return true;
        }

        /// <summary>
        /// Check the open scenes' sets against the one-owner rule: a scene whose sets
        /// state different lightmap modes is refused (one scene reads one mode, an
        /// error), and a scene Unity's bake owns that holds sets is named (its sets stand
        /// down, a warning: <see cref="Apply"/> makes them its owner). A set whose
        /// box-projected probe sits rotated is named too (a warning): Unity's box
        /// projection and the probe's cubemap stay aligned to the world axes.
        /// </summary>
        /// <param name="ctx">The run's context, where each problem is raised; null: the Console.</param>
        /// <returns>How many problems it found.</returns>
        public static int Validate(OpContext ctx = null)
        {
            int problems = 0;
            foreach (Scene scene in OpenScenesList())
            {
                List<LightmapSet> sets = OpenSets().Where(s => s.gameObject.scene == scene).ToList();
                if (sets.Count == 0) continue;
                var modes = sets.Where(s => s.maps.Count > 0).Select(s => s.mode).Distinct().ToList();
                if (modes.Count > 1)
                {
                    problems++;
                    ctx.Raise(ModesDisagreeCode, $"'{scene.name}' holds lightmap sets in {modes.Count} modes " +
                                                 $"({string.Join(", ", sets.Where(s => s.maps.Count > 0).Select(s => $"{s.name}: {s.mode}"))}), " +
                                                 "and a scene reads its lightmaps in one: give them one mode (re-capture, or re-export).",
                              "error", Location(scene));
                }
                if (LightmapOwner.Baked(scene))
                {
                    problems++;
                    string bake = AssetDatabase.GetAssetPath(LightmapOwner.Bake(scene));
                    ctx.Raise(BakeOwnsSetsCode, $"Unity's bake owns '{scene.name}' (its Lighting Data Asset '{bake}'), so its " +
                                                $"{sets.Count} lightmap set(s) stand down ({string.Join(", ", sets.Select(s => s.name))}): " +
                                                "run Apply Scene Lightmaps to clear the bake and bind them, or remove them.",
                              "warning", Location(scene));
                }
                var rotated = sets.Where(s => s.probe != null && s.probe.boxProjection
                                              && Quaternion.Angle(s.probe.transform.rotation, Quaternion.identity) > 0.01f)
                                  .Select(s => s.name).ToList();
                if (rotated.Count > 0)
                {
                    problems++;
                    ctx.Raise(ProbeRotatedCode, $"Rotated model root(s) in '{scene.name}': {string.Join(", ", rotated)}. A reflection " +
                                                "probe's box and its cubemap stay aligned to the world axes, so the room's " +
                                                "reflections no longer line up with the room: keep a probed model unrotated " +
                                                "(moving and scaling it are fine).", "warning", Location(scene));
                }
            }
            return problems;
        }

        // -- helpers ------------------------------------------------------------------

        /// <summary>
        /// Every lightmap set in the open scenes, inactive ones included; none of a preview
        /// scene's (a prefab's stage), which no bake owns.
        /// </summary>
        static List<LightmapSet> OpenSets()
            => UnityEngine.Object.FindObjectsByType<LightmapSet>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .Where(s => s.gameObject.scene.IsValid() && s.gameObject.scene.isLoaded
                                      && !EditorSceneManager.IsPreviewScene(s.gameObject.scene)).ToList();

        static List<Scene> OpenScenesList()
        {
            var scenes = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene)) scenes.Add(scene);
            }
            return scenes;
        }

        static string Location(Scene scene) => string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;

        /// <summary>The textures of the bake whose Lighting Data Asset is at <paramref name="path"/>: what clearing it deletes.</summary>
        static List<string> BakeTextures(string path)
        {
            var textures = new List<string>();
            foreach (string file in AssetDatabase.GetDependencies(path, false))
                if (file != path && AssetDatabase.GetMainAssetTypeAtPath(file) is Type type && typeof(Texture).IsAssignableFrom(type))
                    textures.Add(file);
            return textures;
        }

        /// <summary>The model <paramref name="set"/> was imported with, where its object is an instance of one; else null.</summary>
        static string ModelOf(LightmapSet set)
        {
            LightmapSet source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(set);
            string path = source != null ? AssetDatabase.GetAssetPath(source) : null;
            return !string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter ? path : null;
        }

        /// <summary>Each texture file a set holds a map of, with the sets that hold it.</summary>
        static Dictionary<string, List<string>> Held(List<LightmapSet> sets)
        {
            var held = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (LightmapSet set in sets)
                foreach (LightmapSet.Map map in set.maps)
                    foreach (Texture2D texture in new[] { map?.color, map?.direction, map?.shadowMask })
                    {
                        string file = texture != null ? AssetDatabase.GetAssetPath(texture) : null;
                        if (string.IsNullOrEmpty(file)) continue;
                        if (!held.TryGetValue(file, out var names)) held[file] = names = new List<string>();
                        if (!names.Contains(set.name)) names.Add(set.name);
                    }
            return held;
        }

        // The import-settings mark of a map Capture copied out of a bake: the bake file's
        // dependency hash when it was copied, so a re-bake copies it again.
        const string CopyMark = "unitytk lightmap copy: ";

        /// <summary>The folder <paramref name="root"/>'s set keeps its copies of a bake's maps in, beside its scene.</summary>
        static string CopiesFolder(GameObject root)
        {
            Scene scene = root.scene;
            string dir = string.IsNullOrEmpty(scene.path) ? "Assets" : Path.GetDirectoryName(scene.path).Replace('\\', '/');
            string name = AssetFolders.SafeName(string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name);
            return $"{dir}/{name}_Lightmaps/{AssetFolders.SafeName(root.name)}";
        }

        /// <summary>Whether <paramref name="to"/> is a copy of the bake's file <paramref name="from"/> as it is now.</summary>
        static bool IsCopyOf(string to, string from)
            => File.Exists(to) && AssetImporter.GetAtPath(to) is AssetImporter importer
               && importer.userData == CopyMark + AssetDatabase.GetAssetDependencyHash(from);

        /// <summary>Copy the bake's map <paramref name="from"/> to <paramref name="to"/> (its import settings with it), marked; the copy.</summary>
        static Texture2D Copy(string from, string to)
        {
            string mark = CopyMark + AssetDatabase.GetAssetDependencyHash(from);
            if (File.Exists(to))
            {
                // A re-bake: the new bytes over the copy, which keeps its GUID (the sets holding it).
                File.Copy(from, to, true);
                AssetDatabase.ImportAsset(to, ImportAssetOptions.ForceSynchronousImport);
            }
            else
            {
                AssetFolders.Ensure(Path.GetDirectoryName(to));
                AssetDatabase.CopyAsset(from, to);
            }
            AssetImporter importer = AssetImporter.GetAtPath(to);
            if (importer != null && importer.userData != mark)
            {
                importer.userData = mark;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(to);
        }

        // What a texture of a scene's bake is, for the plan.
        static string What(string file, Scene scene)
        {
            foreach (LightmapData d in LightmapSettings.lightmaps ?? Array.Empty<LightmapData>())
            {
                if (d == null) continue;
                if (d.lightmapColor != null && AssetDatabase.GetAssetPath(d.lightmapColor) == file) return "a lightmap";
                if (d.lightmapDir != null && AssetDatabase.GetAssetPath(d.lightmapDir) == file) return "a lightmap's directions";
                if (d.shadowMask != null && AssetDatabase.GetAssetPath(d.shadowMask) == file) return "a shadow mask";
            }
            return typeof(Cubemap).IsAssignableFrom(AssetDatabase.GetMainAssetTypeAtPath(file)) ? "a baked reflection probe" : "a texture";
        }

        // How many of a set's receivers its binding reaches: a renderer and a map with a texture.
        static int Bindable(LightmapSet set)
            => set.receivers.Count(r => r != null && r.renderer != null && r.map >= 0 && r.map < set.maps.Count
                                        && set.maps[r.map] != null && set.maps[r.map].color != null);

        static void InScene(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (!root.scene.IsValid() || EditorUtility.IsPersistent(root))
                throw new ArgumentException($"'{root.name}' is not in a scene: a lightmap set lights a scene's renderers, so " +
                                            "place the prefab in a scene (or open it in Prefab Mode) and run it there", nameof(root));
        }

        // The root's set, added (asked first) where it has none; false where a dry run left it unmade.
        static bool Ensure(GameObject root, ref LightmapSet set, OpContext ctx)
        {
            if (set != null) return true;
            if (ctx != null && !ctx.ShouldCreate($"{root.name}/{nameof(LightmapSet)}", "add a Lightmap Set")) return false;
            set = ctx.AddComponent<LightmapSet>(root);
            return true;
        }

        // The set's lists replaced, recorded for the prefab instance and the scene, and bound again.
        static void Fill(LightmapSet set, LightmapsMode mode, List<LightmapSet.Map> maps, List<LightmapSet.Receiver> receivers, OpContext ctx)
        {
            set.Release();
            set.mode = mode;
            set.maps = maps;
            set.receivers = receivers;
            ctx.Edited(set);
            set.Bind();
        }

        static bool Same(List<LightmapSet.Map> a, List<LightmapSet.Map> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] == null || a[i].color != b[i].color || a[i].direction != b[i].direction || a[i].shadowMask != b[i].shadowMask)
                    return false;
            return true;
        }

        static bool Same(List<LightmapSet.Receiver> a, List<LightmapSet.Receiver> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] == null || a[i].renderer != b[i].renderer || a[i].map != b[i].map || a[i].scaleOffset != b[i].scaleOffset)
                    return false;
            return true;
        }
    }
}
