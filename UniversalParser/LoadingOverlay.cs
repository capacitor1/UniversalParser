using System;
using System.Windows.Forms;

namespace UniversalParser
{
    // Encapsulates all loading overlay UI and state management
    // Keeps Form1 clean and focused on parser logic
    public sealed class LoadingOverlay
    {
        private Panel? _panel;
        private ProgressBar? _progressBar;
        private Label? _infoLabel;
        private Button? _cancelButton;
        private Form? _ownerForm;
        private Action? _onCancel;

        public LoadingOverlay(Form ownerForm, Panel panel, ProgressBar progressBar, Label infoLabel, Button cancelButton)
        {
            _ownerForm = ownerForm;
            _panel = panel;
            _progressBar = progressBar;
            _infoLabel = infoLabel;
            _cancelButton = cancelButton;
        }

        // Set the cancel callback
        public void SetCancelHandler(Action? onCancel)
        {
            _onCancel = onCancel;
            if (_cancelButton != null)
            {
                _cancelButton.Click -= CancelButton_Click;
                if (onCancel != null)
                {
                    _cancelButton.Click += CancelButton_Click;
                }
            }
        }

        private void CancelButton_Click(object? sender, EventArgs e)
        {
            _onCancel?.Invoke();
        }

        // Show the overlay centered on the form
        public void Show()
        {
            if (_panel == null || _ownerForm == null) return;

            // Center panel on form
            int cx = (_ownerForm.ClientSize.Width - _panel.Width) / 2;
            int cy = (_ownerForm.ClientSize.Height - _panel.Height) / 2;
            _panel.Location = new Point(Math.Max(0, cx), Math.Max(0, cy));
            _panel.BringToFront();
            _panel.Visible = true;

            // Reset progress bar
            if (_progressBar != null)
            {
                _progressBar.Value = 0;
            }
            if (_infoLabel != null)
            {
                _infoLabel.Text = "0 / 0 @ 0 B/s";
            }
        }

        // Hide the overlay
        public void Hide()
        {
            if (_panel != null)
            {
                _panel.Visible = false;
            }
            if (_progressBar != null)
            {
                _progressBar.Value = 0;
            }
        }

        // Update progress display
        public void UpdateProgress(double fraction, ulong bytesRead, ulong? totalBytes, double bytesPerSecond)
        {
            // Clamp fraction to [0, 1]
            fraction = Math.Clamp(fraction, 0.0, 1.0);

            if (_progressBar != null)
            {
                int val = (int)(fraction * 1000.0);
                val = Math.Clamp(val, 0, _progressBar.Maximum);
                _progressBar.Value = val;
            }

            if (_infoLabel != null)
            {
                string read = FormatBytes(bytesRead);
                string total = totalBytes.HasValue ? FormatBytes(totalBytes.Value) : "?";
                string speed = FormatBytes((ulong)bytesPerSecond) + "/s";
                _infoLabel.Text = $"{read} / {total} @ {speed}";
            }
        }

        private static string FormatBytes(ulong bytes)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;
            if (bytes >= (ulong)GB) return $"{bytes / GB:0.00}GB";
            if (bytes >= (ulong)MB) return $"{bytes / MB:0.00}MB";
            if (bytes >= (ulong)KB) return $"{bytes / KB:0.00}KB";
            return $"{bytes}B";
        }
    }
}
