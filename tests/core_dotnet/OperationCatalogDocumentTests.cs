using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Core;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>The generated type of pythontk's catalog document: the committed catalog read and written back.</summary>
    public class OperationCatalogDocumentTests
    {
        static string Catalog => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "editor_operations.json"));

        [Test]
        public void The_committed_catalog_round_trips_byte_for_byte()
        {
            OperationCatalogDocument document = OperationCatalogDocument.Read(Json.Parse(Catalog));
            Assert.That(document.provider, Is.EqualTo("com.m3trik.unitytk"));
            Assert.That(document.operations, Is.Not.Empty);
            Assert.That(Json.Write(document.ToJson(), 2) + "\n", Is.EqualTo(Catalog.Replace("\r\n", "\n")));
        }
    }
}
