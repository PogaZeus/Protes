using System;
using System.Collections.Generic;
using System.Windows;
using Newtonsoft.Json;

namespace Protes
{
    public class SettingsManager
    {
        #region Database Mode & Paths

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

        #endregion

        #region External Database
        public string ExternalConnectionsSerialized
        {
            get => global::Protes.Properties.Settings.Default.ExternalConnectionsSerialized;
            set
            {
                global::Protes.Properties.Settings.Default.ExternalConnectionsSerialized = value;
                Save();
            }
        }
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

        #endregion

        #region Connection Automation

        public bool AutoConnect
        {
            get => global::Protes.Properties.Settings.Default.AutoConnect;
            set
            {
                global::Protes.Properties.Settings.Default.AutoConnect = value;
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

        public bool AutoDisconnectOnSwitch
        {
            get => global::Protes.Properties.Settings.Default.AutoDisconnectOnSwitch;
            set
            {
                global::Protes.Properties.Settings.Default.AutoDisconnectOnSwitch = value;
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

        #endregion

        #region Application Integration

        public bool LaunchOnStartup
        {
            get => Properties.Settings.Default.LaunchOnStartup;
            set
            {
                Properties.Settings.Default.LaunchOnStartup = value;
                Save();
            }
        }
        public bool SendToNoteEditorEnabled
        {
            get => Properties.Settings.Default.SendToNoteEditorEnabled;
            set
            {
                Properties.Settings.Default.SendToNoteEditorEnabled = value;
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

        #endregion

        #region View Preferences (MainWindow)

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

        #endregion

        #region Toolbar Visibility

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
        

        public bool ViewToolbarSettings
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarSettings;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarSettings = value;
                Save();
            }
        }
        public bool ViewToolbarNoteTools
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarNoteTools;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarNoteTools = value;
                Save();
            }
        }
        public bool ViewToolbarCopyPaste
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarCopyPaste;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarCopyPaste = value;
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
        public bool ViewToolbarGateEntry
        {
            get => global::Protes.Properties.Settings.Default.ViewToolbarGateEntry;
            set
            {
                global::Protes.Properties.Settings.Default.ViewToolbarGateEntry = value;
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

        #endregion

        #region Font Settings

        // ===== Main Window Font =====

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

        // ===== Note Editor Font =====

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

        #endregion

        #region Zoom

        public double DataGridZoom
        {
            get => global::Protes.Properties.Settings.Default.DataGridZoom;
            set
            {
                global::Protes.Properties.Settings.Default.DataGridZoom = value;
                Save();
            }
        }

        #endregion

        #region Persistence & Helper Methods

        public void Save()
        {
            global::Protes.Properties.Settings.Default.Save();
        }
        // Step 2: Simulate list using current single settings

        private const string DEFAULT_EXTERNAL_JSON = "[]";

        public List<ExternalDbProfile> GetExternalDbProfiles()
        {
            try
            {
                var json = ExternalConnectionsSerialized;
                if (string.IsNullOrWhiteSpace(json) || json == "[]")
                    return new List<ExternalDbProfile>();

                return JsonConvert.DeserializeObject<List<ExternalDbProfile>>(json) ?? new List<ExternalDbProfile>();
            }
            catch
            {
                return new List<ExternalDbProfile>();
            }
        }

        public void SaveExternalDbProfiles(List<ExternalDbProfile> profiles)
        {
            try
            {
                var json = JsonConvert.SerializeObject(profiles, Formatting.Indented);
                ExternalConnectionsSerialized = json;
                Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save external connections:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Helper to write back to current settings (for Edit)
        public void SaveAsCurrentExternalConnection(ExternalDbProfile profile)
        {
            External_Host = profile.Host;
            External_Port = profile.Port.ToString();
            External_Database = profile.Database;
            External_Username = profile.Username;
            External_Password = profile.Password;
            Save();
        }
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

        #endregion
    }
}