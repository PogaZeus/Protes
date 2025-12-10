# 🌟 **[Protes] Pro Notes Database**  
> **A lightweight, privacy-first note-taking app with database power, built for developers and power users.**

[![.NET Framework](https://img.shields.io/badge/.NET%204.7.2-512BD4?logo=.net&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-3399FF?logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![SQLite](https://img.shields.io/badge/SQLite-003B57?logo=sqlite&logoColor=white)](https://www.sqlite.org/)
[![Local First](https://img.shields.io/badge/Local%20First-00C853?logo=offline&logoColor=white)](https://localfirstweb.dev/)

---

## 🚀 **Features**

Tired of managing hundreds of scattered `.txt` files? **Protes** brings that familiar simplicity into a powerful, organized database—without sacrificing the feel you love.

Think of it as **Notepad, upgraded**:  
- Your notes live in a clean, searchable database (`.db` or `.prote`).  
- The **Note Editor** looks and feels like classic Notepad—but now with persistence, tagging, and instant access.  
- **Import entire folders** of `.txt`, `.md`, or `.csv` files in seconds.  
- **Export any note** back to `.txt` or `.md` anytime—perfect for sharing or backup.  
- Double-click a `.prote` file to **open it instantly**—Protes will auto-import and load it if needed.

And because this is open source—you can **help shape its future**!

### 🔒 **Privacy & Control**
- **100% local by default** — your notes never leave your machine unless you choose to connect externally.
- **Optional MySQL/External DB support** — sync across devices securely when needed.
- **No telemetry, no cloud, no bloat** — just your notes, your rules.
- **Scan for Txt, Md or CSV files** — just your notes, your rules.

### 🧰 **Powerful UI**
- **Fully customizable toolbar** — show/hide Connect, Local DB, ACOS, Import/Export, Search, and an easter egg to find!.
- **Real-time UI updates** — toggle settings anywhere (menu, SettingsWindow) and see changes **instantly** — no restart required.
- **System tray integration** — minimize to tray, close to tray, or quit fully. Your choice.
- **Easter eggs & fun** — 2 clicks away from an extra fun button!

### 🎨 **Personalized Experience**
- **Per-window font settings**:
  - `MainWindow`: Database View of your notes, Independent control over `FontFamily`
  - `NoteEditorWindow`: Same as oldschool Notepad (better font picker?)
- **Zoomable DataGrid** - instead of Font size (MainWindow)
- **Persistent UI state** — column visibility, toolbar layout, window positions, and more saved between sessions.

### 📦 **Database Management**
- **Local `.db` / `.prote` file support** — with auto-numbered exports to avoid overwrites (`Note.txt`, `Note(1).txt`, etc.).
- **One-instance enforcement** — opening a `.prote` file activates the existing app window and prompts to switch databases.
- **Database switching** — right-click system tray icon to switch between available local databases (with current DB marked).
- **Safe file associations** — `.Prote` extension linked to your app.

### ⌨️ **Efficiency & Workflow**
- **Global keyboard shortcuts**:
  - `Ctrl+N` — New note (works from tags/title/content boxes)
  - `F3` / `Shift+F3` — Find next/previous
  - `F5` — Insert current date & time
  - `Ctrl +/- / 0` — Zoom in/out/reset
- **Multi-select notes** — bulk copy, delete, or export.
- **Inline editing** — edit title/tags directly in the DataGrid.

---

## 🧩 **Architecture Highlights**

- **.NET 4.7.2 + C# 7.3 + WPF** — clean, responsive, and maintainable.
- **Single-instance app** — prevents duplicate windows; file opens trigger database switch prompts.
- **SettingsManager** — wraps `Properties.Settings` with shared instance pattern for live sync across windows.
- **Modular UI**:
  - `MainWindow` — core app with DataGrid, toolbar, status bar.
  - `NoteEditorWindow` — standalone rich-text editor (opens independently from tray).
  - `SettingsWindow` — organized into **Application**, **Toolbar**, **Database**, **Notifications** tabs.
  - `CatWindow` — hidden playful dialog (unlocked via Easter egg).
- **Event-driven refresh** — `OnSettingsChanged` callbacks ensure instant UI updates without restarts.

---

## 📁 **File Structure (Key Files)**

```
Protes/
├── MainWindow.xaml.cs          # Core app logic, toolbar, DataGrid, event handlers
├── NoteEditorWindow.xaml.cs    # Standalone note editor (opens from tray/menu)
├── SettingsWindow.xaml.cs      # Unified settings with live preview & save
├── CatWindow.xaml.cs           # Easter egg window with toolbar toggle
├── AboutWindow.xaml.cs         # About dialog
├── SettingsManager.cs          # Wrapper for user settings (shared instance)
├── Models/                     # NoteItem, FullNote, DbFileInfo
├── Services/                   # INoteRepository, SQLite/MySQL implementations
└── Assets/
    ├── Protes_W_Trans.ico		# App icon
    ├── ProtesBlackBG.png       # About window background
    └── MrE_Clean.png           # Author nickname
```

## 💖 Support This Project

If you find Protes useful, consider:
- ⭐ Starring the repo on [GitHub](https://github.com/PogaZeus)
- ☕ Buying me a coffee via [PayPal](https://paypal.me/zxgaming)
---

## 🎯 **Vision**

> **Protes** is built for **you** — the note-taker, the self-taught creator who values control, simplicity, and likes Notepad but doesn't like 100's of text files. 
> It’s powered by SQLite, wrapped in a responsive WPF shell, and ready to grow with your workflow.

---

## 📜 License
This project is licensed under the **MIT License** — see [LICENSE](LICENSE) for details.

---

> 💡 **Protes** — Find the Easter Egg! Cats **always have the last meow**. 🐾