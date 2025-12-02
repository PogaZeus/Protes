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

        public bool ViewMainToolbar
        {
            get => global::Protes.Properties.Settings.Default.ViewMainToolbar;
            set
            {
                global::Protes.Properties.Settings.Default.ViewMainToolbar = value;
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