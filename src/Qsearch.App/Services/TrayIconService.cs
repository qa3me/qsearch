using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Qsearch.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayClickRouter _clickRouter;
    private Icon? _ownedIcon;
    private bool _disposed;

    public TrayIconService(
        Action togglePopover,
        Action openMain,
        Action openAbout,
        Action rebuildIndex,
        Action exit,
        string? iconPath = null)
    {
        ArgumentNullException.ThrowIfNull(togglePopover);
        ArgumentNullException.ThrowIfNull(openMain);
        ArgumentNullException.ThrowIfNull(openAbout);
        ArgumentNullException.ThrowIfNull(rebuildIndex);
        ArgumentNullException.ThrowIfNull(exit);

        _clickRouter = new TrayClickRouter(togglePopover);
        ContextMenuStrip menu = new();
        menu.Items.Add("Quick search / جست‌وجوی سریع", null, (_, _) => togglePopover());
        menu.Items.Add("Open Qsearch / باز کردن", null, (_, _) => openMain());
        menu.Items.Add("About / درباره", null, (_, _) => openAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Rebuild local index / نوسازی فهرست", null, (_, _) => rebuildIndex());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit / خروج", null, (_, _) => exit());
        if (iconPath is not null && !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            _ownedIcon = new Icon(iconPath);
        }

        _notifyIcon = new NotifyIcon
        {
            Text = "Qsearch — fully offline file search",
            Icon = _ownedIcon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += OnMouseClick;
    }

    public void UpdateIcon(string iconPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        if (!File.Exists(iconPath))
        {
            return;
        }

        Icon next = new(iconPath);
        Icon? previous = _ownedIcon;
        _ownedIcon = next;
        _notifyIcon.Icon = next;
        previous?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _ownedIcon?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnMouseClick(object? sender, MouseEventArgs eventArgs)
    {
        TrayPointerButton button = eventArgs.Button switch
        {
            MouseButtons.Left => TrayPointerButton.Left,
            MouseButtons.Middle => TrayPointerButton.Middle,
            _ => TrayPointerButton.Right,
        };
        _clickRouter.Handle(button, eventArgs.Clicks);
    }
}
