"""Tests of make-icon.py: the icon of the package is drawn from the same bulb as the logo of the documentation site.

They need no Pillow (only the drawing itself does): they read the path that is drawn, and the SVG files that it has to be the same as.
That the icon that was drawn is a PNG that nuget.org accepts is checked in test_verify_package.py.

Run with: python -m unittest discover -s tools -p "test_*.py"
"""
import importlib.util
import os
import unittest
import xml.etree.ElementTree as ElementTree

_TOOLS = os.path.dirname(os.path.abspath(__file__))
_SPEC = importlib.util.spec_from_file_location("make_icon", os.path.join(_TOOLS, "make-icon.py"))
make_icon = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(make_icon)

SVG = "{http://www.w3.org/2000/svg}"


def image(name):
    return ElementTree.parse(os.path.join(_TOOLS, "..", "docs", "images", name)).getroot()


def hexadecimal(colour):
    return "#{:02x}{:02x}{:02x}".format(*colour)


class PolylinesTests(unittest.TestCase):
    def test_a_line_is_its_points_and_z_goes_back_to_where_it_began(self):
        self.assertEqual([[(0.0, 0.0), (10.0, 0.0), (10.0, 5.5), (0.0, 0.0)]], make_icon.polylines("M0 0L10 0L10 5.5Z"))

    def test_every_m_begins_a_line_of_its_own(self):
        self.assertEqual([[(1.0, 2.0), (3.0, 4.0)], [(5.0, 6.0), (7.0, 8.0)]], make_icon.polylines("M1 2L3 4M5 6L7 8"))

    def test_a_curve_is_cut_into_pieces_that_run_from_its_start_to_its_end(self):
        (line,) = make_icon.polylines("M0 0C0 10 10 10 10 0")

        self.assertEqual(make_icon.CURVE_STEPS + 1, len(line))
        self.assertEqual((0.0, 0.0), line[0])
        self.assertAlmostEqual(10.0, line[-1][0])
        self.assertAlmostEqual(0.0, line[-1][1])
        # The middle of this curve is at 3/4 of the height of its control points, by the formula of a cubic Bezier curve.
        self.assertAlmostEqual(5.0, line[make_icon.CURVE_STEPS // 2][0])
        self.assertAlmostEqual(7.5, line[make_icon.CURVE_STEPS // 2][1])

    def test_what_is_not_a_path_of_the_logo_is_refused(self):
        for path in ("M0 0H10", "M0 0l1 1", "L1 1", "Z", "M0", "M0 0C1 1 2 2", "M0 0 5 5"):
            with self.subTest(path), self.assertRaises(ValueError):
                make_icon.polylines(path)

    def test_the_bulb_can_be_read(self):
        lines = make_icon.polylines(make_icon.GLYPH)

        # The glass, the base, and three rays.
        self.assertEqual(5, len(lines))
        self.assertEqual(lines[0][0], lines[0][-1])
        for line in lines:
            for x, y in line:
                self.assertTrue(0 < x < make_icon.UNITS and 0 < y < make_icon.UNITS, (x, y))


class LogoTests(unittest.TestCase):
    def test_the_logo_and_the_favicon_have_the_bulb_that_the_icon_is_drawn_from(self):
        for name in ("logo.svg", "favicon.svg"):
            with self.subTest(name):
                bulbs = list(image(name).iter(f"{SVG}path"))

                self.assertEqual(1, len(bulbs))
                self.assertEqual(make_icon.GLYPH, bulbs[0].get("d"))
                self.assertEqual(str(make_icon.STROKE_WIDTH), bulbs[0].get("stroke-width"))
                self.assertEqual(("none", "#fff"), (bulbs[0].get("fill"), bulbs[0].get("stroke")))
                self.assertEqual(("round", "round"), (bulbs[0].get("stroke-linecap"), bulbs[0].get("stroke-linejoin")))

    def test_the_logo_is_a_rounded_square_with_the_diagonal_gradient_of_the_icon(self):
        root = image("logo.svg")
        square = root.find(f"{SVG}rect")
        gradient = root.find(f".//{SVG}linearGradient")

        self.assertEqual(f"0 0 {make_icon.UNITS} {make_icon.UNITS}", root.get("viewBox"))
        self.assertEqual((str(make_icon.UNITS), str(make_icon.UNITS), str(make_icon.CORNER_RADIUS)), (square.get("width"), square.get("height"), square.get("rx")))
        self.assertEqual(f"url(#{gradient.get('id')})", square.get("fill"))
        self.assertEqual(("0", "0", "1", "1"), tuple(gradient.get(name) for name in ("x1", "y1", "x2", "y2")))
        self.assertEqual([hexadecimal(make_icon.START), hexadecimal(make_icon.END)], [stop.get("stop-color") for stop in gradient.iter(f"{SVG}stop")])

    def test_the_favicon_is_a_rounded_square_of_the_brand_colour(self):
        root = image("favicon.svg")
        square = root.find(f"{SVG}rect")

        self.assertEqual(f"0 0 {make_icon.UNITS} {make_icon.UNITS}", root.get("viewBox"))
        self.assertEqual((str(make_icon.UNITS), str(make_icon.UNITS), str(make_icon.CORNER_RADIUS)), (square.get("width"), square.get("height"), square.get("rx")))
        self.assertEqual(hexadecimal(make_icon.END), square.get("fill"))


if __name__ == "__main__":
    unittest.main()
