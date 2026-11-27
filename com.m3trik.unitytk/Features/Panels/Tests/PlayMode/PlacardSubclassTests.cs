using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Panels.Tests
{
    /// <summary>
    /// A <see cref="Placard"/> subclass in Play Mode, where Unity drives it (an editor test's component cannot be one):
    /// what the placard shows is drawn by the subclass's own <c>Render</c>, on its own layout.
    /// </summary>
    public class PlacardSubclassTests
    {
        [UnityTest]
        public IEnumerator A_subclass_draws_what_the_placard_shows_on_its_own_layout()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var node = new GameObject("Own placard");
            try
            {
                var document = node.AddComponent<UIDocument>();
                document.panelSettings = settings;
                var placard = node.AddComponent<OwnLayoutPlacard>();
                placard.Show("Open the door", "Select it.", null);
                yield return null;  // its LateUpdate draws
                Label heading = document.rootVisualElement.Q<Label>(OwnLayoutPlacard.HeadingName);
                Assert.That(heading != null && heading.text == "OPEN THE DOOR", "drawn by the subclass");
                Assert.That(document.rootVisualElement.Q<Label>("heading"), Is.Null, "the package's layout is not there to draw on");
            }
            finally
            {
                Object.Destroy(node);
                Object.Destroy(settings);
            }
        }
    }
}
