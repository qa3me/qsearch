namespace Qsearch.App.Services;

public enum TrayPointerButton
{
    Left,
    Right,
    Middle,
}

public sealed class TrayClickRouter
{
    private readonly Action _togglePopover;

    public TrayClickRouter(Action togglePopover)
    {
        ArgumentNullException.ThrowIfNull(togglePopover);
        _togglePopover = togglePopover;
    }

    public void Handle(TrayPointerButton button, int clickCount)
    {
        if (button == TrayPointerButton.Left && clickCount == 1)
        {
            _togglePopover();
        }
    }
}
