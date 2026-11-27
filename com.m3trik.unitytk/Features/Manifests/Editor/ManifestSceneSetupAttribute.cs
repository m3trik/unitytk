using System;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// Marks a setup of a manifest's fresh scene (a camera rig, the look its delivery asks for): a static
    /// method <c>void Setup(Manifest manifest, ManifestScene scene, OpContext ctx)</c> that
    /// <see cref="ManifestsActions.Reset"/> runs once the plan is built in a new scene, for a manifest its
    /// kit takes part in (<see cref="Kit"/>). A build in place keeps what it made, so it runs only on a reset.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ManifestSceneSetupAttribute : Attribute
    {
        /// <summary>A setup of the manifests the kit compiled beside it takes part in (where none is: of every manifest).</summary>
        public ManifestSceneSetupAttribute()
        {
        }

        /// <summary>A setup of the manifests <paramref name="kit"/> takes part in.</summary>
        /// <param name="kit">An <see cref="IManifestKit"/> type.</param>
        public ManifestSceneSetupAttribute(Type kit)
        {
            Kit = kit;
        }

        /// <summary>
        /// The kit (an <see cref="IManifestKit"/> type) whose manifests it sets up: it runs on a reset of a
        /// manifest that kit takes part in (<see cref="ManifestBehaviors.Kits"/>). Null: the kit compiled in
        /// its own assembly; a setup in an assembly with no kit sets up every manifest.
        /// </summary>
        public Type Kit { get; }
    }
}
