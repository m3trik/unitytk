using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using Payload = M3trik.Unitytk.Contracts.HandoffBlock.Shape.Payload;

[assembly: ReadsRecord(UnitytkRecords.Handoff, typeof(HandoffBlock))]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Looks.Tests")]

namespace M3trik.Unitytk.Looks
{
    /// <summary>
    /// The importer of a delivery's look: its <c>handoff</c> record's <c>rendering.renderer</c> section
    /// (<see cref="UnitytkRecords.Handoff"/>, <see cref="HandoffRendering.Renderer"/>: the tone mapping and
    /// exposure the file was approved under) as a <see cref="SceneLook"/> on the model's root, so the
    /// model shows as approved in any scene it is placed in. A setting the record leaves out takes the
    /// policy's value; a tone mapping unitytk does not show is said, and the project's own look stands.
    /// </summary>
    public class LooksImporter : AssetPostprocessor
    {
        const string Tag = "Looks";

        /// <summary>Bump to reimport every model when what the import makes changes.</summary>
        const uint VERSION = 1;

        /// <inheritdoc/>
        public override uint GetVersion() => VERSION;

        static IRecordSource Records => Provided.One<IRecordSource>();

        // This model's import, OnPreprocessModel to OnPostprocessModel: the record collected off its carrier.
        readonly Dictionary<string, string> _collected = new Dictionary<string, string>(StringComparer.Ordinal);

        void OnPreprocessModel() => _collected.Clear();

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            if (!Imports.IsDelivery(assetPath)) return;
            if (Records.TryGetRecord(names, values, UnitytkRecords.Handoff, out string json))
                Records.Collect(_collected, assetPath, go, json);
        }

        void OnPostprocessModel(GameObject root)
        {
            _collected.TryGetValue(assetPath, out string json);
            _collected.Clear();
            if (json == null || !Imports.IsDelivery(assetPath)) return;
            Assemble(root, json, assetPath, Records);
        }

        /// <summary>
        /// The look the handoff record <paramref name="json"/> states, on <paramref name="root"/>; null for a
        /// record refused, one stating no renderer, or one naming a tone mapping unitytk does not show.
        /// </summary>
        internal static SceneLook Assemble(GameObject root, string json, string assetPath, IRecordSource records)
        {
            Payload record = records.Read(UnitytkRecords.Handoff, json, HandoffBlock.CurrentVersion, Payload.Read, Tag, assetPath);
            const string renderer = HandoffRendering.Renderer.Section;
            if (!HandoffBlock.States(record, renderer)) return null;
            string toneMapping = HandoffBlock.Text(record, renderer, HandoffRendering.Renderer.ToneMapping,
                                                   HandoffRendering.Renderer.ToneMappingDefault);
            float exposure = HandoffBlock.Number(record, renderer, HandoffRendering.Renderer.ToneMappingExposure,
                                                 HandoffRendering.Renderer.ToneMappingExposureDefault);
            if (!LookCurve.Knows(toneMapping))
            {
                ImportLog.Warning(assetPath, $"look: the handoff's tone mapping '{toneMapping}' is not one unitytk shows ({LookCurve.Names}); " +
                                             "the project's own look stands.");
                return null;
            }
            SceneLook look = root.GetComponent<SceneLook>();
            if (look == null) look = root.AddComponent<SceneLook>();
            LooksActions.Fill(look, toneMapping, exposure);
            ImportLog.Info(assetPath, $"look: {toneMapping} at exposure {exposure}, the handoff's, on '{root.name}' (a Scene Look).");
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            if (host != null && host.DisplaySize <= 0)
                ImportLog.Warning(assetPath, $"look: the {host.Name} pipeline shows no look (it reads no colour lookup table), so the " +
                                             "project's own stands; a URP project shows it.");
            return look;
        }
    }
}
