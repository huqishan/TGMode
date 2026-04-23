using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ControlLibrary
{
    public static class IconFactory
    {
        public const string Menu = "menu";
        public const string ArrowLeft = "arrow-left";
        public const string ChevronRight = "chevron-right";
        public const string ChevronDown = "chevron-down";
        public const string Search = "search";
        public const string House = "house";
        public const string FlaskConical = "flask-conical";
        public const string Boxes = "boxes";
        public const string PlugZap = "plug-zap";
        public const string Network = "network";
        public const string MessageSquareCode = "message-square-code";
        public const string Cpu = "cpu";
        public const string Router = "router";
        public const string Workflow = "workflow";
        public const string FileCog = "file-cog";
        public const string Settings = "settings";

        private static readonly IReadOnlyDictionary<string, IconDefinition> IconDefinitions =
            new Dictionary<string, IconDefinition>
            {
                [Menu] = new IconDefinition(
                    paths:
                    [
                        "M4 5h16",
                        "M4 12h16",
                        "M4 19h16"
                    ]),
                [ArrowLeft] = new IconDefinition(
                    paths:
                    [
                        "m12 19-7-7 7-7",
                        "M19 12H5"
                    ]),
                [ChevronRight] = new IconDefinition(
                    paths:
                    [
                        "m9 6 6 6-6 6"
                    ]),
                [ChevronDown] = new IconDefinition(
                    paths:
                    [
                        "m6 9 6 6 6-6"
                    ]),
                [Search] = new IconDefinition(
                    paths:
                    [
                        "m21 21-4.34-4.34"
                    ],
                    circles:
                    [
                        new CircleSpec(11, 11, 8)
                    ]),
                [House] = new IconDefinition(
                    paths:
                    [
                        "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8",
                        "M3 10a2 2 0 0 1 .709-1.528l7-6a2 2 0 0 1 2.582 0l7 6A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"
                    ]),
                [FlaskConical] = new IconDefinition(
                    paths:
                    [
                        "M14 2v6a2 2 0 0 0 .245.96l5.51 10.08A2 2 0 0 1 18 22H6a2 2 0 0 1-1.755-2.96l5.51-10.08A2 2 0 0 0 10 8V2",
                        "M6.453 15h11.094",
                        "M8.5 2h7"
                    ]),
                [Boxes] = new IconDefinition(
                    paths:
                    [
                        "M2.97 12.92A2 2 0 0 0 2 14.63v3.24a2 2 0 0 0 .97 1.71l3 1.8a2 2 0 0 0 2.06 0L12 19v-5.5l-5-3-4.03 2.42Z",
                        "m7 16.5-4.74-2.85",
                        "m7 16.5 5-3",
                        "M7 16.5v5.17",
                        "M12 13.5V19l3.97 2.38a2 2 0 0 0 2.06 0l3-1.8a2 2 0 0 0 .97-1.71v-3.24a2 2 0 0 0-.97-1.71L17 10.5l-5 3Z",
                        "m17 16.5-5-3",
                        "m17 16.5 4.74-2.85",
                        "M17 16.5v5.17",
                        "M7.97 4.42A2 2 0 0 0 7 6.13v4.37l5 3 5-3V6.13a2 2 0 0 0-.97-1.71l-3-1.8a2 2 0 0 0-2.06 0l-3 1.8Z",
                        "M12 8 7.26 5.15",
                        "m12 8 4.74-2.85",
                        "M12 13.5V8"
                    ]),
                [PlugZap] = new IconDefinition(
                    paths:
                    [
                        "M6.3 20.3a2.4 2.4 0 0 0 3.4 0L12 18l-6-6-2.3 2.3a2.4 2.4 0 0 0 0 3.4Z",
                        "m2 22 3-3",
                        "M7.5 13.5 10 11",
                        "M10.5 16.5 13 14",
                        "m18 3-4 4h6l-4 4"
                    ]),
                [Network] = new IconDefinition(
                    paths:
                    [
                        "M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3",
                        "M12 12V8"
                    ],
                    rectangles:
                    [
                        new RectSpec(16, 16, 6, 6, 1),
                        new RectSpec(2, 16, 6, 6, 1),
                        new RectSpec(9, 2, 6, 6, 1)
                    ]),
                [MessageSquareCode] = new IconDefinition(
                    paths:
                    [
                        "M22 17a2 2 0 0 1-2 2H6.828a2 2 0 0 0-1.414.586l-2.202 2.202A.71.71 0 0 1 2 21.286V5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2z",
                        "m10 8-3 3 3 3",
                        "m14 14 3-3-3-3"
                    ]),
                [Cpu] = new IconDefinition(
                    paths:
                    [
                        "M12 20v2",
                        "M12 2v2",
                        "M17 20v2",
                        "M17 2v2",
                        "M2 12h2",
                        "M2 17h2",
                        "M2 7h2",
                        "M20 12h2",
                        "M20 17h2",
                        "M20 7h2",
                        "M7 20v2",
                        "M7 2v2"
                    ],
                    rectangles:
                    [
                        new RectSpec(4, 4, 16, 16, 2),
                        new RectSpec(8, 8, 8, 8, 1)
                    ]),
                [Router] = new IconDefinition(
                    paths:
                    [
                        "M6.01 18H6",
                        "M10.01 18H10",
                        "M15 10v4",
                        "M17.84 7.17a4 4 0 0 0-5.66 0",
                        "M20.66 4.34a8 8 0 0 0-11.31 0"
                    ],
                    rectangles:
                    [
                        new RectSpec(2, 14, 20, 8, 2)
                    ]),
                [Workflow] = new IconDefinition(
                    paths:
                    [
                        "M7 11v4a2 2 0 0 0 2 2h4"
                    ],
                    rectangles:
                    [
                        new RectSpec(3, 3, 8, 8, 2),
                        new RectSpec(13, 13, 8, 8, 2)
                    ]),
                [FileCog] = new IconDefinition(
                    paths:
                    [
                        "M15 8a1 1 0 0 1-1-1V2a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8z",
                        "M20 8v12a2 2 0 0 1-2 2h-4.182",
                        "m3.305 19.53.923-.382",
                        "M4 10.592V4a2 2 0 0 1 2-2h8",
                        "m4.228 16.852-.924-.383",
                        "m5.852 15.228-.383-.923",
                        "m5.852 20.772-.383.924",
                        "m8.148 15.228.383-.923",
                        "m8.53 21.696-.382-.924",
                        "m9.773 16.852.922-.383",
                        "m9.773 19.148.922.383"
                    ],
                    circles:
                    [
                        new CircleSpec(7, 18, 3)
                    ]),
                [Settings] = new IconDefinition(
                    paths:
                    [
                        "M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915"
                    ],
                    circles:
                    [
                        new CircleSpec(12, 12, 3)
                    ])
            };

        public static FrameworkElement Create(string iconKey, Brush strokeBrush, double size = 18)
        {
            if (!IconDefinitions.TryGetValue(iconKey, out IconDefinition? definition))
            {
                return new Border
                {
                    Width = size,
                    Height = size
                };
            }

            Canvas canvas = new Canvas
            {
                Width = 24,
                Height = 24
            };

            foreach (string pathData in definition.Paths)
            {
                canvas.Children.Add(CreatePath(Geometry.Parse(pathData), strokeBrush));
            }

            foreach (RectSpec rectangle in definition.Rectangles)
            {
                canvas.Children.Add(CreatePath(
                    new RectangleGeometry(new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), rectangle.Radius, rectangle.Radius),
                    strokeBrush));
            }

            foreach (CircleSpec circle in definition.Circles)
            {
                canvas.Children.Add(CreatePath(new EllipseGeometry(new Point(circle.CenterX, circle.CenterY), circle.Radius, circle.Radius), strokeBrush));
            }

            return new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Child = canvas
            };
        }

        private static Path CreatePath(Geometry geometry, Brush strokeBrush)
        {
            return new Path
            {
                Data = geometry,
                Stroke = strokeBrush,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent,
                SnapsToDevicePixels = true
            };
        }

        private sealed class IconDefinition
        {
            public IconDefinition(
                IReadOnlyList<string> paths,
                IReadOnlyList<RectSpec>? rectangles = null,
                IReadOnlyList<CircleSpec>? circles = null)
            {
                Paths = paths;
                Rectangles = rectangles ?? [];
                Circles = circles ?? [];
            }

            public IReadOnlyList<string> Paths { get; }

            public IReadOnlyList<RectSpec> Rectangles { get; }

            public IReadOnlyList<CircleSpec> Circles { get; }
        }

        private sealed record RectSpec(double X, double Y, double Width, double Height, double Radius);

        private sealed record CircleSpec(double CenterX, double CenterY, double Radius);
    }
}
