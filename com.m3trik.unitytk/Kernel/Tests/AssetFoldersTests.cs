using NUnit.Framework;
using UnityEditor;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// The folders an action writes into (<see cref="AssetFolders"/>): a name made safe by
    /// one fixed set of characters whatever OS runs the editor, and a folder made with
    /// every missing parent.
    /// </summary>
    public class AssetFoldersTests
    {
        const string Root = "Assets/UnitytkAssetFoldersTests";

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Root);

        [Test]
        public void A_safe_name_replaces_every_character_some_OS_refuses_with_an_underscore()
        {
            Assert.That(AssetFolders.SafeName("ns:Box/a\\b*c?d\"e<f>g|h"), Is.EqualTo("ns_Box_a_b_c_d_e_f_g_h"));
            Assert.That(AssetFolders.SafeName("tab\there\u0001"), Is.EqualTo("tab_here_"), "control characters too");
            Assert.That(AssetFolders.SafeName("plain name-1.0"), Is.EqualTo("plain name-1.0"));
            Assert.That(AssetFolders.SafeName(""), Is.EqualTo(""));
            Assert.That(AssetFolders.SafeName(null), Is.EqualTo(""));
        }

        [Test]
        public void Ensure_makes_the_folder_and_every_missing_parent_once()
        {
            AssetFolders.Ensure(Root + "/a/b");
            Assert.That(AssetDatabase.IsValidFolder(Root + "/a/b"), Is.True);
            AssetFolders.Ensure(Root + "/a/b");  // already there: nothing more
            Assert.That(AssetDatabase.GetSubFolders(Root + "/a"), Is.EqualTo(new[] { Root + "/a/b" }));
            AssetFolders.Ensure(Root.Replace('/', '\\') + "\\a\\c");
            Assert.That(AssetDatabase.IsValidFolder(Root + "/a/c"), Is.True, "either separator");
        }
    }
}
