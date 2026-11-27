using System;
using System.Collections.Generic;

namespace M3trik.Unitytk.Contracts
{
    /// <summary>
    /// Where a scene's commands go and its status is said: a debug panel in the world, a screen overlay, a menu. What
    /// puts commands there reads one off a component (<c>GetComponent&lt;ICommandSurface&gt;()</c>), or code sets one;
    /// with none, the commands are still called by code.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's Rendering Debugger holds a pipeline's switches, not a scene's commands, and an asset-store
    /// console is a screen overlay and a dependency; this contract lets commands go on whatever surface a scene shows
    /// without naming it.
    /// </remarks>
    public interface ICommandSurface
    {
        /// <summary>Put a command labelled <paramref name="label"/> on it that runs <paramref name="run"/>; one <paramref name="owner"/> put there under that label is replaced.</summary>
        /// <param name="owner">What the command belongs to (it takes its commands away with <see cref="Remove"/>).</param>
        /// <param name="label">The command's label.</param>
        /// <param name="run">What running it does.</param>
        void Add(UnityEngine.Object owner, string label, Action run);

        /// <summary>Take away every command <paramref name="owner"/> put on it.</summary>
        /// <param name="owner">What the commands belong to.</param>
        void Remove(UnityEngine.Object owner);

        /// <summary>Say <paramref name="text"/> as its status (the step a sequence is on).</summary>
        /// <param name="text">The status; null: none.</param>
        void SetStatus(string text);
    }

    /// <summary>
    /// The command surface's stand-in: one a test reads back what it was given by (its commands by label, in the order
    /// put on, and its status), and runs a command of, as a person pressing its button would.
    /// </summary>
    public sealed class PureCommandSurface : ICommandSurface
    {
        readonly List<(UnityEngine.Object owner, string label, Action run)> _commands = new List<(UnityEngine.Object, string, Action)>();

        /// <summary>What its status says.</summary>
        public string Status { get; private set; } = "";

        /// <summary>The labels of its commands, in the order they were put on it.</summary>
        public IReadOnlyList<string> Commands => _commands.ConvertAll(command => command.label);

        /// <inheritdoc/>
        public void Add(UnityEngine.Object owner, string label, Action run)
        {
            int at = _commands.FindIndex(command => command.owner == owner && command.label == label);
            if (at >= 0) _commands[at] = (owner, label, run);
            else _commands.Add((owner, label, run));
        }

        /// <inheritdoc/>
        public void Remove(UnityEngine.Object owner) => _commands.RemoveAll(command => command.owner == owner);

        /// <inheritdoc/>
        public void SetStatus(string text) => Status = text ?? "";

        /// <summary>Run the command labelled <paramref name="label"/>; whether there is one.</summary>
        /// <param name="label">The command's label.</param>
        public bool Run(string label)
        {
            foreach (var command in _commands)
                if (command.label == label)
                {
                    command.run();
                    return true;
                }
            return false;
        }
    }
}
