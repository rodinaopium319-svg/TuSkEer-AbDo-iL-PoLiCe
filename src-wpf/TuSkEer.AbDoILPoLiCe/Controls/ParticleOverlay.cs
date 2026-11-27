using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace TuSkEer.AbDoILPoLiCe.Controls;

public sealed class ParticleOverlay : FrameworkElement
{
    private sealed class Particle
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
        public double Radius;
        public Brush Brush = Brushes.White;
    }

    public static readonly DependencyProperty IsAnimationActiveProperty =
        DependencyProperty.Register(
            nameof(IsAnimationActive),
            typeof(bool),
            typeof(ParticleOverlay),
            new PropertyMetadata(true, OnIsAnimationActiveChanged));

    public bool IsAnimationActive
    {
        get => (bool)GetValue(IsAnimationActiveProperty);
        set => SetValue(IsAnimationActiveProperty, value);
    }

    private readonly List<Particle> _particles = new(28);
    private readonly DispatcherTimer _timer;
    private readonly Random _rng = new(2026);
    private bool _initialized;

    public ParticleOverlay()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS lightweight tick so typing thread is never disturbed
        };
        _timer.Tick += OnTick;
        Loaded += (_, _) => UpdateTimerState();
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimerState();
    }

    private static void OnIsAnimationActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ParticleOverlay overlay)
        {
            overlay.UpdateTimerState();
            overlay.InvalidateVisual();
        }
    }

    private void UpdateTimerState()
    {
        if (IsVisible && IsAnimationActive)
        {
            if (!_timer.IsEnabled) _timer.Start();
        }
        else
        {
            if (_timer.IsEnabled) _timer.Stop();
        }
    }

    private void EnsureParticles(double width, double height)
    {
        if (_initialized || width <= 0 || height <= 0) return;
        _initialized = true;

        var palette = new[]
        {
            CreateFrozenBrush(Color.FromArgb(95, 0, 212, 255)),   // Electric blue
            CreateFrozenBrush(Color.FromArgb(80, 168, 85, 247)),  // Violet
            CreateFrozenBrush(Color.FromArgb(65, 239, 68, 68))    // Deep red accent
        };

        for (int i = 0; i < 24; i++)
        {
            _particles.Add(new Particle
            {
                X = _rng.NextDouble() * width,
                Y = _rng.NextDouble() * height,
                Vx = (_rng.NextDouble() - 0.5) * 0.45,
                Vy = -0.18 - _rng.NextDouble() * 0.35,
                Radius = 1.0 + _rng.NextDouble() * 1.8,
                Brush = palette[i % palette.Length]
            });
        }
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        EnsureParticles(w, h);
        foreach (var p in _particles)
        {
            p.X += p.Vx;
            p.Y += p.Vy;

            if (p.Y < -8) p.Y = h + 6;
            if (p.X < -8) p.X = w + 6;
            if (p.X > w + 8) p.X = -6;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!IsAnimationActive) return;

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        EnsureParticles(w, h);
        foreach (var p in _particles)
        {
            dc.DrawEllipse(p.Brush, null, new Point(p.X, p.Y), p.Radius, p.Radius);
        }
    }
}
