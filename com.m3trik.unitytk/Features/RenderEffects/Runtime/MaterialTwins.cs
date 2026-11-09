using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// The twins made in the editor for a set of render effects: per material and effect
    /// state, the template its effects instance while active (<see cref="MaterialWriter.Twin"/>).
    /// Referenced by the effects, it carries the templates into a player build, whose
    /// shader stripping then keeps the variants they draw with. An import adds one to the
    /// model; <c>RenderEffectsActions.PrepareMaterials</c> makes one beside a scene's.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity keeps a shader variant in a build only where a material in it uses
    /// the variant (or a <c>ShaderVariantCollection</c> lists it); a referenced material per
    /// state is the first, with no list to keep in step.
    /// </remarks>
    public sealed class MaterialTwins : ScriptableObject
    {
        /// <summary>One material's twin for one state.</summary>
        [Serializable]
        public struct Twin
        {
            /// <summary>The material as authored.</summary>
            public Material original;
            /// <summary>What the effect asks of it.</summary>
            public TwinState state;
            /// <summary>The twin made for that state, which the effect instances.</summary>
            public Material template;
        }

        [Tooltip("Every twin made for these effects, by material and state.")]
        public List<Twin> twins = new List<Twin>();

        /// <summary>The template made for <paramref name="original"/> in <paramref name="state"/>, or null.</summary>
        public Material Template(Material original, TwinState state)
        {
            state = MaterialWriter.Normalized(state);
            foreach (Twin twin in twins)
                if (twin.original == original && twin.state == state && twin.template != null)
                    return twin.template;
            return null;
        }
    }
}
