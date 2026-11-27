using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using M3trik.Unitytk.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// One run's context, pythontk's <c>OpContext</c> spelled for C#: an action
    /// asks it before each change (<see cref="ShouldChange"/>, <see cref="ShouldCreate"/>,
    /// <see cref="ShouldDelete"/>, <see cref="ShouldWrite"/>), which records the change
    /// -- the plan in a dry run, the journal in a live one -- and says whether to make
    /// it; each ask is also a cancel checkpoint. Problems go to <see cref="Issue"/>
    /// (an action raises them through <see cref="OpContextEdits.Raise"/>, which a null
    /// context writes to the Console), progress to <see cref="Progress"/>. A live ask before changing a Unity object
    /// records it for undo, so an action never calls <c>Undo</c> itself; a menu item
    /// runs an action through <see cref="RunMenu{T}"/>.
    /// </summary>
    /// <remarks>
    /// Prior art: PowerShell's <c>ShouldProcess</c> (ask before each change; list it
    /// instead in a dry run, the mechanism behind <c>-WhatIf</c>) and pythontk's
    /// <c>OpContext</c>, whose C# spelling this is. Unity has nothing that gives an
    /// ordinary method a dry run, undo grouping and a run journal.
    /// </remarks>
    public sealed class OpContext
    {
        readonly Func<bool> _cancelled;
        readonly Action<double, string> _progress;

        /// <param name="dryRun">Whether this run only plans.</param>
        /// <param name="cancelled">Whether the run was cancelled (polled at each checkpoint).</param>
        /// <param name="progress">Where a progress report goes.</param>
        public OpContext(bool dryRun = false, Func<bool> cancelled = null, Action<double, string> progress = null)
        {
            DryRun = dryRun;
            _cancelled = cancelled;
            _progress = progress;
        }

        /// <summary>Whether this run only plans: every change is listed, none made.</summary>
        public bool DryRun { get; }

        /// <summary>Every change asked for, in order: the plan in a dry run, the journal in a live one.</summary>
        public List<Change> Changes { get; } = new List<Change>();

        /// <summary>The problems found, in the order found.</summary>
        public List<Issue> Issues { get; } = new List<Issue>();

        /// <summary>
        /// Ask before changing <paramref name="target"/>; true: make the change. In a live
        /// run a Unity object is recorded for undo first.
        /// </summary>
        public bool ShouldChange(object target, string action = "")
        {
            if (!Ask("change", target, action)) return false;
            if (target is UnityEngine.Object unityObject && unityObject != null)
                Undo.RecordObject(unityObject, UndoName(action));
            return true;
        }

        /// <summary>
        /// Ask before creating <paramref name="target"/> (what is made, by name); true:
        /// create it, then hand the new object to <see cref="OpContextEdits.Created"/>.
        /// </summary>
        public bool ShouldCreate(object target, string action = "") => Ask("create", target, action);

        /// <summary>
        /// Ask before deleting <paramref name="target"/>; true: delete it through
        /// <see cref="OpContextEdits.Destroy"/>, which records the delete for undo.
        /// </summary>
        public bool ShouldDelete(object target, string action = "") => Ask("delete", target, action);

        /// <summary>Ask before writing the file at <paramref name="path"/>; true: write it.</summary>
        public bool ShouldWrite(string path, string action = "") => Ask("write", path, action);

        /// <summary>Report a problem found; it rides on the run's outcome.</summary>
        /// <param name="code">The issue's stable code, declared beside the action.</param>
        /// <param name="message">What is wrong and what to do.</param>
        /// <param name="severity"><c>error</c> (the default here; <see cref="OpContextEdits.Raise"/>'s is
        /// <c>warning</c>), <c>warning</c> or <c>info</c>.</param>
        /// <param name="location">Where (an asset path, an object), when it has one.</param>
        /// <param name="fix">The call that fixes it, when there is one.</param>
        public Issue Issue(string code, string message, string severity = "error",
                           string location = null, OpCall fix = null)
        {
            var issue = new Issue { code = code, message = message, severity = severity, location = location, fix = fix };
            Issues.Add(issue);
            return issue;
        }

        /// <summary>Report how far the run is (0 to 1); also a cancel checkpoint.</summary>
        public void Progress(double fraction, string message = "")
        {
            Check();
            _progress?.Invoke(Math.Max(0, Math.Min(1, fraction)), message ?? "");
        }

        /// <summary>A cancel checkpoint.</summary>
        /// <exception cref="OperationCancelledException">The run was cancelled.</exception>
        public void Check()
        {
            if (_cancelled != null && _cancelled())
                throw new OperationCancelledException("cancelled");
        }

        /// <summary>The undo step's name, the menu's title when a menu runs it.</summary>
        internal string Title { get; set; }

        internal string UndoName(string action) => string.IsNullOrEmpty(Title) ? (string.IsNullOrEmpty(action) ? "unitytk" : action) : Title;

        // -- a menu's run ------------------------------------------------------------

        /// <summary>
        /// Run <paramref name="run"/> as a person's menu item or window button runs an
        /// action: one undo group named <paramref name="title"/>, a progress bar with
        /// Cancel, and its issues in the Console. A cancel undoes its scene edits and
        /// returns the default; a failure undoes them and rethrows. Files it wrote stay,
        /// and the Console names them.
        /// </summary>
        /// <param name="title">What the person chose, as the undo step and the progress bar name it.</param>
        /// <param name="run">The action's call, given the context to pass it.</param>
        /// <returns>What the action returned.</returns>
        public static T RunMenu<T>(string title, Func<OpContext, T> run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            bool cancelled = false, shown = false;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(title);
            var ctx = new OpContext(false, () => cancelled, (fraction, message) =>
            {
                shown = true;
                cancelled = EditorUtility.DisplayCancelableProgressBar(title, message, (float)fraction);
            })
            { Title = title };
            try
            {
                T result = run(ctx);
                Undo.CollapseUndoOperations(group);
                Report(title, ctx, null);
                return result;
            }
            catch (OperationCancelledException)
            {
                Undo.RevertAllDownToGroup(group);
                Report(title, ctx, "cancelled; its scene edits are undone");
                return default;
            }
            catch (Exception)
            {
                Undo.RevertAllDownToGroup(group);
                Report(title, ctx, "failed; its scene edits are undone");
                throw;
            }
            finally
            {
                if (shown) EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Run <paramref name="run"/> as a person's menu item runs an action that
        /// returns nothing (<see cref="RunMenu{T}"/>).
        /// </summary>
        public static void RunMenu(string title, Action<OpContext> run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            RunMenu<object>(title, ctx =>
            {
                run(ctx);
                return null;
            });
        }

        static void Report(string title, OpContext ctx, string ended)
        {
            foreach (Issue issue in ctx.Issues) Write(title, issue);
            if (ended == null) return;
            string[] written = ctx.Changes.Where(c => c.kind == "write").Select(c => c.target).ToArray();
            Debug.LogWarning(written.Length == 0
                ? $"{title}: {ended}."
                : $"{title}: {ended}; the {written.Length} file(s) it wrote stay: {string.Join(", ", written)}.");
        }

        /// <summary>
        /// Write <paramref name="issue"/> to the Console by its severity, as
        /// <c>&lt;title&gt;: &lt;message&gt; (&lt;location&gt;)</c>, the line pinging the asset it names.
        /// </summary>
        internal static void Write(string title, Issue issue)
        {
            string text = $"{title}: {issue.message}" + (string.IsNullOrEmpty(issue.location) ? "" : $" ({issue.location})");
            UnityEngine.Object where = Locate(issue.location);
            if (issue.severity == "error") Debug.LogError(text, where);
            else if (issue.severity == "warning") Debug.LogWarning(text, where);
            else Debug.Log(text, where);
        }

        static UnityEngine.Object Locate(string location)
            => string.IsNullOrEmpty(location) || !location.StartsWith("Assets/", StringComparison.Ordinal)
                ? null
                : AssetDatabase.LoadMainAssetAtPath(location);

        // -----------------------------------------------------------------------------

        bool Ask(string kind, object target, string action)
        {
            Check();
            Changes.Add(new Change { kind = kind, target = NameOf(target), action = action ?? "" });
            return !DryRun;
        }

        // What a change names target by: a scene object, or a component by its object, as OpContextEdits.Label
        // does (its path), so two same-named objects' changes never read as one; another Unity object by its
        // name; anything else as it prints.
        internal static string NameOf(object target)
        {
            if (target is UnityEngine.Object unityObject)
                return unityObject == null ? "(destroyed)"
                     : unityObject is GameObject node ? OpContextEdits.Label(node)
                     : unityObject is Component part ? OpContextEdits.Label(part.gameObject)
                     : unityObject.name;
            return Convert.ToString(target, CultureInfo.InvariantCulture) ?? "";
        }
    }

    /// <summary>
    /// The edits an action makes after its context said yes, recorded for undo when a
    /// context runs it: extension methods, so they work on a null context too (an
    /// action called with none just edits, as Python's <c>ctx=None</c> does).
    /// </summary>
    public static class OpContextEdits
    {
        /// <summary>Hand over an object the action just created, so undo removes it.</summary>
        public static void Created(this OpContext ctx, UnityEngine.Object created)
        {
            if (ctx != null && created != null) Undo.RegisterCreatedObjectUndo(created, ctx.UndoName("create"));
        }

        /// <summary>
        /// Delete <paramref name="target"/>, so undo restores it when a context runs the
        /// action; with none, its scene is marked changed, so it saves the delete.
        /// </summary>
        public static void Destroy(this OpContext ctx, UnityEngine.Object target)
        {
            if (target == null) return;
            if (ctx != null)
            {
                Undo.DestroyObjectImmediate(target);
                return;
            }
            Scene scene = target is GameObject go ? go.scene : target is Component component ? component.gameObject.scene : default;
            UnityEngine.Object.DestroyImmediate(target);
            Changed(scene);
        }

        /// <summary>
        /// Add a <typeparamref name="T"/> to <paramref name="gameObject"/>, so undo removes
        /// it when a context runs the action; with none, its scene is marked changed, so it
        /// saves the component.
        /// </summary>
        public static T AddComponent<T>(this OpContext ctx, GameObject gameObject) where T : Component
        {
            if (ctx != null) return Undo.AddComponent<T>(gameObject);
            T added = gameObject.AddComponent<T>();
            Changed(gameObject.scene);
            return added;
        }

        /// <summary>
        /// Mark <paramref name="edited"/> changed after the action edited it: recorded as a
        /// prefab instance's override where it is one, and marked for saving; with no
        /// context its scene is marked changed too, which a context's ask (its undo
        /// record) does when one runs the action.
        /// </summary>
        public static void Edited(this OpContext ctx, UnityEngine.Object edited)
        {
            if (edited == null) return;
            PrefabUtility.RecordPrefabInstancePropertyModifications(edited);
            EditorUtility.SetDirty(edited);
            if (ctx != null) return;
            Changed(edited is GameObject go ? go.scene : edited is Component component ? component.gameObject.scene : default);
        }

        // A scene an edit with no context changed: undo marks it when a context runs.
        static void Changed(Scene scene)
        {
            if (scene.IsValid() && !EditorApplication.isPlaying) EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Say what the run did: a line in the Console, which a call from outside the
        /// editor (<c>unity.csharp.exec</c>) returns as its output.
        /// </summary>
        public static void Log(this OpContext ctx, string message) => Debug.Log(message);

        /// <summary>
        /// Whether this is a dry run that already planned a <paramref name="kind"/> on
        /// <paramref name="target"/> (and <paramref name="action"/>, when given). A part a dry run
        /// only planned is not there to find, so an action that would make or change it again asks
        /// this and treats it as made: a chained dry run then plans what its live run makes. False
        /// in a live run and with no context.
        /// </summary>
        /// <param name="kind"><c>create</c>, <c>change</c>, <c>delete</c> or <c>write</c>, as the asks record it.</param>
        /// <param name="target">What was asked about, named as the asks name it (a scene object or a component by its
        /// object's <see cref="Label(GameObject)"/>, another Unity object by its name).</param>
        /// <param name="action">The ask's action; null matches any.</param>
        public static bool Planned(this OpContext ctx, string kind, object target, string action = null)
        {
            if (ctx == null || !ctx.DryRun) return false;
            string name = OpContext.NameOf(target);
            foreach (Change change in ctx.Changes)
                if (change.kind == kind && change.target == name && (action == null || change.action == action)) return true;
            return false;
        }

        /// <summary>
        /// The name an action asks about <paramref name="node"/>'s <paramref name="part"/> by:
        /// <c>&lt;node&gt;/&lt;Part&gt;</c>, the node by its path (<see cref="Label(GameObject)"/>), so a dry run
        /// never takes two same-named objects' parts for one. What an action passes
        /// <see cref="OpContext.ShouldCreate"/> and <see cref="Planned"/> for a part it adds; a caller
        /// that has only planned the node hands the action a <see cref="PlannedObject"/> (its own
        /// <see cref="Key(PlannedObject, Type)"/>, the same key), never a key of its own making.
        /// </summary>
        /// <param name="node">The object the part goes on.</param>
        /// <param name="part">The part's type (a component).</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="part"/> is null.</exception>
        public static string Key(GameObject node, Type part)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            return $"{Label(node)}/{part.Name}";
        }

        /// <summary>
        /// The name an action asks about a part of <paramref name="node"/>, an object a dry run only planned,
        /// by: the key its live run asks with once the object is made (<see cref="PlannedObject.Path"/>).
        /// </summary>
        /// <param name="node">The planned object the part goes on.</param>
        /// <param name="part">The part's type (a component).</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="part"/> is null.</exception>
        public static string Key(PlannedObject node, Type part)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (part == null) throw new ArgumentNullException(nameof(part));
            return $"{node.Path}/{part.Name}";
        }

        /// <summary>
        /// <paramref name="node"/> as an action's plan names it: its path from its scene's root
        /// (<c>Root/Child/Node</c>; a top-level object's is its name). It depends on the node alone, so a dry
        /// run names an object it only planned (<see cref="PlannedObject.Path"/>) as its live run names the made one.
        /// </summary>
        /// <param name="node">A scene object (one in no scene, a prefab asset's, by its path from the asset's root).</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static string Label(GameObject node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            return AnimationUtility.CalculateTransformPath(node.transform, null);
        }

        /// <summary>
        /// Plan, once, the creation of <paramref name="key"/> in a dry run: what an action handed a
        /// <see cref="PlannedObject"/> does in place of making the part (it has nothing to make it on), so
        /// the dry run plans what its live run makes. Nothing in a live run or with no context.
        /// </summary>
        /// <param name="key">What would be made, as the live run asks for it (<see cref="Key(PlannedObject, Type)"/>).</param>
        /// <param name="action">What making it does, as the live run says it.</param>
        /// <returns>Whether it planned it now (false: planned already, a live run, or no context).</returns>
        public static bool PlanCreate(this OpContext ctx, string key, string action)
        {
            if (ctx == null || !ctx.DryRun || ctx.Planned("create", key)) return false;
            ctx.ShouldCreate(key, action);
            return true;
        }

        /// <summary>
        /// Plan, once, the change <paramref name="action"/> of <paramref name="target"/> in a dry run: what an action handed a
        /// <see cref="PlannedObject"/> does in place of the change (there is nothing yet to change), so the dry run plans what
        /// its live run changes. Nothing in a live run or with no context.
        /// </summary>
        /// <param name="target">What would change, as the live run names it (a planned object's <see cref="PlannedObject.Path"/>,
        /// or an object there).</param>
        /// <param name="action">What the change does, as the live run says it.</param>
        /// <returns>Whether it planned it now (false: planned already, a live run, or no context).</returns>
        public static bool PlanChange(this OpContext ctx, object target, string action)
        {
            if (ctx == null || !ctx.DryRun || ctx.Planned("change", target, action)) return false;
            ctx.ShouldChange(target, action);
            return true;
        }

        /// <summary>The title a problem's Console line carries when no menu ran the action.</summary>
        internal const string ConsoleTitle = "unitytk";

        /// <summary>
        /// Report a problem found: with a context, an issue on it (<see cref="OpContext.Issue"/>),
        /// which rides on the run's outcome and a menu's run writes to the Console; with none,
        /// that Console line itself, in the same words (titled <see cref="ConsoleTitle"/>), so an
        /// action a script or <c>unity.csharp.exec</c> calls with no context still says what it
        /// found. Not <c>ctx?.Issue(...)</c>, which drops the problem when there is no context.
        /// </summary>
        /// <param name="code">The issue's stable code, declared beside the action.</param>
        /// <param name="message">What is wrong and what to do.</param>
        /// <param name="severity"><c>error</c>, <c>warning</c> (the default here, where
        /// <see cref="OpContext.Issue"/>'s is <c>error</c>: pass <c>error</c> for one that is) or <c>info</c>.</param>
        /// <param name="location">Where (an asset path, an object), when it has one.</param>
        /// <param name="fix">The call that fixes it, when there is one.</param>
        public static void Raise(this OpContext ctx, string code, string message, string severity = "warning",
                                 string location = null, OpCall fix = null)
        {
            if (ctx != null)
            {
                ctx.Issue(code, message, severity, location, fix);
                return;
            }
            OpContext.Write(ConsoleTitle, new Issue { code = code, message = message, severity = severity, location = location, fix = fix });
        }
    }
}
