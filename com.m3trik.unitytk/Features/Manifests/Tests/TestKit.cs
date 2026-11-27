using System;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Manifests.Tests
{
    /// <summary>
    /// A test-only kit: around the rows of this assembly's behaviors it builds one top-level object
    /// (<see cref="Root"/>, or the name its own data in the plan gives: its extension <c>test</c>'s <c>root</c>;
    /// what the steps share) and a child a step (each step's own part), and counts what it is asked to keep and
    /// forget. What a kit does around its rows, without a feature.
    /// </summary>
    public sealed class TestKit : IManifestKit
    {
        /// <summary>The name of the object holding a child a step.</summary>
        public const string Root = "Test Steps";

        /// <summary>How many times a build asked it to keep a person's adjustments, and to forget them (a test's own count).</summary>
        public static int Kept, Forgotten;

        /// <inheritdoc/>
        public Type Settings => typeof(TestSettings);

        /// <summary>The top-level object's name <paramref name="scene"/>'s plan gives (its extension <c>test</c>'s <c>root</c>), else <see cref="Root"/>.</summary>
        /// <param name="scene">The delivery the plan is built into.</param>
        public static string RootOf(ManifestScene scene)
            => scene.Plan.Extension("test") is System.Collections.Generic.IDictionary<string, object> own && own.TryGetValue("root", out object name)
               && name is string text ? text : Root;

        /// <inheritdoc/>
        public void Begin(ManifestScene scene, OpContext ctx)
        {
            string name = RootOf(scene);
            GameObject root = SceneRoots.Find(name);
            if (root == null && !ctx.Planned("create", name) && (ctx == null || ctx.ShouldCreate(name, "hold a child a step")))
            {
                root = new GameObject(name);
                ctx.Created(root);
            }
            scene.Hold(root);
            foreach (var step in scene.Plan.steps)
            {
                if (!scene.Builds(step.id)) continue;
                using (scene.For(step.id))
                {
                    Transform own = root == null ? null : root.transform.Find(step.id);
                    if (own == null)
                    {
                        if (ctx != null && !ctx.ShouldCreate($"{name}/{step.id}", "the step's own object")) continue;
                        own = new GameObject(step.id).transform;
                        own.SetParent(root.transform, false);
                        ctx.Created(own.gameObject);
                    }
                    scene.Hold(own.gameObject);
                }
            }
        }

        /// <inheritdoc/>
        public void End(ManifestScene scene, OpContext ctx)
        {
        }

        /// <inheritdoc/>
        public void Keep(Manifest manifest, ManifestScene scene, OpContext ctx) => Kept++;

        /// <inheritdoc/>
        public void Forget(Manifest manifest, OpContext ctx) => Forgotten++;

        /// <inheritdoc/>
        System.Collections.Generic.IEnumerable<ManifestAdjustment> IManifestKit.Kept(Manifest manifest, ManifestScene scene) => new ManifestAdjustment[0];  // (the count above has the name)

        /// <inheritdoc/>
        public int Revert(Manifest manifest, ManifestScene scene, UnityEngine.Component part, OpContext ctx) => 0;

        public int PlaceAgain(Manifest manifest, ManifestScene scene, UnityEngine.Component part, OpContext ctx) => 0;
    }
}
