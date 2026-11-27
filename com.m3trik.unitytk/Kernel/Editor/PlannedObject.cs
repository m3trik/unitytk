using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// An object a dry run only planned, standing in for it where an action takes the object: it is not
    /// there to hand over, so an action given one plans on it what its live run makes, under the keys its
    /// live run asks with (<see cref="OpContextEdits.Key(PlannedObject, Type)"/>,
    /// <see cref="OpContextEdits.PlanCreate"/>), and returns nothing. A chained dry run then plans what its
    /// live run makes, and no caller restates an action's keys.
    /// </summary>
    /// <remarks>
    /// Named as the live run names the object once it is made: by its <see cref="Path"/>, as
    /// <see cref="OpContextEdits.Label(GameObject)"/> names a made one, so two same-named objects under
    /// different parents, planned or made, never share a key.
    /// </remarks>
    public sealed class PlannedObject
    {
        /// <param name="name">The name the live run gives it.</param>
        /// <param name="parent">The object it is made under (null: a top-level object).</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        public PlannedObject(string name, Transform parent = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name what is planned", nameof(name));
            Name = name;
            Parent = parent;
        }

        /// <param name="name">The name the live run gives it.</param>
        /// <param name="scene">The scene it is made at the top of (an invalid one: the active scene).</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        public PlannedObject(string name, Scene scene)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name what is planned", nameof(name));
            Name = name;
            _scene = scene;
        }

        /// <param name="name">The name the live run gives it.</param>
        /// <param name="parent">The planned object it is made under.</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="parent"/> is null.</exception>
        public PlannedObject(string name, PlannedObject parent)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name what is planned", nameof(name));
            Name = name;
            PlannedParent = parent ?? throw new ArgumentNullException(nameof(parent));
        }

        /// <summary>The name the live run gives it.</summary>
        public string Name { get; }

        /// <summary>The object it is made under; null under a planned one, or at the top of its scene.</summary>
        public Transform Parent { get; }

        /// <summary>The planned object it is made under; null under an object there now, or at the top.</summary>
        public PlannedObject PlannedParent { get; }

        readonly Scene _scene;

        /// <summary>The scene it is made in: its parent's, else the one it is planned at the top of, else the active one.</summary>
        public Scene Scene => PlannedParent != null ? PlannedParent.Scene
                            : Parent != null ? Parent.gameObject.scene
                            : _scene.IsValid() ? _scene : SceneManager.GetActiveScene();

        /// <summary>
        /// Its path from its scene's root once made, its parents' names down to its own: how its live run's
        /// plan names it (<see cref="OpContextEdits.Label(GameObject)"/>).
        /// </summary>
        public string Path
        {
            get
            {
                string above = PlannedParent != null ? PlannedParent.Path
                             : Parent != null ? OpContextEdits.Label(Parent.gameObject) : null;
                return string.IsNullOrEmpty(above) ? Name : $"{above}/{Name}";
            }
        }

        /// <inheritdoc/>
        public override string ToString() => Path;
    }
}
