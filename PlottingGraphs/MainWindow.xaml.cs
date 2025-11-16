using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PlottingGraphs
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Example: line from (0,0) to (1,1) in world "units"
            Plot.Start = new Point(0, 0);
            Plot.End = new Point(1, 20);

            // Sample data: t, value
            var samples = new List<(double t, double value)>
            {
                (0.0,  1.0),
                (0.1,  0.2),
                (0.2, -0.2),
                (0.3,  0.5),
                (0.4,  0.0),
                (0.5, -0.5),
                (0.6, -1.0),
                (0.7, -0.3),
                (0.8,  0.4),
                (0.9,  0.8),
                (1.0,  0.0),
            };

            Plot.Samples = samples;
            Plot.ValueScale = 0.4;   // control how "tall" the plot looks
        }
    }

    public class PerpPlotControl : FrameworkElement
    {
        public Point Start { get; set; }
        public Point End { get; set; }

        // (t, value) list
        public IEnumerable<(double t, double value)> Samples { get; set; } =
            Enumerable.Empty<(double, double)>();

        // Visual scale factor for values
        public double ValueScale { get; set; } = 1.0;

        private Matrix _viewMatrix = Matrix.Identity;
        private bool _isPanning;
        private Point _lastMouse;

        public PerpPlotControl()
        {
            Focusable = true;
            Background = Brushes.Transparent;

            Loaded += (s, e) =>
            {
                // Zoom to fit when control first loads
                FitToContent();
            };

            MouseWheel += OnMouseWheel;
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
        }

        // Allow setting a background through XAML
        public Brush Background { get; set; } = Brushes.Transparent;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            // Paint background
            dc.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));

            // Push pan/zoom transform
            dc.PushTransform(new MatrixTransform(_viewMatrix));

            DrawBaseLine(dc);
            DrawPerpPlot(dc);

            dc.Pop();
        }

        private void DrawBaseLine(DrawingContext dc)
        {
            if (Start == End) return;

            var pen = new Pen(Brushes.Gray, 0.01); // width in world units
            dc.DrawLine(pen, Start, End);
        }

        private void DrawPerpPlot(DrawingContext dc)
        {
            var samplesList = Samples?.ToList();
            if (samplesList == null || samplesList.Count < 2) return;

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                bool first = true;
                foreach (var (t, v) in samplesList)
                {
                    var pt = SamplePoint(Start, End, t, v, ValueScale);
                    if (first)
                    {
                        ctx.BeginFigure(pt, false, false);
                        first = false;
                    }
                    else
                    {
                        ctx.LineTo(pt, true, false);
                    }
                }
            }
            geom.Freeze();

            var pen = new Pen(Brushes.Blue, 0.02);
            dc.DrawGeometry(null, pen, geom);
        }

        private static Point SamplePoint(Point start, Point end, double t, double v, double scale)
        {
            Vector line = end - start;
            if (line.Length < 1e-9) return start;

            Vector dir = line;
            dir.Normalize();

            Vector normal = new Vector(-dir.Y, dir.X); // 90° left

            Point basePoint = start + t * line;
            Point offsetPoint = basePoint + v * scale * normal;

            return offsetPoint;
        }

        // ====== Zoom to Fit ======

        /// <summary>
        /// Compute world-space bounds of the line + plotted curve.
        /// </summary>
        private Rect? GetWorldBounds()
        {
            var pts = new List<Point>();

            pts.Add(Start);
            pts.Add(End);

            var samplesList = Samples?.ToList();
            if (samplesList != null && samplesList.Count > 0)
            {
                foreach (var (t, v) in samplesList)
                {
                    pts.Add(SamplePoint(Start, End, t, v, ValueScale));
                }
            }

            var valid = pts
                .Where(p =>
                    !double.IsNaN(p.X) && !double.IsInfinity(p.X) &&
                    !double.IsNaN(p.Y) && !double.IsInfinity(p.Y))
                .ToList();

            if (valid.Count == 0)
                return null;

            double minX = valid.Min(p => p.X);
            double maxX = valid.Max(p => p.X);
            double minY = valid.Min(p => p.Y);
            double maxY = valid.Max(p => p.Y);

            return new Rect(new Point(minX, minY), new Point(maxX, maxY));
        }

        /// <summary>
        /// Zoom and center so all geometry fits in the control.
        /// Triggered on load and double middle-click.
        /// </summary>
        private void FitToContent()
        {
            if (ActualWidth <= 0 || ActualHeight <= 0)
            {
                _viewMatrix = Matrix.Identity;
                return;
            }

            var boundsNullable = GetWorldBounds();
            if (boundsNullable == null || boundsNullable.Value.IsEmpty)
            {
                _viewMatrix = Matrix.Identity;
                InvalidateVisual();
                return;
            }

            var bounds = boundsNullable.Value;

            // Add a little padding (10%)
            double marginFrac = 0.1;
            double worldWidth = bounds.Width;
            double worldHeight = bounds.Height;

            if (worldWidth < 1e-6) worldWidth = 1.0;
            if (worldHeight < 1e-6) worldHeight = 1.0;

            double usableWidth = ActualWidth * (1.0 - 2 * marginFrac);
            double usableHeight = ActualHeight * (1.0 - 2 * marginFrac);

            double scaleX = usableWidth / worldWidth;
            double scaleY = usableHeight / worldHeight;
            double scale = Math.Min(scaleX, scaleY);

            // Center of world bounds
            double cx = bounds.X + bounds.Width / 2.0;
            double cy = bounds.Y + bounds.Height / 2.0;

            var m = Matrix.Identity;
            m.Scale(scale, -scale); // flip Y so world "up" is screen "up"

            // We want world center -> screen center
            double tx = ActualWidth / 2.0 - (cx * scale);
            double ty = ActualHeight / 2.0 + (cy * scale); // note +cy because of -scale on Y

            m.Translate(tx, ty);

            _viewMatrix = m;
            InvalidateVisual();
        }

        // ====== Pan & Zoom ======

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(this);

            double zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;

            // Convert screen point to world (inverse transform)
            Matrix inv = _viewMatrix;
            if (!inv.HasInverse) return;
            inv.Invert();
            Point worldPos = inv.Transform(pos);

            // Scale around worldPos
            Matrix m = _viewMatrix;
            m.Translate(-worldPos.X, -worldPos.Y);
            m.Scale(zoomFactor, zoomFactor);
            m.Translate(worldPos.X, worldPos.Y);

            _viewMatrix = m;
            InvalidateVisual();
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Double middle-click: Zoom to fit
            if (e.ChangedButton == MouseButton.Middle && e.ClickCount == 2)
            {
                FitToContent();
                return;
            }

            // Left button: start panning
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanning = true;
                _lastMouse = e.GetPosition(this);
                CaptureMouse();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
            {
                var cur = e.GetPosition(this);
                Vector delta = cur - _lastMouse;

                // Translate in screen space
                Matrix m = _viewMatrix;
                m.Translate(delta.X, delta.Y);
                _viewMatrix = m;

                _lastMouse = cur;
                InvalidateVisual();
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanning = false;
                ReleaseMouseCapture();
            }
        }
    }
}
