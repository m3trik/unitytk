using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Manifests.Tests
{
    /// <summary>
    /// <see cref="ManifestValues"/>: a value an edit keeps, written back to its field only where its text is a value of the
    /// field's kind; any other is refused, the field left as it was.
    /// </summary>
    public class ManifestValuesTests
    {
        [Test]
        public void A_switch_takes_true_or_false_or_a_sheets_words_for_them_and_refuses_any_other_text()
        {
            var part = new GameObject("part").AddComponent<BoxCollider>();
            try
            {
                using (var so = new SerializedObject(part))
                {
                    SerializedProperty trigger = so.FindProperty("m_IsTrigger");
                    Assert.That(ManifestValues.Write(trigger, "True"), Is.True);
                    Assert.That(trigger.boolValue, Is.True);
                    Assert.That(ManifestValues.Write(trigger, "maybe"), Is.False, "no value of a switch: refused");
                    Assert.That(trigger.boolValue, Is.True, "and left as it was, never read as off");
                    Assert.That(ManifestValues.Write(trigger, "false"), Is.True);
                    Assert.That(trigger.boolValue, Is.False);
                    // A sheet writes a flag on or off (yes or no): the template's own words for it.
                    Assert.That(ManifestValues.Write(trigger, "on") && trigger.boolValue, Is.True);
                    Assert.That(ManifestValues.Write(trigger, "No") && !trigger.boolValue, Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(part.gameObject);
            }
        }

        [Test]
        public void A_colour_is_kept_as_its_four_numbers_past_1_and_a_sheets_hex_code_still_reads()
        {
            // A highlight's colour is HDR (its intensity past 1): kept as #RRGGBBAA it was clamped, so an object's own glow
            // lost its intensity, and raising only the intensity of a saturated colour read as no change at all.
            var lamp = new GameObject("lamp").AddComponent<Light>();
            try
            {
                using (var so = new SerializedObject(lamp))
                {
                    SerializedProperty colour = so.FindProperty("m_Color");
                    colour.colorValue = new Color(1.5f, 0.25f, 0f, 1f);
                    Assert.That(ManifestValues.Read(colour), Is.EqualTo("1.5,0.25,0,1"), "exact, and past 1");
                    Assert.That(ManifestValues.Text(new Color(1.5f, 0.25f, 0f, 1f)), Is.EqualTo(ManifestValues.Read(colour)),
                                "a part's record and an edit are written by the one codec");
                    Assert.That(ManifestValues.Write(colour, "3,0,0,1") && colour.colorValue == new Color(3f, 0f, 0f, 1f), Is.True,
                                "read back as written");
                    Assert.That(ManifestValues.Write(colour, "#FF0000FF") && colour.colorValue == Color.red, Is.True, "a sheet's hex code reads");
                    Assert.That(ManifestValues.Write(colour, "1,0"), Is.False, "two numbers are no colour: refused");
                    Assert.That(colour.colorValue, Is.EqualTo(Color.red), "and left as it was");
                }
            }
            finally
            {
                Object.DestroyImmediate(lamp.gameObject);
            }
        }
    }
}
