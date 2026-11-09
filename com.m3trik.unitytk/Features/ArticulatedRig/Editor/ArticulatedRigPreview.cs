using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The one keeper of what articulated rigs show outside play mode. There a rig gets no
    /// LateUpdate, so each inspector that shows one (<see cref="Open"/>: the rig's, or one of
    /// its joints') hands it here, and it shows while any of them is open: each editor update
    /// the parts its values move off their rest are posed, so dragging a value turns its part.
    /// A part is held -- its local transform taken as it is -- only while a value moves it (its
    /// own, or, for a part off the chain, a joint's above it), and given back when the values
    /// return to rest. A part the person moves while it is posed (the Move tool, an undo, a
    /// script) is theirs: the preview yields it, the move stays, until a value or the shape of
    /// its joint, or of one above it, moves again (Capture Rest From Transform makes the move the joint's rest).
    /// A rig that follows its animation is left to it (the Animation window and Timeline pose
    /// its parts), as is any part the Animation window animates. Every held part is given back
    /// as it was when the last inspector closes, the rig is removed, disabled or deactivated,
    /// before scripts reload and as play mode starts, and before a scene is saved or a prefab
    /// written -- after which the rig shows again while an inspector is still open. So nothing a
    /// preview posed reaches a saved scene or prefab; the values stay, and pose the rig in play.
    /// A rig in a prefab or model asset shows nothing (open the prefab, or place it in a scene).
    /// </summary>
    /// <remarks>
    /// The list is the registry the C# rules allow: empty after every domain reload (each rig is
    /// given back first), holding a rig only while it shows.
    ///
    /// Prior art: <c>[ExecuteAlways]</c> would run the rig's LateUpdate in edit mode in every
    /// open scene, posing rigs no inspector shows and writing their poses into saved scenes,
    /// so it was not taken. <c>AnimationMode</c> (StartAnimationMode, AddPropertyModification)
    /// is Unity's own preview that reverts what it changed, but it is one mode the Animation
    /// window and Timeline drive: an inspector starting or stopping it would end their preview,
    /// or have their recording key the preview's pose. So this keeper gives the parts back itself
    /// and stands aside where that mode animates a part. The skeleton (a registry, the release
    /// hooks, the prefab-save processor) is the one <c>ShadowPlanePreview</c> and
    /// <c>RenderEffectsPreview</c> follow; its only shared home would be the foundation, so each
    /// feature keeps its own.
    /// </remarks>
    [InitializeOnLoad]
    internal static class ArticulatedRigPreview
    {
        sealed class Part
        {
            public Transform transform;
            /// <summary>The preview poses it, and holds its transform as it was.</summary>
            public bool posing;
            public Vector3 heldPosition, posedPosition;
            public Quaternion heldRotation, posedRotation;
            /// <summary>The person moved it while posed: left as they left it until its joint's values or shape move.</summary>
            public bool yielded;
            public int yieldedAt;
        }

        sealed class Held
        {
            public ArticulatedRig rig;
            public Scene scene;
            /// <summary>How many inspectors show it (<see cref="Open"/>); 0 for a rig shown only by <see cref="Show"/>.</summary>
            public int open;
            public readonly List<Part> parts = new List<Part>();
        }

        static readonly List<Held> _held = new List<Held>();

        static ArticulatedRigPreview()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorSceneManager.sceneSaving += (scene, path) => GiveBackIn(scene);
            EditorSceneManager.sceneClosing += (scene, removing) => ReleaseIn(scene);
            PrefabStage.prefabSaving += prefab => GiveBackAll();
        }

        /// <summary>
        /// Before Unity writes a prefab -- one made from a scene object or an instance's
        /// overrides applied -- every held part is given back, so the asset holds the parts' own
        /// transforms, never the preview's pose.
        /// </summary>
        sealed class PrefabSaves : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                foreach (string path in paths)
                    if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                    {
                        GiveBackAll();
                        break;
                    }
                return paths;
            }
        }

        /// <summary>Whether <paramref name="rig"/> shows through the preview.</summary>
        internal static bool Showing(ArticulatedRig rig) => IndexOf(rig) >= 0;

        /// <summary>An inspector showing <paramref name="rigs"/> opened: each shows while any is open.</summary>
        internal static void Open(IEnumerable<ArticulatedRig> rigs)
        {
            foreach (ArticulatedRig rig in rigs)
            {
                Held held = Take(rig);
                if (held == null) continue;
                held.open++;
                Step(held);
            }
            SceneView.RepaintAll();
        }

        /// <summary>An inspector showing <paramref name="rigs"/> closed: the last one gives each back.</summary>
        internal static void Close(IEnumerable<ArticulatedRig> rigs)
        {
            foreach (ArticulatedRig rig in rigs)
            {
                int i = IndexOf(rig);
                if (i >= 0 && --_held[i].open <= 0) Drop(i);
            }
        }

        /// <summary>
        /// Show <paramref name="rigs"/> now (a value an inspector changed), each a loaded scene's:
        /// none in play mode, where each poses itself, nor one in a prefab asset or Prefab Mode's
        /// preview.
        /// </summary>
        internal static void Show(IEnumerable<ArticulatedRig> rigs)
        {
            bool shown = false;
            foreach (ArticulatedRig rig in rigs)
            {
                Held held = Take(rig);
                if (held == null) continue;
                Step(held);
                shown = true;
            }
            if (shown) SceneView.RepaintAll();
        }

        // The rig's entry, made where it shows; null where it shows nothing.
        static Held Take(ArticulatedRig rig)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || rig == null || !InScene(rig) || !Live(rig)) return null;
            int i = IndexOf(rig);
            if (i >= 0) return _held[i];
            var held = new Held { rig = rig, scene = rig.gameObject.scene };
            _held.Add(held);
            return held;
        }

        static Part PartOf(Held held, Transform t)
        {
            foreach (Part part in held.parts)
                if (part.transform == t) return part;
            var made = new Part { transform = t };
            held.parts.Add(made);
            return made;
        }

        // The parts a value moves held and posed; the rest left, or given back, as they stand.
        static void Step(Held held)
        {
            ArticulatedRig rig = held.rig;
            if (rig.followAnimation || !rig.IsValid)
            {
                GiveBack(held);
                return;
            }
            int count = rig.joints.Count;
            var moving = new bool[count];  // the part moves off where it stands at rest, posed or carried
            var leave = new bool[count];
            for (int j = 0; j < count; j++)
            {
                ArticulatedJoint joint = rig.joints[j];
                Part part = PartOf(held, joint.transform);
                Transform t = part.transform;
                int signature = Signature(joint);
                if (part.posing && (t.localPosition != part.posedPosition || t.localRotation != part.posedRotation))
                {
                    // The person moved it: theirs now. The move stays, and nothing gives it back.
                    part.posing = false;
                    part.yielded = true;
                    part.yieldedAt = signature;
                }
                if (part.yielded && signature != part.yieldedAt) part.yielded = false;
                int above = rig.IndexOf(joint.parent);
                bool carried = above >= 0 && above < j && moving[above];
                // Posed where a value of its own moves it, or (off the chain) where the joints above do;
                // a nested part is carried by its scene parent.
                bool moves = !part.yielded && !Animated(t)
                             && (OffRest(joint) || (carried && !ArticulatedRig.Nested(joint)));
                moving[j] = !part.yielded && (OffRest(joint) || carried);
                if (moves && !part.posing)
                {
                    part.heldPosition = t.localPosition;
                    part.heldRotation = t.localRotation;
                    part.posing = true;
                }
                else if (!moves && part.posing)
                {
                    Restore(part);
                }
                leave[j] = !part.posing;
            }
            rig.ApplyNow(leave);
            foreach (Part part in held.parts)
            {
                if (!part.posing || part.transform == null) continue;
                part.posedPosition = part.transform.localPosition;
                part.posedRotation = part.transform.localRotation;
            }
        }

        // Whether a value of joint's moves it off its rest.
        static bool OffRest(ArticulatedJoint joint)
        {
            foreach (ArticulatedJoint.Channel c in joint.channels)
                if (c != null && joint.GetValue(c.axis) != 0f) return true;
            return false;
        }

        // What a yield waits on: the values and shape of the joint and those above it.
        static int Signature(ArticulatedJoint joint)
        {
            unchecked
            {
                int v = 17;
                for (int up = 0; joint != null && up < 64; joint = joint.parent, up++)
                {
                    v = v * 31 + joint.Version;
                    v = v * 31 + joint.rx.GetHashCode();
                    v = v * 31 + joint.ry.GetHashCode();
                    v = v * 31 + joint.rz.GetHashCode();
                    v = v * 31 + joint.tx.GetHashCode();
                    v = v * 31 + joint.ty.GetHashCode();
                    v = v * 31 + joint.tz.GetHashCode();
                }
                return v;
            }
        }

        // A part the Animation window (or Timeline) animates: its pose is that mode's.
        static bool Animated(Transform t)
            => AnimationMode.InAnimationMode()
               && (AnimationMode.IsPropertyAnimated(t, "m_LocalRotation.x") || AnimationMode.IsPropertyAnimated(t, "m_LocalPosition.x"));

        // A held part's transform as it was.
        static void Restore(Part part)
        {
            if (part.posing && part.transform != null)
            {
                part.transform.localPosition = part.heldPosition;
                part.transform.localRotation = part.heldRotation;
            }
            part.posing = false;
        }

        // Every held part of the rig given back; it still shows (the next update poses it again).
        static void GiveBack(Held held)
        {
            foreach (Part part in held.parts) Restore(part);
        }

        /// <summary>Give back every rig it shows.</summary>
        internal static void ReleaseAll()
        {
            for (int i = _held.Count - 1; i >= 0; i--) Drop(i);
        }

        static void GiveBackAll()
        {
            foreach (Held held in _held) GiveBack(held);
        }

        static int IndexOf(ArticulatedRig rig)
        {
            for (int i = 0; i < _held.Count; i++)
                if (ReferenceEquals(_held[i].rig, rig)) return i;
            return -1;
        }

        static void Drop(int i)
        {
            Held held = _held[i];
            _held.RemoveAt(i);
            GiveBack(held);
            SceneView.RepaintAll();
        }

        /// <summary>Each editor update: every shown rig posed again (after a save gave it back too).</summary>
        internal static void Update()
        {
            if (_held.Count == 0) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                ArticulatedRig rig = _held[i].rig;
                if (rig == null || !Live(rig)) Drop(i);
                else Step(_held[i]);
            }
        }

        internal static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) ReleaseAll();
        }

        // Every held rig of scene, or on none any more: the scene is going.
        static void ReleaseIn(Scene scene)
        {
            for (int i = _held.Count - 1; i >= 0; i--)
                if (_held[i].rig == null || _held[i].scene == scene) Drop(i);
        }

        // Every held part of scene's rigs given back before it is written; they show again after.
        static void GiveBackIn(Scene scene)
        {
            foreach (Held held in _held)
                if (held.rig == null || held.scene == scene) GiveBack(held);
        }

        // Enabled on an active object (outside play mode, Unity sends it no OnEnable).
        static bool Live(ArticulatedRig rig) => rig.enabled && rig.gameObject.activeInHierarchy;

        // A loaded scene's object, outside any prefab asset or Prefab Mode's preview.
        static bool InScene(ArticulatedRig rig)
            => rig.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewSceneObject(rig.gameObject)
               && !EditorUtility.IsPersistent(rig);
    }
}
