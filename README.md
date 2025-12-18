# 🌟 **[Protes] Pro Notes Database**  
> **A lightweight, privacy-first note-taking app with database power — built for developers, tinkerers, and anyone who’s tired of juggling endless `.txt` files.**

[![.NT Framework](https://img.shields.io/badge/.NET%204.7.2-512BD4?logo=.net&logoColor=white)](https://dotnet.microsoft.com/)  
[![WPF](https://img.shields.io/badge/WPF-3399FF?logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)  
[![SQLite](https://img.shields.io/badge/SQLite-003B57?logo=sqlite&logoColor=white)](https://www.sqlite.org/)  
[![Local First](https://img.shields.io/badge/Local%20First-00C853?logo=offline&logoColor=white)](https://localfirstweb.dev/)

---

## 🚀 **Features**

Tired of managing hundreds of scattered `.txt` files? **Protes** brings that familiar simplicity into a powerful, organized database—without sacrificing the feel you love.

Think of it as **Notepad, in a database**:  
- Your notes live in a clean, searchable file (`.db` or `.prote`) or works with external databases.  
- The **Note Editor** looks and feels like classic Notepad—but now with persistence, tagging, and instant access.  
- **Import entire folders** of `.txt`, `.md`, or `.csv` files in seconds—perfect for migrating from legacy systems.  
- **Export any note** back to `.txt` or `.md` anytime—ideal for sharing, backup, or version control.  
- Double-click a `.prote` file to **open it instantly**—Protes auto-imports and loads it if needed.

### 🔒 **Privacy & Control — Now with Gate Entry Lock & Encryption, Use at your own risk**
- **100% local by default** — your notes never leave your machine unless you choose to connect externally.  
- **Optional MySQL/External DB support** — sync across devices securely when needed.  
- **No telemetry, no cloud, no bloat** — just your notes, your rules.  
- **🛡️ Password/Lock System**: Not safe from hackers without Encryption but protects your file with a password inside Protes.  
- **🔑 AES-256 Encryption**: Encrypt *entire notes* (title, tags, and content) with a key derived from your password + salt. Encrypted notes appear as `[Encrypted Data]` in the UI.  
- **🔐 Transparent security warnings**: Clear alerts and warnings about the potential dangers of password loss.  
- **🧾 Data trail **: Encryption state is always visible—never guess whether your data is secure.

### 🧰 **Powerful, Polished (but basic) UI**
- **Fully customizable toolbar** — show/hide Connect, Local DB, ACOS, Import/Export, Search, Lock Tool, and more via Settings.  
- **Real-time UI updates** — toggle settings anywhere (menu, SettingsWindow) and see changes **instantly** — no restart required.  
- **System tray integration** — minimize to tray, close to tray, or quit fully. Your choice.  
- **Right-click context menus everywhere** — including database switching and external profile management.  

### 🎨 **Personalized Experience**
- **Per-window font settings**:  
  - `MainWindow`: Database view with independent control over `FontFamily`, `FontWeight`, and `FontStyle`.  
  - `NoteEditorWindow`: Classic Notepad feel—but with better font support and RTF-like editing.  
- **Zoomable DataGrid** — adjust note list size with `Ctrl + / - / 0` (independent of font settings).  
- **Persistent UI state** — customise the toolbar icons.

### 📦 **Smart Database Management**
- **Local `.prote` or `.db` file support** — with auto-numbered exports (`Note.txt`, `Note(1).txt`, etc.) to prevent overwrites.  
- **One-instance enforcement** — opening a `.prote` file activates the existing app window and prompts to switch databases.  
- **Database switching** — right-click system tray icon or use the toolbar dropdown to switch between local or external databases.  
- **Safe file associations** — `.prote` extension linked to your app for seamless double-click loading.  
- **External DB profiles** — save and switch between multiple MySQL connections with one click.

### ⌨️ **Efficiency & Workflow in 'Pro Note' editor** 
- **Global keyboard shortcuts**:  
  - `Ctrl+N` — New note (works from any text field)  
  - `F3` / `Shift+F3` — Find next/previous  
  - `F5` — Insert current date & time  
  - `Ctrl +/- / 0` — Zoom in/out/reset  
- **Multi-select notes** — bulk copy, delete, export, or encrypt.  
- **Inline editing** — edit title/tags directly in the DataGrid with confirmation prompts.  
- **Send To integration** — right-click any text file → **Send to → Pro Note** to open it instantly in the editor.

### ➗ **Calculator Integration**
- **Built-in calculator toolbar button** — launches an oldschool style calculator.  
- **History tracking** (last 100 calculations) with scrollable overlay.  
- **Insert results as new notes** — with full expression shown (`5 × 5 × 5 = 125`) and auto-tagged as `calculator`.  
- **Insert into existing notes** — choose from last 10 modified entries via dropdown.

---

## 📸 **Screenshots**

See Protes in action!

| Feature | Preview |
|--------|---------|
| **Settings Window** | ![Settings Example](https://www.z3u5.org/protes/SettingsExample.png) |
| **Import Files** | ![Import Example](https://www.z3u5.org/protes/ImportExample.png) |
| **Export Notes** | ![Export Example](https://www.z3u5.org/protes/ExportExample.png) |
| **Locked Database** | ![Locked Example](https://www.z3u5.org/protes/LockedExample.png) |
| **Encrypted Notes** | ![Encrypted Example](https://www.z3u5.org/protes/EncryptedExample.png) |
| **Decrypted Notes** | ![Decrypted Example](https://www.z3u5.org/protes/DecryptedExample.png) |

---

## 🧩 **Architecture Highlights**

- **.NET 4.7.2 + C# 7.3 + WPF** — clean, responsive, and maintainable.  
- **Single-instance app** — prevents duplicate windows; file opens trigger database switch prompts via named pipes (IPC).  
- **SettingsManager** — wraps `Properties.Settings` with shared instance pattern for live sync across windows.  
- **Modular UI**:  
  - `MainWindow` — core app with DataGrid, dynamic toolbar, status bar, and Gate Entry logic.  
  - `NoteEditorWindow` — standalone rich-text editor (opens independently from tray or Send To).  
  - `SettingsWindow` — organized into tabs.  
  - `CalculatorWindow` — full-featured calculator with note integration.  
- **Event-driven refresh** — `OnSettingsChanged` callbacks ensure instant UI updates without restarts.  
- **Repository pattern** — abstracted data access supports both SQLite and MySQL with zero code duplication.

🔑 AES-256 Encryption:
Each note’s title, tags, and content are encrypted individually using AES-256 in CBC mode with a unique initialization vector (IV). The encryption key is derived from your password using PBKDF2-HMAC-SHA256 with 100,000+ iterations and a per-database random salt (stored in the DB). No part of your password is ever saved. If you forget it, encrypted data cannot be recovered—by design. Non-sensitive metadata (e.g., timestamps, IDs) remains unencrypted for usability.

---

## 📁 **File Structure (Key Files)**

```text
Protes/
├── MainWindow.xaml.cs          # Core app logic, toolbar, DataGrid, Gate Entry, IPC
├── NoteEditorWindow.xaml.cs    # Standalone note editor (opens from tray/menu/Send To)
├── SettingsWindow.xaml.cs      # Unified settings with live preview & save
├── CalculatorWindow.xaml.cs    # Windows-style calculator with note integration
├── CatWindow.xaml.cs           # Easter egg window with toolbar toggle
├── AboutWindow.xaml.cs         # About dialog
├── SettingsManager.cs          # Wrapper for user settings (shared instance)
├── Models/                     # NoteItem, FullNote, DbFileInfo, ExternalDbProfile
├── Services/                   # INoteRepository, SqliteNoteRepository, MySqlNoteRepository
├── EncryptionService.cs        # AES-256 + HMAC + PBKDF2 key derivation
└── Assets/
    ├── Protes_W_Trans.ico		# App icon
    ├── ProtesBlackBG.png       # About window background
    └── MrE_Clean.png           # Author nickname
```

## 💖 Support This Project

If you find Protes useful, consider:  
- ⭐ Starring the repo on [GitHub](https://github.com/PogaZeus)  
- ☕ Buying me a coffee via [PayPal](https://paypal.me/zxgaming)
- ⭐ Visiting my current website & hosting [Z3U5](https://www.Z3U5.org)

> Please note this is my second C# application (3rd or 4th app if you count when I was young playing with C++). I made this because I wanted to prove to myself that I could—but honestly, without AI, I probably couldn’t have made this (at least not in such a short time). Because I’m already well versed in programming languages, I was able to produce this in rapid succession with AI assistance. C# is rapidly becoming one of my favorite languages, and I believe this codebase is fully C# 7.3 compatible. I’m still a newcomer to building desktop apps with these tools—but future apps are coming, and I plan to keep improving Protes!

---

## 🎯 **Future Goals & Known Issues**

### 🐞 Known Issues
- **Large data handling**: The app may appear unresponsive when importing, copying, or encrypting large amounts of data, with no visual progress feedback.
- **Progress visibility**: Current progress bars only cover file scanning during import—not the full import/encryption/export pipeline.

### 🚧 Planned Improvements
- **Enhanced progress feedback**: Show progress during large imports, exports, copy/paste, and encryption/decryption operations.
- **Add Theme System**: Light or Dark mode possibly.
- **More granular settings**: Additional user preferences for UI behavior, performance thresholds, and warning suppressions.
- **Performance optimizations**: Reduce UI lag during bulk operations and improve memory efficiency.

---

## 📜 License  
This project is licensed under the **MIT License** — see [LICENSE](LICENSE) for details.

---

> 💡 **Protes** — Find the Easter Egg! Cats **always have the last meow**. 🐾

--- 
