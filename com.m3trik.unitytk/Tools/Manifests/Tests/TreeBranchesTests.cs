using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>
    /// <see cref="TreeBranches.Layout"/>: a tree row's guides and expander, the Shot Manifest's design
    /// (16 px cells and 20 px rows): an 11 px box at (2, 4) in the row's own cell, its
    /// sign 2 px clear of its walls; each row hanging from its parent's box by a line on the cell's seventh pixel.
    /// </summary>
    public class TreeBranchesTests
    {
        const float Cell = 16f, Row = 20f;

        [Test]
        public void A_closed_row_with_children_is_a_box_with_a_plus_and_no_lines()
        {
            TreeBranches.Parts parts = TreeBranches.Layout(0, true, false, new bool[0], Cell, Row);
            Assert.That(parts.guides, Is.Empty, "a top row hangs from nothing");
            Assert.That(parts.walls, Is.EquivalentTo(new[]
            {
                new Rect(2, 4, 11, 1), new Rect(2, 14, 11, 1), new Rect(2, 5, 1, 9), new Rect(12, 5, 1, 9),
            }));
            Assert.That(parts.sign, Is.EquivalentTo(new[] { new Rect(5, 9, 5, 1), new Rect(7, 7, 1, 2), new Rect(7, 10, 1, 2) }),
                        "a plus on the centre pixel, its upright above and below the bar");
        }

        [Test]
        public void An_open_row_shows_a_minus_and_a_line_down_to_its_children()
        {
            TreeBranches.Parts parts = TreeBranches.Layout(0, true, true, new bool[0], Cell, Row);
            Assert.That(parts.sign, Is.EqualTo(new[] { new Rect(5, 9, 5, 1) }), "a minus alone");
            Assert.That(parts.guides, Is.EqualTo(new[] { new Rect(7, 15, 1, 5) }), "from under the box to the row's foot");
        }

        [Test]
        public void A_child_hangs_from_its_parents_box_and_the_last_ends_its_line()
        {
            TreeBranches.Parts middle = TreeBranches.Layout(1, false, false, new[] { true }, Cell, Row);
            Assert.That(middle.walls, Is.Empty);
            Assert.That(middle.guides, Is.EquivalentTo(new[] { new Rect(7, 0, 1, 20), new Rect(8, 9, 22, 1) }),
                        "down through the row (a sibling follows), across from beside it to 2 px short of its own cell's end");
            TreeBranches.Parts last = TreeBranches.Layout(1, false, false, new[] { false }, Cell, Row);
            Assert.That(last.guides.First(), Is.EqualTo(new Rect(7, 0, 1, 10)), "the last ends at its elbow");
        }

        [Test]
        public void A_nested_row_carries_its_ancestors_lines_and_joins_its_own_box()
        {
            // Depth 2, with children: its grandparent's line runs on (a later aunt), its parent's does not.
            TreeBranches.Parts parts = TreeBranches.Layout(2, true, false, new[] { true, false }, Cell, Row);
            Assert.That(parts.guides, Does.Contain(new Rect(7, 0, 1, 20)), "the grandparent's line, through");
            Assert.That(parts.guides, Does.Contain(new Rect(23, 0, 1, 10)), "its elbow in the parent's cell: the last child");
            Assert.That(parts.guides, Does.Contain(new Rect(24, 9, 10, 1)), "across to its own box's wall (x 34)");
            Assert.That(parts.walls.First(), Is.EqualTo(new Rect(34, 4, 11, 1)), "its box in its own cell");
        }

        [Test]
        public void No_two_parts_overlap_so_a_faded_colour_is_laid_once_on_every_pixel()
        {
            foreach (int depth in new[] { 0, 1, 2 })
            foreach (bool children in new[] { false, true })
            foreach (bool open in new[] { false, true })
            foreach (bool later in new[] { false, true })
            {
                bool[] runs = Enumerable.Repeat(later, depth).ToArray();
                TreeBranches.Parts parts = TreeBranches.Layout(depth, children, open, runs, Cell, Row);
                Rect[] all = parts.guides.Concat(parts.walls).Concat(parts.sign).ToArray();
                for (int i = 0; i < all.Length; i++)
                    for (int j = i + 1; j < all.Length; j++)
                        Assert.That(all[i].Overlaps(all[j]), Is.False,
                                    $"depth {depth}, children {children}, open {open}, later {later}: {all[i]} and {all[j]}");
            }
        }
    }
}
