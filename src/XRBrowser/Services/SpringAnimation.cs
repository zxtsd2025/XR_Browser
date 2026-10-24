using System.Diagnostics;
using System.Windows.Media;

namespace XRBrowser.Services;

/// <summary>
/// A small damped-spring integrator driven by CompositionTarget.Rendering.
/// It is interruptible: whenever a new target is set, the current position and
/// velocity are carried forward instead of restarting from a fixed storyboard.
/// </summary>
public sealed class SpringAnimation
{
    private readonly Action<double, double> _apply;
    private readonly Action? _completed;
    private readonly double _stiffness;
    private readonly double _damping;
    private readonly double _mass;
    private readonly double _timeScale;

    private double _value;
    private double _velocity;
    private double _target;
    private long _lastTimestamp;
    private bool _running;

    public SpringAnimation(
        Action<double, double> apply,
        Action? completed = null,
        double stiffness = 300,
        double damping = 27,
        double mass = 1,
        double speed = 1.0)
    {
        _apply = apply;
        _completed = completed;
        _stiffness = Math.Max(1, stiffness);
        _damping = Math.Max(0, damping);
        _mass = Math.Max(0.01, mass);
        _timeScale = Math.Clamp(speed, 0.25, 3.0);
    }

    public double Value => _value;
    public double Velocity => _velocity;
    public bool IsRunning => _running;

    public void Start(double from, double to, double velocity = 0)
    {
        _value = from;
        _velocity = velocity;
        _target = to;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _apply(_value, _velocity);

        if (_running) return;
        _running = true;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
        _lastTimestamp = now;
        if (dt <= 0) return;

        // Sub-step for stability at low frame rates. CompositionTarget.Rendering
        // is vsync aligned, so this normally runs once per display refresh.
        dt = Math.Min(dt, 0.05) * _timeScale;
        var step = dt / 4.0;
        for (var i = 0; i < 4; i++)
        {
            var acceleration = (-_stiffness * (_value - _target) - _damping * _velocity) / _mass;
            _velocity += acceleration * step;
            _value += _velocity * step;
        }

        _apply(_value, _velocity);

        if (Math.Abs(_value - _target) < 0.002 && Math.Abs(_velocity) < 0.02)
        {
            _value = _target;
            _velocity = 0;
            _apply(_value, _velocity);
            Stop();
            _completed?.Invoke();
        }
    }
}
