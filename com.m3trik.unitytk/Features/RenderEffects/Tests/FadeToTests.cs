using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary><see cref="FadeTo"/>: its Fade taken from where it stands to its opacity over its time, then held.</summary>
    public class FadeToTests
    {
        GameObject _node;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_node);

        [Test]
        public void A_fade_runs_from_where_it_stands_to_its_opacity_and_holds()
        {
            _node = new GameObject("LAMP");
            var fade = _node.AddComponent<Fade>();
            var to = _node.AddComponent<FadeTo>();
            to.alpha = 0f;
            to.seconds = 2f;
            fade.alpha = 0.8f;
            to.Play();
            Assert.That(to.Running && fade.enabled);
            to.Advance(1f);
            Assert.That(fade.alpha, Is.EqualTo(0.4f).Within(1e-5));
            to.Advance(5f);
            Assert.That(fade.alpha, Is.EqualTo(0f));
            Assert.That(to.Running, Is.False);
        }

        [Test]
        public void A_fade_of_no_time_lands_at_once()
        {
            _node = new GameObject("LAMP");
            var fade = _node.AddComponent<Fade>();
            var to = _node.AddComponent<FadeTo>();
            to.alpha = 1f;
            to.seconds = 0f;
            fade.alpha = 0f;
            to.Play();
            Assert.That(fade.alpha, Is.EqualTo(1f));
            Assert.That(to.Running, Is.False);
        }
    }
}
