using System;
using System.Collections.Generic;
using M3trik.Unitytk.Core;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>The JSON reader and writer every kernel part shares.</summary>
    public class JsonTests
    {
        [Test]
        public void A_tree_round_trips_text_for_text()
        {
            const string text = "{\"a\":[1,2.5,-3E-05,1.0,true,false,null],\"b\":{\"c\":\"d\\n\\\"e\\\"\"},\"f\":[]}";
            object tree = Json.Parse(text);
            Assert.That(Json.Write(tree), Is.EqualTo(text));
            Assert.That(Json.Parse(Json.Write(tree)), Is.EqualTo(tree));
        }

        [Test]
        public void An_integer_stays_exact()
        {
            Assert.That(Json.Parse("9007199254740993"), Is.EqualTo(9007199254740993L), "2^53 + 1, which a double cannot hold");
            Assert.That(Json.Write(Json.Parse("9007199254740993")), Is.EqualTo("9007199254740993"));
            object wide = Json.Parse("-123456789012345678901234567");
            Assert.That(wide, Is.TypeOf<decimal>(), "past a long: a decimal, every digit kept");
            Assert.That(Json.Write(wide), Is.EqualTo("-123456789012345678901234567"));
            Assert.That(Json.Parse("1e3"), Is.TypeOf<double>(), "an exponent makes a float, as in Python");
            Assert.That(Json.Parse("2.0"), Is.TypeOf<double>());
        }

        [Test]
        public void Without_exact_integers_every_number_is_a_double()
        {
            var list = (List<object>)Json.Parse("[1, 2.5]", false);
            Assert.That(list, Is.EqualTo(new object[] { 1.0, 2.5 }));
            Assert.That(list[0], Is.TypeOf<double>());
        }

        [Test]
        public void A_float_is_the_shortest_text_that_reads_back_with_dot_zero_when_integral()
        {
            Assert.That(Json.Number(0.1), Is.EqualTo("0.1"));
            Assert.That(Json.Number(0.1 + 0.2), Is.EqualTo("0.30000000000000004"));
            Assert.That(Json.Number(1.0), Is.EqualTo("1.0"));
            Assert.That(Json.Number(-2.5), Is.EqualTo("-2.5"));
            Assert.That(Json.Number(1e300), Is.EqualTo("1E+300"));
            foreach (double value in new[] { Math.PI, 1.0 / 3, 5e-324, double.MaxValue, 123456789.123 })
                Assert.That(double.Parse(Json.Number(value), System.Globalization.CultureInfo.InvariantCulture),
                            Is.EqualTo(value), $"{value:R} round-trips");
        }

        [Test]
        public void Non_finite_numbers_read_and_write_as_python_writes_them()
        {
            var list = (List<object>)Json.Parse("[NaN, Infinity, -Infinity]");
            Assert.That(double.IsNaN((double)list[0]));
            Assert.That(list[1], Is.EqualTo(double.PositiveInfinity));
            Assert.That(list[2], Is.EqualTo(double.NegativeInfinity));
            Assert.That(Json.Write(list), Is.EqualTo("[NaN,Infinity,-Infinity]"));
        }

        [Test]
        public void Indented_text_is_laid_out_as_python_json_dumps_indent_2_lays_it_out()
        {
            var tree = new Dictionary<string, object>
            {
                ["id"] = "x", ["empty"] = new Dictionary<string, object>(), ["none"] = new List<object>(),
                ["list"] = new List<object> { 1, "é\u0001" }, ["nested"] = new Dictionary<string, object> { ["k"] = null },
            };
            const string python =
                "{\n  \"id\": \"x\",\n  \"empty\": {},\n  \"none\": [],\n  \"list\": [\n    1,\n    \"\\u00e9\\u0001\"\n  ],\n" +
                "  \"nested\": {\n    \"k\": null\n  }\n}";
            Assert.That(Json.Write(tree, 2), Is.EqualTo(python));
        }

        [Test]
        public void A_value_json_cannot_hold_is_refused()
        {
            Assert.Throws<FormatException>(() => Json.Write(new { a = 1 }));
            Assert.Throws<FormatException>(() => Json.Parse("{\"a\": 1,}"));
            Assert.Throws<FormatException>(() => Json.Parse("[1] 2"));
        }
    }
}
