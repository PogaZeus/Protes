using System;

namespace Protes
{
    public class SettingsManager
    {
        // ===== DATABASE MODE =====
        public string DatabaseModeSetting
        {
            get => global::Protes.Properties.Settings.Default.DatabaseMode;
            set
            {
                global::Protes.Properties.Settings.Default.DatabaseMode = value;
                Save();
            }
        }

        public string LastLocalDatabasePath
        {
            get => global::Protes.Properties.Settings.Default.LastLocalDatabasePath;
            set
            {
                global::Protes.Properties.Settings.Default.LastLocalDatabasePath = value;
                Save();
            }
        }

        // ===== EXTERNAL DATABASE =====
        public string External_Host
        {
            get => global::Protes.Properties.Settings.Default.External_Host;
            set
            {
                global::Protes.Properties.Settings.Default.External_Host = value;
                Save();
            }
        }

        public string External_Port
        {
            get => global::Protes.Properties.Settings.Default.External_Port;
            set
            {
                global::Protes.Properties.Settings.Default.External_Port = value;
                Save();
            }
        }

        public string External_Database
        {
            get => global::Protes.Properties.Settings.Default.External_Database;
            set
            {
                global::Protes.Properties.Settings.Default.External_Database = value;
                Save();
            }
        }

        public string External_Username
        {
            get => global::Protes.Properties.Settings.Default.External_Username;
            set
            {
                global::Protes.Properties.Settings.Default.External_Username = value;
                Save();
            }
        }

        public string External_Password
        {
            get => global::Protes.Properties.Settings.Default.External_Password;
            set
            {
                global::Protes.Properties.Settings.Default.External_Password = value;
                Save();
            }
        }

        // ===== CONNECTION BEHAVIOR =====
        public bool AutoConnect
        {
            get => global::Protes.Properties.Settings.Default.AutoConnect;
            set
            {
                global::Protes.Properties.Settings.Default.AutoConnect = value;
                Save();
            }
        }

        public bool AutoDisconnectOnSwitch
        {
            get => global::Protes.Properties.Settings.Default.AutoDisconnectOnSwitch;
            set
            {
                global::Protes.Properties.Settings.Default.AutoDisconnectOnSwitch = value;
                Save();
            }
        }

        public bool AutoConnectOnSwitch
        {
            get => global::Protes.Properties.Settings.Default.AutoConnectOnSwitch;
            set
            {
                global::Protes.Properties.Settings.Default.AutoConnectOnSwitch = value;
                Save();
            }
        }

        public bool ShowNotifications
        {
            get => global::Protes.Properties.Settings.Default.ShowNotifications;
            set
            {
                global::Protes.Properties.Settings.Default.ShowNotifications = value;
                Save();
            }
        }

        public bool NotifyDeleted
        {
            get => global::Protes.Properties.Settings.Default.NotifyDeleted;
            set
            {
                global::Protes.Properties.Settings.Default.NotifyDeleted = value;
                Save();
            }
        }
        public bool ShellNewIntegrationEnabled
        {
            get => Properties.Settings.Default.ShellNewIntegrationEnabled;
            set
            {
                Properties.Settings.Default.ShellNewIntegrationEnabled = value;
                Save();
            }
        }
        public bool LaunchOnStartup
        {
            get => Properties.Settings.Default.LaunchOnStartup;
            set
            {
                Properties.Settings.Default.LaunchOnStartup = value;
                Save();
            }
        }
        public bool SendToIntegrationEnabled
        {
            get => Properties.Settings.Default.SendToIntegrationEnabled;
            set
            {
                Properties.Settings.Default.SendToIntegrationEnabled = value;
                Save();
            }
        }
        public bool MinimizeToTray
        {
            get => Properties.Settings.Default.MinimizeToTray;
            set
            {
                Properties.Settings.Default.MinimizeToTray = value;
                Save();
            }
        }
        public bool CloseToTray
        {
            get => Properties.Settings.Default.CloseToTray;
            set
            {
                Properties.Settings.Default.CloseToTray = value;
                Save();
            }
        }

        public bool NotifyCopied
        {
            get => global::Protes.Properties.Settings.Default.NotifyCopied;
            set
            {
                global::Protes.Properties.Settings.Default.NotifyCopied = value;
                Save();
            }
        }

        public bool NotifyPasted
        {
            get => global::Protes.Properties.Settings.Default.NotifyPasted;
            set
            {
                global::Protes.Properties.Settings.Default.NotifyPasted = value;
                Save();
            }
        }

        // ===== VIEW PREFERENCES =====
        public bool ViewMainWindowTitle
        {
            get => global::Protes.Properties.Settings.Default.ViewMainWindowTitle;
            set
            {
                global::Protes.Properties.Settings.Default.ViewMainWindowTitle = value;
                Save();
            }
        }

        public bool ViewMainWindowTags
        {
            get => global::Protes.Properties.Settings.Default.ViewMainWindowTags;
            set
            {
                global::Protes.Properties.Settings.Default.ViewMainWindowTags = value;
                Save();
            }
        }

        public bool ViewMainWindowMod
        {
            get => global::Protes.Properties.Settings.Default.ViewMainWindowMod;
            set
            {
                global::Protes.Properties.Settings.Default.ViewMainWindowMod = value;
                Save();
            }
        }

        // ===== ZOOM =====
        public double DataGridZoom
        {
            get => global::Protes.Properties.Settings.Default.DataGridZoom;
            set
            {
                global::Protes.Properties.Settings.Default.DataGridZoom = value;
                Save();
            }
        }

        // ===== TOOLBAR SUBMENU VISIBILITY =====
        public bool ViewToolbarConnect
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarConnect;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarConnect = value;
                Save();
            }
        }
        public bool ViewToolbarLocalDB
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarLocalDB;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarLocalDB = value;
                Save();
            }
        }

        public bool ViewToolbarACOS
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarACOS;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarACOS = value;
                Save();
            }
        }

        public bool ViewToolbarImpEx
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarImpEx;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarImpEx = value;
                Save();
            }
        }
        public bool ViewToolbarSearch
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarSearch;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarSearch = value;
                Save();
            }
        }

        public bool ViewMainToolbar
        {
            get => global::Protes.Properties.Settings.Default.ViewMainToolbar;
            set
            {
                global::Protes.Properties.Settings.Default.ViewMainToolbar = value;
                Save();
            }
        }
        public bool ViewToolbarOptionsInMenu
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarOptionsInMenu;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarOptionsInMenu = value;
                Save();
            }
        }
        public bool ViewToolbarCat
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarCat;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarCat = value;
                Save();
            }
        }

        // ===== PERSISTENCE =====
        public void Save()
        {
            global::Protes.Properties.Settings.Default.Save();
        }

        // ===== HELPER: Convert stored string ↔ enum =====
        public DatabaseMode GetDatabaseMode()
        {
            switch (DatabaseModeSetting)
            {
                case "Local":
                    return DatabaseMode.Local;
                case "External":
                    return DatabaseMode.External;
                default:
                    return DatabaseMode.Local;
            }
        }

        public void SetDatabaseMode(DatabaseMode mode)
        {
            DatabaseModeSetting = mode == DatabaseMode.Local ? "Local" : "External";
        }
        // ===== DEFAULT MAIN WINDOW FONT =====
        public string DefaultMainFontFamily
        {
            get => global::Protes.Properties.Settings.Default.DefaultMainFontFamily;
            set
            {
                global::Protes.Properties.Settings.Default.DefaultMainFontFamily = value;
                Save();
            }
        }

        public string DefaultMainFontWeight
        {
            get => global::Protes.Properties.Settings.Default.DefaultMainFontWeight;
            set
            {
                global::Protes.Properties.Settings.Default.DefaultMainFontWeight = value;
                Save();
            }
        }

        public string DefaultMainFontStyle
        {
            get => global::Protes.Properties.Settings.Default.DefaultMainFontStyle;
            set
            {
                global::Protes.Properties.Settings.Default.DefaultMainFontStyle = value;
                Save();
            }
        }

        // ===== DEFAULT NOTE EDITOR FONT =====
        public string DefaultNoteEditorFontFamily
        {
            get => global::Protes.Properties.Settings.Default.DefaultNoteEditorFontFamily;
            set
            {
                global::Protes.Properties.Settings.Default.DefaultNoteEditorFontFamily = value;
                Save();
            }
        }

        public double DefaultNoteEditorFontSize
        {
            get => global::Protes.Properties.Settings.Default.DefaultNoteEditorFontSize;
            set
            {
                global::Protes.Properties.Settings.Default.DefaultNoteEditorFontSize = value;
                Save();
            }
        }

        public string DefaultNoteEditorFontWeight
        {
            get => global::Protes.Properties.Settings.Default.DefaultNoteEditorFontWeight;
            set
            {
                global::Protes.Properties.Settings.Default.DefaultNoteEditorFontWeight = value;
                Save();
            }
        }

        public string DefaultNoteEditorFontStyle
        {
            get => global::Protes.Properties.Settings.Default.DefaultNoteEditorFontStyle;
            set
            {
                global::Protes.Properties.Settings.Default.DefaultNoteEditorFontStyle = value;
                Save();
            }
        }


        // ===== MISCELLANEOUS =====
        public string DefaultDatabaseFolder
        {
            get => global::Protes.Properties.Settings.Default.DefaultDatabaseFolder;
            set
            {
                global::Protes.Properties.Settings.Default.DefaultDatabaseFolder = value;
                Save();
            }
        }

        public string ImportedDatabasePaths
        {
            get => global::Protes.Properties.Settings.Default.ImportedDatabasePaths;
            set
            {
                global::Protes.Properties.Settings.Default.ImportedDatabasePaths = value;
                Save();
            }
        }
    }
}