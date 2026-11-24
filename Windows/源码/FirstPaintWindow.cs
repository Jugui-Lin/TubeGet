using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TubeGet
{
    internal class FirstPaintWindow : Form
    {
        private const int LayeredWindow = 0x00080000;
        private const uint LayeredAlpha = 0x00000002;
        private const int WindowPositionChanging = 0x0046;
        private const int WindowSize = 0x0005;
        private const int SizeRestored = 0;
        private const int SizeMinimized = 1;
        private const int SizeMaximized = 2;
        private bool pendingReveal;
        private bool wasMinimized;

        internal FirstPaintWindow()
        {
            DoubleBuffered = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= LayeredWindow;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            pendingReveal = false;
            HoldFrame(Handle);
            base.OnHandleCreated(e);
            if (IsHandleCreated && !IsDisposed && Visible) BeginInvoke(new Action(Reveal));
        }

        protected override void SetVisibleCore(bool value)
        {
            bool showing = value && !Visible;
            if (showing && IsHandleCreated) HoldFrame(Handle);

            base.SetVisibleCore(value);

            if (showing) Reveal();
        }

        protected override void WndProc(ref Message message)
        {
            // Explorer can restore the native window without calling Form.Show or setting WindowState.
            if (message.Msg == WindowPositionChanging && wasMinimized && !IsIconic(message.HWnd))
                HoldFrame(message.HWnd);

            bool restoring = false;
            if (message.Msg == WindowSize)
            {
                int state = message.WParam.ToInt32();
                if (state == SizeMinimized) wasMinimized = true;
                else if (state == SizeRestored || state == SizeMaximized)
                {
                    restoring = wasMinimized;
                    wasMinimized = false;
                    if (restoring) HoldFrame(message.HWnd);
                }
            }

            base.WndProc(ref message);
            if (restoring && IsHandleCreated && !IsDisposed) BeginInvoke(new Action(Reveal));
        }

        private void HoldFrame(IntPtr window)
        {
            if (pendingReveal) return;
            pendingReveal = true;
            if (!SetLayeredWindowAttributes(window, 0, 0, LayeredAlpha)) pendingReveal = false;
        }

        private void Reveal()
        {
            if (!pendingReveal || IsDisposed || !IsHandleCreated || !Visible || IsIconic(Handle)) return;
            try
            {
                PerformLayout();
                PagePainting.Repaint(this);
            }
            finally
            {
                // Keep the same layered surface so Windows presents the frame already painted above.
                pendingReveal = false;
                if (IsHandleCreated) SetLayeredWindowAttributes(Handle, 0, 255, LayeredAlpha);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed) OnFirstPaintComplete();
            }));
        }

        protected virtual void OnFirstPaintComplete() { }

        internal bool SetTransitionOpacity(byte opacity)
        {
            return IsHandleCreated && !pendingReveal &&
                SetLayeredWindowAttributes(Handle, 0, opacity, LayeredAlpha);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);
    }
}
