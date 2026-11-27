using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using UnityEngine;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>
    /// A second test-only kit, beside the feature's own (<c>TestKit</c>): its behaviors are this assembly's, and
    /// around their rows it makes one top-level object named by its own data in the plan (its extension,
    /// <c>tool_test</c>: <c>holder</c>), which no other kit reads.
    /// </summary>
    public sealed class ToolTestKit : IManifestKit
    {
        /// <summary>Its name in a plan's extensions.</summary>
        public const string Extension = "tool_test";

        /// <summary>The holder's name where its extension names none.</summary>
        public const string Holder = "Tool Test Holder";

        /// <inheritdoc/>
        public Type Settings => typeof(ToolTestSettings);

        /// <summary>The holder <paramref name="scene"/>'s plan names (its <see cref="Extension"/>'s <c>holder</c>), else <see cref="Holder"/>.</summary>
        /// <param name="scene">The delivery the plan is built into.</param>
        public static string HolderOf(ManifestScene scene)
            => scene.Plan.Extension(Extension) is IDictionary<string, object> own && own.TryGetValue("holder", out object name) && name is string text
                ? text : Holder;

        /// <inheritdoc/>
        public void Begin(ManifestScene scene, OpContext ctx)
        {
            string name = HolderOf(scene);
            GameObject holder = SceneRoots.Find(name);
            if (holder == null && !ctx.Planned("create", name) && (ctx == null || ctx.ShouldCreate(name, "hold the kit's parts")))
            {
                holder = new GameObject(name);
                ctx.Created(holder);
            }
            scene.Hold(holder);
        }

        /// <inheritdoc/>
        public void End(ManifestScene scene, OpContext ctx)
        {
        }

        /// <inheritdoc/>
        public void Keep(Manifest manifest, ManifestScene scene, OpContext ctx)
        {
        }

        /// <inheritdoc/>
        public void Forget(Manifest manifest, OpContext ctx)
        {
        }

        /// <inheritdoc/>
        public System.Collections.Generic.IEnumerable<ManifestAdjustment> Kept(Manifest manifest, ManifestScene scene) => new ManifestAdjustment[0];

        /// <inheritdoc/>
        public int Revert(Manifest manifest, ManifestScene scene, UnityEngine.Component part, OpContext ctx) => 0;

        public int PlaceAgain(Manifest manifest, ManifestScene scene, UnityEngine.Component part, OpContext ctx) => 0;
    }
}
