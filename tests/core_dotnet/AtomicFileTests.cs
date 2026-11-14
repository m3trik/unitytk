using System;
using System.IO;
using M3trik.Unitytk.Core;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>
    /// A file written whole or not at all, and the temp files a killed write left
    /// beside it swept once they are older than <see cref="AtomicFile.StaleAfter"/>.
    /// </summary>
    public class AtomicFileTests
    {
        string folder;
        string path;

        [SetUp]
        public void MakeFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "unitytk_atomic_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, "settings.json");
        }

        [TearDown]
        public void RemoveFolder()
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        string Part(string name, TimeSpan age)
        {
            string part = Path.Combine(folder, name);
            File.WriteAllText(part, "half");
            File.SetLastWriteTimeUtc(part, DateTime.UtcNow - age);
            return part;
        }

        [Test]
        public void A_write_makes_or_replaces_the_file_and_leaves_no_temp_file()
        {
            AtomicFile.Write(path, "one");
            Assert.That(File.ReadAllText(path), Is.EqualTo("one"));
            AtomicFile.Write(path, "two");
            Assert.That(File.ReadAllText(path), Is.EqualTo("two"));
            Assert.That(Directory.GetFiles(folder, "*.part"), Is.Empty);
        }

        [Test]
        public void A_stale_temp_file_is_swept_and_a_fresh_one_kept()
        {
            string stale = Part("settings.json.dead.part", AtomicFile.StaleAfter + TimeSpan.FromMinutes(5));
            string fresh = Part("settings.json.live.part", TimeSpan.FromMinutes(1));
            AtomicFile.Sweep(path);
            Assert.That(File.Exists(stale), Is.False, "a killed write's leftover stays");
            Assert.That(File.Exists(fresh), Is.True, "a write still running lost its temp file");
        }

        [Test]
        public void The_sweep_touches_only_the_files_of_its_own_path()
        {
            TimeSpan old = AtomicFile.StaleAfter + TimeSpan.FromHours(1);
            string other = Part("other.json.dead.part", old);
            string notPart = Part("settings.json.dead.bak", old);
            AtomicFile.Sweep(path);
            Assert.That(File.Exists(other), Is.True);
            Assert.That(File.Exists(notPart), Is.True);
        }

        [Test]
        public void A_write_sweeps_first()
        {
            string stale = Part("settings.json.dead.part", AtomicFile.StaleAfter + TimeSpan.FromMinutes(5));
            AtomicFile.Write(path, "whole");
            Assert.That(File.Exists(stale), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo("whole"));
        }

        [Test]
        public void A_missing_folder_is_nothing_to_sweep()
        {
            Assert.DoesNotThrow(() => AtomicFile.Sweep(Path.Combine(folder, "absent", "settings.json")));
        }

        [Test]
        public void A_leftover_is_stale_after_an_hour()
        {
            Assert.That(AtomicFile.StaleAfter, Is.EqualTo(TimeSpan.FromHours(1)));
        }
    }
}
