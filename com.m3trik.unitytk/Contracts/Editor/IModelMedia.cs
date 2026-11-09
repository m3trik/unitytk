using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Contracts
{
    /// <summary>
    /// The media a model's FBX embeds (audio, textures) and where its extracted
    /// copies live: its own media folder, beside the model. An importer asks it to
    /// extract the files a record names, and which project files it may bind. The
    /// carrier feature provides it; a test passes <see cref="PureModelMedia"/>.
    /// </summary>
    public interface IModelMedia
    {
        /// <summary>
        /// Ask that the model at <paramref name="assetPath"/> extract the embedded
        /// files <paramref name="names"/> on its next import (those
        /// <paramref name="holds"/> says are already current excepted); true when
        /// a request was made.
        /// </summary>
        bool RequestEmbeddedFiles(string assetPath, IEnumerable<string> names, Func<string, bool> holds = null);

        /// <summary>Whether the model's media folder holds an extracted copy of <paramref name="name"/> as current as the model.</summary>
        bool HoldsCurrent(string assetPath, string name);

        /// <summary>The folder the model's extracted media land in.</summary>
        string MediaFolder(string assetPath);

        /// <summary>
        /// The folders a model's files are looked for in, nearest first: its media
        /// folder (<see cref="MediaFolder"/>), then the folder it sits in, where a
        /// file copied in by hand, or extracted before the model shared its folder,
        /// still lies (one folder when they are the same).
        /// </summary>
        string[] MediaFolders(string assetPath);

        /// <summary>Whether the file at <paramref name="fileAssetPath"/> is the model's own extracted media.</summary>
        bool IsOwnMedia(string modelAssetPath, string fileAssetPath);

        /// <summary>
        /// Whether the file at <paramref name="fileAssetPath"/> lies in the model's folder of
        /// its own, where every extraction first imports the files it writes: a channel that
        /// gives a file its first-import settings by name finds the model's own there, and a
        /// later move beside the model takes those settings with it. Never the folder the
        /// model sits in, where a same-named file dropped by hand is not the model's; and,
        /// unlike <see cref="IsOwnMedia"/>, not a file the model extracted beside itself,
        /// which is marked its own only after its first import.
        /// </summary>
        bool InOwnMediaFolder(string modelAssetPath, string fileAssetPath);

        /// <summary>Whether the model may bind the file at <paramref name="fileAssetPath"/> (its own, or no other model's extraction).</summary>
        bool IsReusableBy(string modelAssetPath, string fileAssetPath);

        /// <summary>The model's own extracted copies of <paramref name="name"/>.</summary>
        IEnumerable<string> OwnMediaCopies(string assetPath, string name);
    }

    /// <summary>
    /// The stand-in: a model that embeds nothing, beside which any file may be
    /// bound; its media folder is the model's own folder.
    /// </summary>
    [Provides(typeof(IModelMedia), StandIn = true)]
    public sealed class PureModelMedia : IModelMedia
    {
        /// <inheritdoc/>
        public bool RequestEmbeddedFiles(string assetPath, IEnumerable<string> names, Func<string, bool> holds = null) => false;

        /// <inheritdoc/>
        public bool HoldsCurrent(string assetPath, string name) => false;

        /// <inheritdoc/>
        public string MediaFolder(string assetPath) => Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? "";

        /// <inheritdoc/>
        public string[] MediaFolders(string assetPath) => new[] { MediaFolder(assetPath) };

        /// <inheritdoc/>
        public bool IsOwnMedia(string modelAssetPath, string fileAssetPath) => false;

        /// <inheritdoc/>
        public bool InOwnMediaFolder(string modelAssetPath, string fileAssetPath) => false;

        /// <inheritdoc/>
        public bool IsReusableBy(string modelAssetPath, string fileAssetPath) => true;

        /// <inheritdoc/>
        public IEnumerable<string> OwnMediaCopies(string assetPath, string name) => new string[0];
    }
}
