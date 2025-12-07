using Protes.Views;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace Protes
{
    public partial class App : Application
    {
        private const string MutexName = "Protes_Singleton_v1";
        private const string PipeName = "Protes_IPC_Pipe_v1";
        private Mutex _mutex; // 👈 Store mutex as field to keep it alive

        protected override void OnStartup(StartupEventArgs e)
        {
            bool firstInstance;
            _mutex = new Mutex(true, MutexName, out firstInstance);

            if (!firstInstance)
            {
                // Send args to first instance via named pipe
                SendToFirstInstance(e.Args);
                Shutdown();
                return;
            }

            // Start pipe server in background
            ThreadPool.QueueUserWorkItem(_ => ListenForRequests());

            base.OnStartup(e);

            var mainWindow = new MainWindow();
            mainWindow.Title = "Mr E Tools - [Protes] Pro Notes Database";
            mainWindow.Show();

            // 👇 Handle command-line args if .prote file was double-clicked or -new command
            if (e.Args.Length > 0)
            {
                mainWindow.HandleIpcMessage(e.Args[0]);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Clean up mutex
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private void SendToFirstInstance(string[] args)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(2000); // 2-second timeout
                    using (var writer = new StreamWriter(client) { AutoFlush = true })
                    {
                        if (args.Length > 0)
                        {
                            // Send the file path or command
                            writer.WriteLine(args[0]);
                        }
                        else
                        {
                            writer.WriteLine("!ACTIVATE");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log or show error if needed
                System.Diagnostics.Debug.WriteLine($"Failed to send to first instance: {ex.Message}");
            }
        }

        private void ListenForRequests()
        {
            while (true)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In))
                    {
                        server.WaitForConnection();
                        using (var reader = new StreamReader(server))
                        {
                            string message = reader.ReadLine();

                            if (string.IsNullOrEmpty(message))
                                continue;

                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                var mainWindow = Current.MainWindow as MainWindow;
                                if (mainWindow != null)
                                {
                                    if (message == "!ACTIVATE")
                                    {
                                        mainWindow.ActivateWindow();
                                    }
                                    else if (message == "-new")
                                    {
                                        // Don't activate window for new note command
                                        // Just handle the message directly
                                        mainWindow.HandleIpcMessage(message);
                                    }
                                    else
                                    {
                                        // For file paths and other commands, activate first
                                        mainWindow.ActivateWindow();
                                        mainWindow.HandleIpcMessage(message);
                                    }
                                }
                            }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log pipe errors but continue listening
                    System.Diagnostics.Debug.WriteLine($"Pipe error: {ex.Message}");
                    Thread.Sleep(100); // Brief pause before retry
                }
            }
        }

    }
}