using System;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>What this editor and its project are, and what in them stops work: an agent's first call.</summary>
    public static class ProjectDoctor
    {
        /// <summary>unitytk's package name.</summary>
        public const string Package = "com.m3trik.unitytk";

        /// <summary>
        /// This editor and project as an agent needs them first: the Unity version, the
        /// project, its render pipeline, its packages (not the engine's built-in
        /// modules), whether its scripts compile and
        /// which unitytk serves it; each problem is an issue, with the call that fixes it
        /// when there is one.
        /// </summary>
        [Operation("unity.project.doctor",
            "This editor and project: the Unity version, the render pipeline, the packages, whether the scripts " +
            "compile and the unitytk serving it, each problem an issue.",
            Title = "Check the Unity project")]
        public static Dictionary<string, object> Doctor(OpContext ctx)
        {
            string project = UnitySceneFiles.Project;
            PackageInfo unitytk = PackageInfo.FindForAssembly(typeof(ProjectDoctor).Assembly);
            var packages = new SortedDictionary<string, object>(StringComparer.Ordinal);
            // The engine's own modules (com.unity.modules.*) are in every project: not news.
            foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages())
                if (package.source != UnityEditor.PackageManager.PackageSource.BuiltIn)
                    packages[package.name] = package.version;
            bool failed = EditorUtility.scriptCompilationFailed;

            if (failed)
                ctx.Issue("compile_failed",
                          "the project's scripts do not compile, so no script change takes effect until they do: " +
                          "the editor log names each error");
            if (unitytk == null)
                ctx.Issue("unitytk_not_a_package",
                          $"unitytk's kernel is not the package {Package} here (copied into Assets?), so it is not " +
                          "updated with unitytk: install the package and delete the copy",
                          "warning", null, Install(project));
            foreach (string problem in OperationRegistry.Problems)
                ctx.Issue("operation_declaration", problem, "warning");
            foreach (string problem in Provided.Problems())
                ctx.Issue("provider", problem);

            return new Dictionary<string, object>
            {
                ["unity"] = Application.unityVersion,
                ["project"] = project,
                ["pipeline"] = Pipeline(),
                ["packages"] = packages,
                ["compile_failed"] = failed,
                ["unitytk"] = unitytk == null ? null : unitytk.version,
            };
        }

        static OpCall Install(string project) => new OpCall
        {
            op = "unity_project.install",
            args = new Dictionary<string, object> { ["project"] = project },
        };

        /// <summary><c>built-in</c>, <c>urp</c>, <c>hdrp</c>, or another pipeline asset's type name.</summary>
        static string Pipeline()
        {
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null) return "built-in";
            string type = asset.GetType().FullName ?? "";
            if (type.Contains("Universal")) return "urp";
            if (type.Contains("HighDefinition")) return "hdrp";
            return asset.GetType().Name;
        }
    }
}
