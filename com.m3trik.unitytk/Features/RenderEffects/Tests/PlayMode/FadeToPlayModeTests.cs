using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// A <see cref="FadeTo"/> in Play Mode, where Unity drives its clock: under a paused time scale one on game
    /// time stands still, and one told to run on real time (<see cref="FadeTo.unscaledTime"/>) fades all the same.
    /// </summary>
    public class FadeToPlayModeTests
    {
        Stage _stage;
        float _scale;

        [SetUp]
        public void SetUp()
        {
            _stage = new Stage();
            _scale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _scale;
            _stage.Dispose();
        }

        // A fade from 1 to 0 over a tenth of a second, started as it is enabled.
        FadeTo Fading(string name, bool unscaled)
        {
            GameObject node = _stage.Node(name);
            node.SetActive(false);
            node.AddComponent<Fade>().alpha = 1f;
            var to = node.AddComponent<FadeTo>();
            to.alpha = 0f;
            to.seconds = 0.1f;
            to.unscaledTime = unscaled;
            node.SetActive(true);
            return to;
        }

        [UnityTest]
        public IEnumerator Under_a_paused_time_scale_it_fades_on_real_time_only_where_told()
        {
            Time.timeScale = 0f;
            FadeTo game = Fading("Game", unscaled: false), real = Fading("Real", unscaled: true);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(game.GetComponent<Fade>().alpha, Is.EqualTo(1f), "game time stands still while paused: no fade yet");
            Assert.That(game.Running, Is.True);
            Assert.That(real.GetComponent<Fade>().alpha, Is.EqualTo(0f), "real time runs on: faded");
            Assert.That(real.Running, Is.False);
        }
    }
}
