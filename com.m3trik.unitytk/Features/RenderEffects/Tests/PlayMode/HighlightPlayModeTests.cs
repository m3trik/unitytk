using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// A <see cref="Highlight"/>'s pulse in Play Mode, where Unity drives its clock: under a paused time scale one
    /// on game time holds still, and one told to run on real time (<see cref="Highlight.unscaledTime"/>) pulses on.
    /// </summary>
    public class HighlightPlayModeTests
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

        // A pulse rising over its first half second, started as it is enabled.
        Highlight Pulsing(string name, bool unscaled)
        {
            GameObject node = _stage.Node(name);
            node.SetActive(false);
            var highlight = node.AddComponent<Highlight>();
            highlight.intensity = 1f;
            highlight.pulse = true;
            highlight.loop = true;
            highlight.pulsePeriod = 1f;
            highlight.pulseDuty = 0.5f;
            highlight.pulseRamp = 0.5f;
            highlight.unscaledTime = unscaled;
            node.SetActive(true);
            return highlight;
        }

        [UnityTest]
        public IEnumerator Under_a_paused_time_scale_it_pulses_on_real_time_only_where_told()
        {
            Time.timeScale = 0f;
            Highlight game = Pulsing("Game", unscaled: false), real = Pulsing("Real", unscaled: true);
            float gameAt = game.Current, realAt = real.Current;
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(game.Current, Is.EqualTo(gameAt), "game time stands still while paused: the pulse holds");
            Assert.That(real.Current - realAt, Is.GreaterThan(0.1f), "real time runs on: the pulse rises");
        }
    }
}
