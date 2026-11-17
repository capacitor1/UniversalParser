using UniversalParser.Core.Model;

namespace UniversalParser.App.Controls;

/// <summary>
/// Panel that supports the legacy semi-transparent white loading box. A stock Panel ignores alpha
/// backgrounds, which is why the old overlay sometimes painted solid white.
/// </summary>
internal sealed class OverlayPanel : Panel
{
    public OverlayPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
    }
}

/// <summary>
/// Loading/progress overlay. Progress is throttled so a fast parser cannot flood the UI thread with
/// thousands of invalidation requests.
/// </summary>
internal sealed class LoadingOverlay
{
    private readonly Form _owner;
    private readonly OverlayPanel _panel;
    private readonly ProgressBar _progressBar;
    private readonly Label _title;
    private readonly Label _info;
    private readonly Button _cancelButton;

    private Action? _onCancel;
    private long _lastUpdateTick;
    private ulong _lastBytes;
    private double _bytesPerSecond;

    public LoadingOverlay(
        Form owner,
        OverlayPanel panel,
        ProgressBar progressBar,
        Label title,
        Label info,
        Button cancelButton)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        _panel = panel;
        _progressBar = progressBar;
        _title = title;
        _info = info;
        _cancelButton = cancelButton;
    }

    public void SetCancelHandler(Action? onCancel)
    {
        _onCancel = onCancel;
        _cancelButton.Click -= CancelButtonClick;
        if (onCancel is not null)
        {
            _cancelButton.Click += CancelButtonClick;
        }
    }

    public void Show(string? title = null)
    {
        if (_owner.ClientSize.Width > 0 && _owner.ClientSize.Height > 0)
        {
            var x = Math.Max(0, (_owner.ClientSize.Width - _panel.Width) / 2);
            var y = Math.Max(0, (_owner.ClientSize.Height - _panel.Height) / 2);
            _panel.Location = new Point(x, y);
        }

        _title.Text = title ?? "Loading...";
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 30;
        _progressBar.Value = 0;
        _info.Text = "Working...";
        _lastUpdateTick = 0;
        _lastBytes = 0;
        _bytesPerSecond = 0;
        _panel.Visible = true;
        _panel.BringToFront();
    }

    public void Hide()
    {
        _panel.Visible = false;
        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Value = 0;
        _lastUpdateTick = 0;
        _bytesPerSecond = 0;
    }

    public void UpdateProgress(ParseProgress progress)
    {
        // The parser reports in chunks, so start with an animated marquee; switch to a real
        // percentage as soon as the first byte position arrives.
        if (_progressBar.Style != ProgressBarStyle.Continuous && progress.Processed > 0)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 0;
        }

        var now = Environment.TickCount64;
        var fraction = progress.Fraction ?? 0;
        var bytes = progress.Processed > 0 ? (ulong)progress.Processed : 0;

        if (_lastUpdateTick != 0)
        {
            var elapsed = now - _lastUpdateTick;
            if (elapsed < 80 && fraction < 1d)
            {
                return;
            }

            if (elapsed > 0 && bytes >= _lastBytes)
            {
                _bytesPerSecond = (bytes - _lastBytes) * 1000.0 / elapsed;
            }
        }

        _lastUpdateTick = now;
        _lastBytes = bytes;

        var value = (int)Math.Clamp(fraction * _progressBar.Maximum, 0, _progressBar.Maximum);
        if (_progressBar.Value != value)
        {
            _progressBar.Value = value;
        }

        var total = progress.Total > 0 ? FormatBytes((ulong)progress.Total) : "?";
        _info.Text = $"{FormatBytes(bytes)} / {total} @ {FormatBytes((ulong)Math.Max(0, _bytesPerSecond))}/s";
        if (!string.IsNullOrEmpty(progress.Stage))
        {
            _title.Text = $"Loading... ({progress.Stage})";
        }
    }

    private void CancelButtonClick(object? sender, EventArgs e) => _onCancel?.Invoke();

    private static string FormatBytes(ulong bytes)
    {
        const double kb = 1024.0;
        const double mb = kb * 1024.0;
        const double gb = mb * 1024.0;
        if (bytes >= (ulong)gb)
        {
            return $"{bytes / gb:0.00}GB";
        }

        if (bytes >= (ulong)mb)
        {
            return $"{bytes / mb:0.00}MB";
        }

        if (bytes >= (ulong)kb)
        {
            return $"{bytes / kb:0.00}KB";
        }

        return $"{bytes}B";
    }
}
