# Windows companion surfaces roadmap

Qsearch 0.1 includes a frameless native compact window (420×180 initial size), tray menu, and global shortcut. It shares search state with the main window and is not a Windows Widgets Board provider.

Before considering Widgets Board integration:

- finish persistent scopes and continuous index updates;
- measure idle memory and wakeups;
- complete keyboard, high-contrast, reduced-motion, and scaling checks;
- evaluate current Microsoft provider requirements and distribution constraints;
- preserve the same offline/no-account boundary;
- avoid adding a web runtime solely for a widget.

Any future provider should be optional and should not change first-launch consent or start a second indexer.
