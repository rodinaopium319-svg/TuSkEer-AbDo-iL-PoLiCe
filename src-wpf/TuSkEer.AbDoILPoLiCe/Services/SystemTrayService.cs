using System;
using System.Drawing;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TuSkEer.AbDoILPoLiCe.Services;

public sealed class SystemTrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _pauseMenuItem;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? PauseResumeRequested;
    public event Action? StopRequested;
    public event Action? ExitRequested;

    public SystemTrayService()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "TuSkEer AbDo iL PoLiCe",
            Visible = true,
            Icon = LoadTrayIcon()
        };

        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = Color.FromArgb(13, 21, 46),
            ForeColor = Color.FromArgb(236, 244, 255)
        };

        var openItem = new Forms.ToolStripMenuItem("Open TuSkEer AbDo iL PoLiCe");
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        openItem.Click += (_, _) => OpenRequested?.Invoke();

        _pauseMenuItem = new Forms.ToolStripMenuItem("Pause / Resume");
        _pauseMenuItem.Click += (_, _) => PauseResumeRequested?.Invoke();

        var stopItem = new Forms.ToolStripMenuItem("Stop");
        stopItem.Click += (_, _) => StopRequested?.Invoke();

        var exitItem = new Forms.ToolStripMenuItem("Exit Completely");
        exitItem.ForeColor = Color.FromArgb(248, 113, 113);
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(stopItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void UpdatePauseLabel(bool isPaused)
    {
        _pauseMenuItem.Text = isPaused ? "Resume Typing" : "Pause Typing";
    }

    public void ShowTrayNotification(string title, string text)
    {
        if (_disposed) return;
        _notifyIcon.ShowBalloonTip(2000, title, text, Forms.ToolTipIcon.Info);
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app_icon.ico");
            if (File.Exists(diskPath))
            {
                return new Icon(diskPath);
            }

            var uri = new Uri("pack://application:,,,/Resources/app_icon.ico", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo?.Stream is not null)
            {
                using var stream = streamInfo.Stream;
                return new Icon(stream);
            }
        }
        catch
        {
            // Fall back to executable associated icon or default application icon
        }

        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var extracted = Icon.ExtractAssociatedIcon(exePath);
                if (extracted is not null) return extracted;
            }
        }
        catch
        {
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
