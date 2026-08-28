using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Orla
{
    internal enum OrlaIcon
    {
        WifiOff, WifiLow, WifiMedium, WifiHigh, Ethernet,
        VolumeZero, VolumeLow, VolumeHigh, VolumeMuted,
        Battery, Bluetooth, Settings, Search, Close, ChevronRight
    }

    // Vetores derivados do Lucide (ISC), normalizados na mesma grade 24 x 24.
    // Viewbox e Path usam a composição nativa do WPF e não dependem de fontes
    // ou arquivos externos em tempo de execução.
    internal sealed class VectorIcon : Viewbox
    {
        private static readonly IDictionary<OrlaIcon, Geometry[]> Geometries = CreateGeometries();
        private readonly Canvas _canvas;
        private readonly List<Shape> _strokedShapes = new List<Shape>();
        private readonly List<Shape> _filledShapes = new List<Shape>();
        private OrlaIcon _icon;
        private Brush _foreground;
        private int _batteryPercent;
        private bool _hasBattery = true;

        internal VectorIcon(OrlaIcon icon, double size, Brush foreground, string accessibleName)
        {
            _icon = icon;
            _foreground = foreground;
            Width = size;
            Height = size;
            Stretch = Stretch.Uniform;
            StretchDirection = StretchDirection.Both;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            _canvas = new Canvas { Width = 24, Height = 24 };
            Child = _canvas;
            SetAccessibleName(accessibleName);
            Rebuild();
            UpdateAutomationStatus();
        }

        internal OrlaIcon Icon { get { return _icon; } }

        internal Brush Foreground
        {
            get { return _foreground; }
            set
            {
                if (ReferenceEquals(_foreground, value)) return;
                _foreground = value;
                UpdateBrushes();
            }
        }

        internal void SetAccessibleName(string value)
        {
            System.Windows.Automation.AutomationProperties.SetName(this, value ?? string.Empty);
        }

        internal void SetIcon(OrlaIcon icon)
        {
            if (_icon == icon) return;
            _icon = icon;
            Rebuild();
            UpdateAutomationStatus();
            RevealChange();
        }

        internal void SetBatteryState(int percent, bool hasBattery)
        {
            int bounded = Math.Max(0, Math.Min(100, percent));
            if (_icon == OrlaIcon.Battery && _batteryPercent == bounded && _hasBattery == hasBattery) return;
            _icon = OrlaIcon.Battery;
            _batteryPercent = bounded;
            _hasBattery = hasBattery;
            Rebuild();
            UpdateAutomationStatus();
            RevealChange();
        }

        private void Rebuild()
        {
            _canvas.Children.Clear();
            _strokedShapes.Clear();
            _filledShapes.Clear();

            if (_icon == OrlaIcon.Battery)
            {
                Rectangle outline = new Rectangle
                {
                    Width = 16, Height = 12, RadiusX = 2, RadiusY = 2, StrokeThickness = 2
                };
                Canvas.SetLeft(outline, 2);
                Canvas.SetTop(outline, 6);
                AddStroked(outline);

                Line terminal = new Line
                {
                    X1 = 22, X2 = 22, Y1 = 10, Y2 = 14, StrokeThickness = 2,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                };
                AddStroked(terminal);

                if (_hasBattery && _batteryPercent > 0)
                {
                    Rectangle fill = new Rectangle
                    {
                        Width = Math.Max(0.75, 12 * (_batteryPercent / 100.0)),
                        Height = 8, RadiusX = 1, RadiusY = 1
                    };
                    Canvas.SetLeft(fill, 4);
                    Canvas.SetTop(fill, 8);
                    _filledShapes.Add(fill);
                    _canvas.Children.Add(fill);
                }
            }
            else
            {
                Geometry[] iconGeometries;
                if (Geometries.TryGetValue(_icon, out iconGeometries))
                {
                    foreach (Geometry geometry in iconGeometries)
                    {
                        Path path = new Path
                        {
                            Data = geometry, StrokeThickness = 2,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Round,
                            StrokeLineJoin = PenLineJoin.Round
                        };
                        AddStroked(path);
                    }
                }
            }
            UpdateBrushes();
        }

        private void AddStroked(Shape shape)
        {
            _strokedShapes.Add(shape);
            _canvas.Children.Add(shape);
        }

        private void UpdateBrushes()
        {
            foreach (Shape shape in _strokedShapes) shape.Stroke = _foreground;
            foreach (Shape shape in _filledShapes) shape.Fill = _foreground;
        }

        private void UpdateAutomationStatus()
        {
            string status = _icon == OrlaIcon.Battery
                ? "Battery " + (_hasBattery ? _batteryPercent.ToString() + "%" : "unavailable")
                : _icon.ToString();
            System.Windows.Automation.AutomationProperties.SetItemStatus(this, status);
        }

        private void RevealChange()
        {
            if (!SystemParameters.ClientAreaAnimation || !IsLoaded) return;
            DoubleAnimation reveal = new DoubleAnimation(0.48, 1, TimeSpan.FromMilliseconds(115));
            reveal.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, reveal, HandoffBehavior.SnapshotAndReplace);
        }

        private static IDictionary<OrlaIcon, Geometry[]> CreateGeometries()
        {
            Dictionary<OrlaIcon, Geometry[]> icons = new Dictionary<OrlaIcon, Geometry[]>();
            Geometry wifiDot = Parse("M12 20h.01");
            Geometry wifiInner = Parse("M8.5 16.429a5 5 0 0 1 7 0");
            Geometry wifiMiddle = Parse("M5 12.859a10 10 0 0 1 14 0");
            Geometry wifiOuter = Parse("M2 8.82a15 15 0 0 1 20 0");
            icons[OrlaIcon.WifiLow] = new[] { wifiDot, wifiInner };
            icons[OrlaIcon.WifiMedium] = new[] { wifiDot, wifiInner, wifiMiddle };
            icons[OrlaIcon.WifiHigh] = new[] { wifiDot, wifiInner, wifiMiddle, wifiOuter };
            icons[OrlaIcon.WifiOff] = new[]
            {
                wifiDot, wifiInner,
                Parse("M5 12.859a10 10 0 0 1 5.17-2.69"),
                Parse("M19 12.859a10 10 0 0 0-2.007-1.523"),
                Parse("M2 8.82a15 15 0 0 1 4.177-2.643"),
                Parse("M22 8.82a15 15 0 0 0-11.288-3.764"),
                Parse("M2 2l20 20")
            };
            icons[OrlaIcon.Ethernet] = new[]
            {
                Parse("M10 8v1M14 8v1M18 8v1M6 8v1"),
                Parse("M19 17a2 2 0 0 0-1.765 1.059l-.47.882A2 2 0 0 1 15 20H9a2 2 0 0 1-1.765-1.059l-.47-.882A2 2 0 0 0 5 17H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2z")
            };

            Geometry speaker = Parse("M11 4.702a.705.705 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.705.705 0 0 0 11 19.298z");
            Geometry volumeInner = Parse("M16 9a5 5 0 0 1 0 6");
            icons[OrlaIcon.VolumeZero] = new[] { speaker };
            icons[OrlaIcon.VolumeLow] = new[] { speaker, volumeInner };
            icons[OrlaIcon.VolumeHigh] = new[] { speaker, volumeInner, Parse("M19.364 18.364a9 9 0 0 0 0-12.728") };
            icons[OrlaIcon.VolumeMuted] = new[] { speaker, Parse("M16.5 14.5l5-5M16.5 9.5l5 5") };
            icons[OrlaIcon.Bluetooth] = new[] { Parse("M7 7l10 10-5 5V2l5 5L7 17") };
            icons[OrlaIcon.Settings] = new[]
            {
                Parse("M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915"),
                Freeze(new EllipseGeometry(new Point(12, 12), 3, 3))
            };
            icons[OrlaIcon.Search] = new[]
            {
                Freeze(new EllipseGeometry(new Point(11, 11), 8, 8)),
                Parse("M16.66 16.66L21 21")
            };
            icons[OrlaIcon.Close] = new[] { Parse("M18 6L6 18M6 6l12 12") };
            icons[OrlaIcon.ChevronRight] = new[] { Parse("M9 18l6-6-6-6") };
            return icons;
        }

        private static Geometry Parse(string data)
        {
            return Freeze(Geometry.Parse(data));
        }

        private static Geometry Freeze(Geometry geometry)
        {
            if (geometry.CanFreeze) geometry.Freeze();
            return geometry;
        }
    }

}
