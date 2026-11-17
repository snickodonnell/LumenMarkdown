# Lumen Markdown

A dependency-free, native Windows Markdown reader focused on fast startup and very large files.

## Features

- File-backed, virtualized rendering: only visible lines are decoded and drawn.
- Light and dark themes (`Ctrl+T`), remembered between launches.
- Headings, emphasis, strikethrough, inline code, links, quotes, nested lists, tasks, rules, tables, and fenced code.
- Tables with column widths shared across every row, sized to their content, and honoring `:---:` / `---:` alignment.
- Drag and drop, `Ctrl+O`, `Home`, and `End` keyboard support.
- Closable tabs with multi-select Open and multi-file drag and drop.
- Fast in-document search (`Ctrl+F`) with match navigation and visible highlights.
- A lightweight heading outline (`Ctrl+Shift+O`) with one-click section jumps.
- Session restoration for open tabs, active tab, scroll positions, window bounds, and theme.
- Tab navigation with `Ctrl+Tab`, `Ctrl+Shift+Tab`, `Ctrl+1`–`Ctrl+9`, and middle-click close.
- Relative Markdown links, local-file links, and same-document heading anchors.
- Automatic external-file reload with a brief, unobtrusive update toast.
- Adaptive horizontal scrolling for wide tables and long code lines.
- Single-instance Explorer integration: additional files open as tabs in the existing window.
- A custom Lumen icon embedded in the app, shortcuts, installer, and file registration.
- File menu with Open, recent files, Save As, reload, reveal, copy path, Copy Raw Markdown, tab closing, and Exit commands.
- A bounded 12-file recent list stored locally with no startup scanning.
- No browser runtime and no third-party packages.

## Build

```powershell
dotnet build LumenMarkdown.slnx -c Release
dotnet run --project tests/LumenMarkdown.Tests -c Release
dotnet publish src/LumenMarkdown/LumenMarkdown.csproj -c Release -r win-x64 --self-contained false -p:DebugType=None -o dist
```

The deployed reader is about 437 KB including the multi-resolution icon, and uses the installed .NET Windows Desktop runtime.

The app icon assets can be regenerated from `src/LumenMarkdown/Assets/LumenMarkdown-source.jpg` with `tools/Generate-AppIcon.ps1`.

## Installer

Run `./build-installer.ps1` to build the MSI. It installs per-user, appears in Windows Installed Apps, adds a Start-menu shortcut, supports upgrades/uninstall, and registers `.md` and `.markdown` files.
