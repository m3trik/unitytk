using System;
using System.Collections.Generic;

namespace M3trik.Unitytk.Core
{
    /// <summary>No scenes (pythontk's <c>PureSceneFiles</c>): a request that names one is refused.</summary>
    public sealed class PureSceneFiles : ISceneFiles
    {
        /// <inheritdoc/>
        public void Open(IList<string> paths)
        {
            if (paths != null && paths.Count > 0)
                throw new ArgumentException($"this host opens no scenes (the request names {paths.Count})");
        }

        /// <inheritdoc/>
        public IList<string> Modified() => new List<string>();

        /// <inheritdoc/>
        public bool Unsaved() => false;

        /// <inheritdoc/>
        public void Changed()
        {
            // nothing held, so nothing to mark
        }

        /// <inheritdoc/>
        public bool Playing() => false;

        /// <inheritdoc/>
        public void Save(IList<string> paths)
        {
            if (paths != null && paths.Count > 0) throw new ArgumentException("this host saves no scenes");
        }
    }
}
