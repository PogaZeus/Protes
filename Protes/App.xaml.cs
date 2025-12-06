using Protes.Views;
using System;
using System.IO;
using System.Windows;

namespace Protes
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string[] args = e.Args;

            // Handle "New → Note Editor (Protes)" → launch NoteEditorWindow directly
            if (args.Length == 1 && args[0] == "-new")
            {
                var editor = new NoteEditorWindow();
                editor.Show();
                return;
            }

            // Handle double-click on .db file
            if (args.Length == 1 &&
                File.Exists(args[0]) &&
                args[0].EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                var mainWindow = new MainWindow();
                mainWindow.Show();

                // Delay slightly to ensure MainWindow is initialized
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(300);
                timer.Tick += (s, _) =>
                {
                    mainWindow.SwitchToLocalDatabase(args[0]);
                    timer.Stop();
                };
                timer.Start();
                return;
            }

            // Normal startup
            var main = new MainWindow();
            main.Show();
        }
    }
}