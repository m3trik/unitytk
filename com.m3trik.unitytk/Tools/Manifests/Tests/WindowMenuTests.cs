using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>
    /// <see cref="WindowMenu"/> and <see cref="EditorLook"/>: a window wears the manifest windows' look once; its
    /// menu is drawn inside it, sections under their headings, an entry with nothing to do greyed; a toggle
    /// keeps it open and says its new state; it closes once, saying so.
    /// </summary>
    public class WindowMenuTests
    {
        [Test]
        public void A_window_wears_the_look_and_its_menu_is_drawn_inside_it()
        {
            var root = new VisualElement();
            Assert.That(EditorLook.Apply(root), Is.True, "the look is found in the package");
            EditorLook.Apply(root);
            Assert.That(root.ClassListContains(EditorLook.RootClass) && root.styleSheets.count == 1, "worn once");

            var menu = new WindowMenu();
            bool ran = false, ticked = false;
            int closed = 0;
            menu.Item("Build", () => ran = true);
            menu.Item("Nothing to do", null);
            menu.Toggle("Behaviors/Highlight", false, on => ticked = on);
            menu.Closed += () => closed++;
            menu.Show(root, Vector2.zero);
            VisualElement overlay = root.Q(className: "window-menu__overlay");
            Assert.That(overlay, Is.Not.Null, "drawn in the window, not by the system");
            var items = overlay.Query(className: "window-menu__item").ToList();
            Assert.That(items.Select(i => i.Q<Label>(className: "window-menu__text").text), Is.EqualTo(new[] { "Build", "Nothing to do", "Highlight" }));
            Assert.That(items[1].ClassListContains("window-menu__item--disabled"), "nothing to do: greyed");
            Assert.That(overlay.Q<Label>(className: "window-menu__heading").text, Is.EqualTo("Behaviors"), "a section under its heading");
            Assert.That(ran || ticked, Is.False, "nothing runs until clicked");

            menu.Close();
            menu.Close();
            Assert.That(closed, Is.EqualTo(1), "closed once, said once");
            Assert.That(root.Q(className: "window-menu__overlay"), Is.Null);
        }
    }
}
