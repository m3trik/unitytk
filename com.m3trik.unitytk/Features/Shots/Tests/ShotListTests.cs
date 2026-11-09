using M3trik.Unitytk.Shots;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Shots.Tests
{
    /// <summary>The component's lookups: a shot by its clip, and a metadata value by its key.</summary>
    public class ShotListTests
    {
        GameObject _root;
        AnimationClip _intro, _outro, _other;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Model");
            _intro = new AnimationClip { name = "Intro" };
            _outro = new AnimationClip { name = "Outro" };
            _other = new AnimationClip { name = "Intro" };  // the same name, another clip
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object made in new Object[] { _root, _intro, _outro, _other })
                if (made != null) Object.DestroyImmediate(made);
        }

        [Test]
        public void A_shot_is_found_by_its_clip_and_only_by_it()
        {
            var shots = _root.AddComponent<ShotList>();
            var intro = new Shot { clip = _intro, description = "the door opens" };
            shots.entries.Add(intro);
            shots.entries.Add(new Shot { clip = _outro });
            Assert.That(shots.Entry(_intro), Is.SameAs(intro));
            Assert.That(shots.Entry(_outro).clip, Is.SameAs(_outro));
            Assert.That(shots.Entry(_other), Is.Null, "a clip, not a name: another clip named alike has no entry");
            Assert.That(shots.Entry(null), Is.Null);
        }

        [Test]
        public void An_entry_whose_clip_is_gone_or_an_empty_slot_is_passed_over()
        {
            var shots = _root.AddComponent<ShotList>();
            shots.entries.Add(new Shot { clip = _outro });
            shots.entries.Add(null);
            var intro = new Shot { clip = _intro };
            shots.entries.Add(intro);
            Object.DestroyImmediate(_outro);
            Assert.That(shots.Entry(_intro), Is.SameAs(intro));
            Assert.That(shots.Entry(_outro), Is.Null, "a destroyed clip has no entry");
        }

        [Test]
        public void A_metadata_value_is_its_json_text_by_its_exact_key()
        {
            var shot = new Shot { clip = _intro };
            shot.metadata.Add(new ShotField { key = "take", json = "2" });
            shot.metadata.Add(new ShotField { key = "cameras", json = "[\"A\", \"B\"]" });
            Assert.That(shot.TryGetMetadata("take", out string take), Is.True);
            Assert.That(take, Is.EqualTo("2"));
            Assert.That(shot.TryGetMetadata("cameras", out string cameras), Is.True);
            Assert.That(cameras, Is.EqualTo("[\"A\", \"B\"]"));
            Assert.That(shot.TryGetMetadata("Take", out string none), Is.False, "keys are case-sensitive, as JSON's are");
            Assert.That(none, Is.Null);
        }
    }
}
