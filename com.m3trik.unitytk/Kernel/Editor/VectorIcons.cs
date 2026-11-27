using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The editor's icon set: the ecosystem's shared SVG icons, copied
    /// beside this assembly as <c>Icons/&lt;name&gt;.svg</c> by
    /// <c>m3trik/scripts/sync_unity_icons.py</c>, their icon colour written white. Unity's
    /// built-in SVG importer makes each a UI
    /// Toolkit <see cref="VectorImage"/>, sharp at any size, drawn in a tint by
    /// <see cref="VectorIcon"/> (an <see cref="Image"/>: its tint multiplies the white).
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's SVG importer (built in from Unity 6.3, this package's floor) is
    /// adopted as it stands; a reader of our own and raster copies (blurred when scaled)
    /// were weighed and not adopted.
    /// </remarks>
    public static class VectorIcons
    {
        /// <summary>The icons' folder, as the asset database names it.</summary>
        public const string Folder = "Packages/com.m3trik.unitytk/Kernel/Editor/Icons";

        static readonly Dictionary<string, VectorImage> Cache = new Dictionary<string, VectorImage>(StringComparer.Ordinal);

        /// <summary>Every icon's name, sorted.</summary>
        public static IReadOnlyList<string> Names
        {
            get
            {
                var names = new List<string>();
                string folder = Path.GetFullPath(Folder);
                if (Directory.Exists(folder))
                    foreach (string file in Directory.GetFiles(folder, "*.svg")) names.Add(Path.GetFileNameWithoutExtension(file));
                names.Sort(StringComparer.Ordinal);
                return names;
            }
        }

        /// <summary>Icon <paramref name="name"/> (its file's name), or null for none.</summary>
        public static VectorImage Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out VectorImage cached) && cached != null) return cached;
            VectorImage image = AssetDatabase.LoadAssetAtPath<VectorImage>($"{Folder}/{name}.svg");
            Cache[name] = image;
            return image;
        }

        /// <summary>Whether there is an icon named <paramref name="name"/>.</summary>
        public static bool Has(string name) => Load(name) != null;
    }

    /// <summary>
    /// One <see cref="VectorIcons"/> icon as a UI Toolkit element: <see cref="icon"/> drawn in
    /// <see cref="tint"/>, fitted to its box (16 x 16 by default; size it as any element). Given no
    /// tint, its style sheet's colours it (<c>--unity-image-tint-color</c>, as a state's class sets it),
    /// else it draws white.
    /// </summary>
    public sealed class VectorIcon : Image
    {
        string _icon;

        public VectorIcon(string icon = null, Color? tint = null)
        {
            style.width = 16;
            style.height = 16;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            scaleMode = ScaleMode.ScaleToFit;
            if (tint.HasValue) tintColor = tint.Value;  // set here, it outranks a style sheet's
            this.icon = icon;
        }

        /// <summary>The icon's name; null or an unknown one draws nothing.</summary>
        public string icon
        {
            get => _icon;
            set { _icon = value; vectorImage = VectorIcons.Load(value); }
        }

        /// <summary>The colour the icon draws in.</summary>
        public Color tint
        {
            get => tintColor;
            set => tintColor = value;
        }
    }
}
