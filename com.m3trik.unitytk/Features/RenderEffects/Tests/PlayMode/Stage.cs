using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;

[assembly: InternalsVisibleTo("M3trik.Unitytk.RenderEffects.Tests")]

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// Scene objects a test builds and takes away, in Edit Mode and Play Mode alike (the
    /// Edit Mode tests reference this assembly for it): materials written on the project's
    /// own pipeline Lit (<see cref="MaterialWriter"/>, whichever pipeline the project
    /// draws with), renderers wearing them, and the host's names to read them by.
    /// </summary>
    internal sealed class Stage : IDisposable
    {
        readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();

        public Stage()
        {
            Host = RenderPipelineHosts.Current;
            Assume.That(Host, Is.Not.Null, "a unitytk render-pipeline host serves this project's pipeline");
        }

        public IRenderPipelineHost Host { get; }

        public LitTable Table => Host.Lit;

        public T Made<T>(T made) where T : UnityEngine.Object
        {
            _made.Add(made);
            return made;
        }

        /// <summary>A material on the pipeline's Lit, written from <paramref name="surface"/> (default: opaque white).</summary>
        public Material Lit(string name, LitSurface surface = null)
        {
            var material = Made(new Material(Shader.Find(Host.LitShader)) { name = name });
            Assert.That(MaterialWriter.Write(material, surface ?? new LitSurface(), Host), Is.True, name);
            return material;
        }

        /// <summary>A material on the pipeline's Lit of base colour <paramref name="color"/> (linear).</summary>
        public Material Lit(string name, Color color) => Lit(name, new LitSurface { baseColor = color });

        public GameObject Node(string name, Transform parent = null)
        {
            var go = Made(new GameObject(name));
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>A renderer under <paramref name="parent"/> wearing <paramref name="materials"/>, slot by slot.</summary>
        public MeshRenderer Renderer(string name, Transform parent, params Material[] materials)
        {
            var go = Node(name, parent);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }

        public Color BaseColor(Material material) => material.GetColor(Table.BaseColor);

        /// <summary>The emission a material shows, linear (its colour decoded).</summary>
        public Color Emission(Material material) => MaterialColor.Decode(material.GetColor(Table.EmissionColor));

        public Texture EmissionMap(Material material) => material.GetTexture(Table.EmissionMap);

        public void Dispose()
        {
            // Each writer gives its renderers back and frees its twins first (Edit Mode calls no OnDisable).
            foreach (UnityEngine.Object made in _made)
                if (made is GameObject go && go != null)
                    foreach (RenderEffect effect in go.GetComponents<RenderEffect>()) effect.Release();
            for (int i = _made.Count - 1; i >= 0; i--)
                if (_made[i] != null) UnityEngine.Object.DestroyImmediate(_made[i]);
            _made.Clear();
        }

        public static void AssertRgb(Color actual, Color expected, string why)
            => Assert.That(new[] { actual.r, actual.g, actual.b }, Is.EqualTo(new[] { expected.r, expected.g, expected.b }).Within(2e-3f), why);
    }
}
