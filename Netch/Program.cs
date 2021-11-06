﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Microsoft.VisualStudio.Threading;
using Netch.Controllers;
using Netch.Enums;
using Netch.Forms;
using Netch.Services;
using Netch.Utils;
using Serilog;
using Serilog.Events;
using SingleInstance;
#if RELEASE
using Windows.Win32.UI.WindowsAndMessaging;
#endif

namespace Netch
{
    public static class Program
    {
        public static readonly SingleInstanceService SingleInstance = new($"Global\\{nameof(Netch)}");

        internal static HWND ConsoleHwnd { get; private set; }

#pragma warning disable VSTHRD002
        // VSTHRD002: Avoid problematic synchronous waits
        // Main never re-called, so we can ignore this

        [STAThread]
        public static void Main(string[] args)
        {
            // handle arguments
            if (args.Contains(Constants.Parameter.ForceUpdate))
                Flags.AlwaysShowNewVersionFound = true;

            // set working directory
            Directory.SetCurrentDirectory(Global.NetchDir);

            // append .\bin to PATH
            var binPath = Path.Combine(Global.NetchDir, "bin");
            Environment.SetEnvironmentVariable("PATH", $"{Environment.GetEnvironmentVariable("PATH")};{binPath}");

            // check if .\bin directory exists
            if (!Directory.Exists("bin") || !Directory.EnumerateFileSystemEntries("bin").Any())
            {
                i18N.Load("System");
                MessageBoxX.Show(i18N.Translate("Please extract all files then run the program!"));
                Environment.Exit(2);
            }

            // clean up old files
            Updater.CleanOld(Global.NetchDir);

            // pre-create directories
            var directories = new[] { "mode\\Custom", "data", "i18n", "logging" };
            foreach (var item in directories)
                if (!Directory.Exists(item))
                    Directory.CreateDirectory(item);

            // load configuration
            Configuration.LoadAsync().Wait();

            // check if the program is already running
            if (!SingleInstance.IsFirstInstance)
            {
                SingleInstance.PassArgumentsToFirstInstance(args.Append(Constants.Parameter.Show));
                Environment.Exit(0);
                return;
            }

            SingleInstance.ArgumentsReceived.Subscribe(SingleInstance_ArgumentsReceived);

            // clean up old logs
            if (Directory.Exists("logging"))
            {
                var directory = new DirectoryInfo("logging");

                foreach (var file in directory.GetFiles())
                    file.Delete();

                foreach (var dir in directory.GetDirectories())
                    dir.Delete(true);
            }

            InitConsole();

            CreateLogger();

            // load i18n
            i18N.Load(Global.Settings.Language);

            // log environment information
            Task.Run(LogEnvironment).Forget();
            CheckClr();
            CheckOS();

            // handle exceptions
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_OnException;
            Application.ApplicationExit += Application_OnExit;

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(Global.MainForm);
        }

#pragma warning restore VSTHRD002

        private static void LogEnvironment()
        {
            Log.Information("Netch Version: {Version}", $"{UpdateChecker.Owner}/{UpdateChecker.Repo}@{UpdateChecker.Version}");
            Log.Information("OS: {OSVersion}", Environment.OSVersion);
            Log.Information("SHA256: {Hash}", $"{Utils.Utils.SHA256CheckSum(Global.NetchExecutable)}");
            Log.Information("System Language: {Language}", CultureInfo.CurrentCulture.Name);

#if RELEASE
            if (Log.IsEnabled(LogEventLevel.Debug))
            {
                // TODO log level setting
                Task.Run(() => Log.Debug("Third-party Drivers:\n{Drivers}", string.Join(Constants.EOF, SystemInfo.SystemDrivers(false)))).Forget();
                Task.Run(() => Log.Debug("Running Processes: \n{Processes}", string.Join(Constants.EOF, SystemInfo.Processes(false)))).Forget();
            }
#endif
        }

        private static void CheckClr()
        {
            var framework = Assembly.GetExecutingAssembly().GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
            if (framework == null)
            {
                Log.Warning("TargetFrameworkAttribute null");
                return;
            }

            var frameworkName = new FrameworkName(framework);

            if (frameworkName.Version.Major != Environment.Version.Major)
            {
                Log.Information("CLR: {Version}", Environment.Version);
                Flags.NoSupport = true;
                if (!Global.Settings.NoSupportDialog)
                    MessageBoxX.Show(
                        i18N.TranslateFormat("{0} won't get developers' support, Please do not report any issues or seek help from developers.",
                            "CLR " + Environment.Version),
                        LogLevel.WARNING);
            }
        }

        private static void CheckOS()
        {
            if (Environment.OSVersion.Version.Build < 17763)
            {
                Flags.NoSupport = true;
                if (!Global.Settings.NoSupportDialog)
                    MessageBoxX.Show(
                        i18N.TranslateFormat("{0} won't get developers' support, Please do not report any issues or seek help from developers.",
                            Environment.OSVersion),
                        LogLevel.WARNING);
            }
        }

        private static void InitConsole()
        {
            PInvoke.AllocConsole();

            ConsoleHwnd = PInvoke.GetConsoleWindow();
#if RELEASE
            // hide console window
            PInvoke.ShowWindow(ConsoleHwnd, SHOW_WINDOW_CMD.SW_HIDE);
#endif
        }

        public static void CreateLogger()
        {
            Log.Logger = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Verbose()
#else
                .MinimumLevel.Debug()
#endif
                .WriteTo.Async(c => c.File(Path.Combine(Global.NetchDir, Constants.LogFile),
                    outputTemplate: Constants.OutputTemplate,
                    rollOnFileSizeLimit: false))
                .WriteTo.Console(outputTemplate: Constants.OutputTemplate)
                .MinimumLevel.Override(@"Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .CreateLogger();
        }

        private static void Application_OnException(object sender, ThreadExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unhandled error");
        }

        private static void Application_OnExit(object? sender, EventArgs eventArgs)
        {
            Log.CloseAndFlush();
        }

        private static void SingleInstance_ArgumentsReceived(IEnumerable<string> args)
        {
            if (args.Contains(Constants.Parameter.Show))
            {
                Utils.Utils.ActivateVisibleWindows();
            }
        }
    }
}