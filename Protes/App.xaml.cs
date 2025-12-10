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
        private Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool firstInstance;
            _mutex = new Mutex(true, MutexName, out firstInstance);

            if (!firstInstance)
            {
                SendToFirstInstance(e.Args);
                Shutdown();
                return;
            }

            ThreadPool.QueueUserWorkItem(_ => ListenForRequests());

            var mainWindow = new MainWindow();
            Current.MainWindow = mainWindow;
            mainWindow.Title = "[Protes] Pro Notes Database";
            mainWindow.Show();

            // Handle startup args AFTER window is ready
            if (e.Args.Length > 0)
            {
                string arg = e.Args[0];
                if (File.Exists(arg))
                {
                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        mainWindow.HandleIpcMessage(arg);
                    }));
                }
                else if (arg == "-new")
                {
                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        mainWindow.HandleIpcMessage(arg);
                    }));
                }
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
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
                    client.Connect(2000);
                    using (var writer = new StreamWriter(client) { AutoFlush = true })
                    {
                        if (args.Length > 0)
                            writer.WriteLine(args[0]);
                        else
                            writer.WriteLine("!ACTIVATE");
                    }
                }
            }
            catch (Exception ex)
            {
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
                            if (string.IsNullOrEmpty(message)) continue;

                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                var mainWindow = Current.MainWindow as MainWindow;
                                if (mainWindow != null)
                                {
                                    if (message == "!ACTIVATE")
                                    {
                                        mainWindow.ActivateWindow();
                                    }
                                    else
                                    {
                                        // 👇 THIS IS THE KEY: route ALL messages through HandleIpcMessage
                                        mainWindow.HandleIpcMessage(message);
                                    }
                                }
                            }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Pipe error: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
        }
    }
}