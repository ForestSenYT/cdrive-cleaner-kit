using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CDriveCleaner.App.Controls;

/// <param name="Color">The category colour, shared with the list and the legend.</param>
/// <param name="Bytes">What this category could free.</param>
/// <param name="SelectedBytes">The part of it the user has ticked.</param>
public sealed record CapacitySegment(Color Color, double Bytes, double SelectedBytes);

/// <param name="Morph">True when the change should animate from the previous state (after a clean).</param>
public sealed record CapacitySnapshot(double Total, double Used, IReadOnlyList<CapacitySegment> Segments, bool Morph = false)
{
    public static CapacitySnapshot Empty { get; } = new(0, 0, []);
}

/// <summary>
/// The system drive as one strip: space used by everything else, then each category's cleanable space, the ticked
/// part last and striped (it is about to become free), then free space. When a clean finishes the strip morphs from
/// the old layout to the new one, so the reclaimed space visibly joins the free part.
/// </summary>
public sealed class CapacityBar : FrameworkElement
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot), typeof(CapacitySnapshot), typeof(CapacityBar),
        new FrameworkPropertyMetadata(CapacitySnapshot.Empty, FrameworkPropertyMetadataOptions.AffectsRender,
            (owner, _) => ((CapacityBar)owner).Relayout()));

    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(CapacityBar),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Stripes = CreateStripes();

    private List<Piece> _from = [];
    private List<Piece> _to = [];

    public CapacitySnapshot Snapshot
    {
        get => (CapacitySnapshot)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    private double Progress => (double)GetValue(ProgressProperty);

    private void Relayout()
    {
        var next = Layout(Snapshot);
        bool animate = Snapshot.Morph && _to.Count > 0 && SystemParameters.ClientAreaAnimation;
        _from = animate ? Blend(_from, _to, Progress) : next;
        _to = next;
        if (!animate)
        {
            BeginAnimation(ProgressProperty, null);
            SetValue(ProgressProperty, 1.0);
            return;
        }

        BeginAnimation(ProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            BeginTime = TimeSpan.FromMilliseconds(120),
        });
    }

    /// <summary>Pieces as fractions of the whole drive.</summary>
    private static List<Piece> Layout(CapacitySnapshot snapshot)
    {
        if (snapshot.Total <= 0)
        {
            return [];
        }

        double cleanable = snapshot.Segments.Sum(segment => segment.Bytes);
        // A rule may find files on another drive (a temp folder moved to D:, say); never draw past what is used.
        double scale = cleanable > snapshot.Used && cleanable > 0 ? snapshot.Used / cleanable : 1;
        double other = Math.Max(0, snapshot.Used - cleanable * scale);

        var pieces = new List<Piece> { new(-1, false, Colors.Transparent, other / snapshot.Total) };
        for (int index = 0; index < snapshot.Segments.Count; index++)
        {
            var segment = snapshot.Segments[index];
            pieces.Add(new Piece(index, false, segment.Color, (segment.Bytes - segment.SelectedBytes) * scale / snapshot.Total));
            pieces.Add(new Piece(index, true, segment.Color, segment.SelectedBytes * scale / snapshot.Total));
        }

        return pieces;
    }

    private static List<Piece> Blend(List<Piece> from, List<Piece> to, double t)
    {
        if (t >= 1 || from.Count == 0)
        {
            return to;
        }

        var result = new List<Piece>(to.Count + from.Count);
        foreach (var target in to)
        {
            double start = from.FirstOrDefault(piece => piece.SameSlot(target))?.Width ?? 0;
            result.Add(target with { Width = start + (target.Width - start) * t });
        }

        // Pieces that disappear (a cleaned category) shrink to nothing instead of vanishing at once.
        foreach (var gone in from.Where(piece => !to.Any(piece.SameSlot)))
        {
            result.Add(gone with { Width = gone.Width * (1 - t) });
        }

        return result;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, 30);

    protected override void OnRender(DrawingContext drawing)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var track = new Rect(0, 0, width, height);
        var free = (Brush?)TryFindResource("ControlFillColorSecondaryBrush") ?? Brushes.WhiteSmoke;
        var outline = (Brush?)TryFindResource("ControlStrokeColorDefaultBrush") ?? Brushes.Gray;
        var used = (Brush?)TryFindResource("ControlStrongFillColorDisabledBrush") ?? Brushes.DarkGray;
        var gap = (Brush?)TryFindResource("SolidBackgroundFillColorBaseBrush") ?? Brushes.White;

        drawing.PushClip(new RectangleGeometry(track, 8, 8));
        drawing.DrawRectangle(free, null, track);

        double x = 0;
        foreach (var piece in Blend(_from, _to, Progress).OrderBy(piece => piece.Order))
        {
            double w = piece.Width * width;
            if (w < 0.1)
            {
                continue;
            }

            var rect = new Rect(x, 0, w, height);
            if (piece.Index < 0)
            {
                drawing.DrawRectangle(used, null, rect);
            }
            else
            {
                var color = piece.Color;
                var fill = new SolidColorBrush(piece.Selected ? color : Color.FromArgb(0x66, color.R, color.G, color.B));
                fill.Freeze();
                drawing.DrawRectangle(fill, null, rect);
                if (piece.Selected)
                {
                    drawing.DrawRectangle(Stripes, null, rect);
                }
            }

            // A hairline between neighbours keeps adjacent colours apart.
            if (w > 2)
            {
                drawing.DrawRectangle(gap, null, new Rect(x + w - 1, 0, 1, height));
            }

            x += w;
        }

        drawing.Pop();
        var border = new Pen(outline, 1);
        border.Freeze();
        drawing.DrawRoundedRectangle(null, border, new Rect(0.5, 0.5, width - 1, height - 1), 8, 8);
    }

    private static Brush CreateStripes()
    {
        var stripe = new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(0x5A, 0xFF, 0xFF, 0xFF)),
            null,
            Geometry.Parse("M0,10 L10,0 L10,4 L4,10 Z"));
        var brush = new DrawingBrush(stripe)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 10, 10),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 10, 10),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <param name="Index">Segment index; -1 for space used by everything else.</param>
    private sealed record Piece(int Index, bool Selected, Color Color, double Width)
    {
        /// <summary>Other used space first, then unticked categories, then the ticked ones next to free space.</summary>
        public int Order => Index < 0 ? -1 : (Selected ? 1000 : 0) + Index;

        public bool SameSlot(Piece other) => other.Index == Index && other.Selected == Selected;
    }
}
