// Crash protection: every unhandled error is logged to %APPDATA%\LiteCap\logs and shown as a
// short, friendly message instead of the raw .NET dialog. The app keeps running when it can.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LiteCap
{
    static class Crash
    {
        static bool showing;
        static DateTime lastShown = DateTime.MinValue;
        static readonly object gate = new object();

        public static string LogFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteCap", "logs"); }
        }

        public static void Install()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Report(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Log(ex, "fatal");
                if (e.IsTerminating)
                {
                    try { MessageBox.Show(L.T("crash_fatal"), L.T("app"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
                    catch { }
                }
            };
        }

        // Appends one entry to this month's log file. Never throws.
        public static void Log(Exception ex, string context)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(LogFolder);
                    string file = Path.Combine(LogFolder, "litecap-" + DateTime.Now.ToString("yyyy-MM") + ".log");
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + context + "]");
                    string scale = "?";
                    try { scale = Ui.S.ToString("0.00"); } catch { }
                    sb.AppendLine("LiteCap " + Ui.Version + "  |  " + Environment.OSVersion + "  |  CLR " + Environment.Version
                        + "  |  " + (Environment.Is64BitProcess ? "x64" : "x86") + "  |  lang " + L.Lang + "  |  scale " + scale);
                    sb.AppendLine(ex == null ? "(no exception object)" : ex.ToString());
                    sb.AppendLine();
                    File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
                }
            }
            catch { }
        }

        // Logs the error, closes any capture overlay it may have left open, and tells the user once.
        public static void Report(Exception ex)
        {
            Log(ex, "ui");
            CloseOverlays();

            if (showing || (DateTime.Now - lastShown).TotalSeconds < 3) return;
            showing = true;
            try
            {
                DialogResult r = MessageBox.Show(L.T("crash_msg"), L.T("app"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly);
                if (r == DialogResult.Yes) OpenLogFolder();
            }
            catch { }
            finally
            {
                showing = false;
                lastShown = DateTime.Now;
            }
        }

        public static void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                Process.Start("explorer.exe", "\"" + LogFolder + "\"");
            }
            catch { }
        }

        static void CloseOverlays()
        {
            try
            {
                List<Form> stuck = new List<Form>();
                foreach (Form f in Application.OpenForms)
                    if (f is SelectOverlay) stuck.Add(f);
                foreach (Form f in stuck)
                {
                    f.DialogResult = DialogResult.Cancel;
                    f.Close();
                }
            }
            catch { }
        }
    }
}
