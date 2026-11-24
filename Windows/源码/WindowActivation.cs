using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed class WindowActivation : NativeWindow, IDisposable
    {
        internal const string MutexName = @"Local\TubeGetDesktop";
        private const string WindowName = "TubeGetDesktop.Activation";
        private static readonly IntPtr MessageOnly = new IntPtr(-3);
        private static readonly int ActivateMessage = RegisterWindowMessage("TubeGetDesktop.Activate");
        private readonly Action show;

        internal WindowActivation(Action show)
        {
            this.show = show;
            CreateHandle(new CreateParams { Parent = MessageOnly, Caption = WindowName });
        }

        internal static bool Request()
        {
            if (ActivateMessage == 0) return false;
            IntPtr window = FindWindowEx(MessageOnly, IntPtr.Zero, null, WindowName);
            if (window == IntPtr.Zero) return false;
            uint process;
            if (GetWindowThreadProcessId(window, out process) == 0) return false;
            AllowSetForegroundWindow(process);
            return PostMessage(window, ActivateMessage, IntPtr.Zero, IntPtr.Zero);
        }

        protected override void WndProc(ref Message message)
        {
            if (ActivateMessage != 0 && message.Msg == ActivateMessage) show();
            else base.WndProc(ref message);
        }

        public void Dispose()
        {
            DestroyHandle();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string windowClass, string caption);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(uint processId);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
