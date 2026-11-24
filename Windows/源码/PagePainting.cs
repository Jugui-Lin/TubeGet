using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TubeGet
{
    internal static class PagePainting
    {
        private const int SetRedraw = 0x000B;
        private const uint Invalidate = 0x0001;
        private const uint AllChildren = 0x0080;
        private const uint UpdateNow = 0x0100;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);

        internal static void Repaint(Control surface)
        {
            if (!surface.IsDisposed && surface.IsHandleCreated)
                RedrawWindow(surface.Handle, IntPtr.Zero, IntPtr.Zero, Invalidate | AllChildren | UpdateNow);
        }

        internal static void Update(Control surface, Action change)
        {
            if (!surface.IsHandleCreated || !surface.Visible)
            {
                change();
                return;
            }

            IntPtr window = surface.Handle;
            surface.SuspendLayout();
            SendMessage(window, SetRedraw, IntPtr.Zero, IntPtr.Zero);
            try
            {
                change();
            }
            finally
            {
                // Re-enable painting even if a child fails to update.
                try { surface.ResumeLayout(true); }
                finally
                {
                    if (!surface.IsDisposed && surface.IsHandleCreated && surface.Handle == window)
                    {
                        SendMessage(window, SetRedraw, new IntPtr(1), IntPtr.Zero);
                        Repaint(surface);
                    }
                }
            }
        }
    }

    internal sealed class BufferedTaskList : ListView
    {
        private const int LeftDown = 0x0201, LeftUp = 0x0202, LeftDoubleClick = 0x0203, CaptureChanged = 0x0215;
        private ListViewItem pressedCheckbox;
        internal int CheckboxClickWidth { get; set; }

        internal BufferedTaskList()
        {
            DoubleBuffered = true;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == CaptureChanged) pressedCheckbox = null;
            if (CheckboxClickWidth > 0 && Enabled)
            {
                if (message.Msg == LeftDown || message.Msg == LeftDoubleClick)
                {
                    var item = CheckboxAt(message.LParam);
                    if (item != null)
                    {
                        Focus();
                        item.Focused = true;
                        pressedCheckbox = item;
                        Capture = true;
                        return;
                    }
                }
                else if (message.Msg == LeftUp && pressedCheckbox != null)
                {
                    var item = pressedCheckbox;
                    pressedCheckbox = null;
                    Capture = false;
                    if (item.ListView == this && CheckboxAt(message.LParam) == item)
                    {
                        if (CheckBoxes) item.Checked = !item.Checked;
                        else item.Selected = true;
                    }
                    return;
                }
            }
            base.WndProc(ref message);
        }

        private ListViewItem CheckboxAt(IntPtr position)
        {
            long coordinates = position.ToInt64();
            int x = unchecked((short)coordinates);
            int y = unchecked((short)(coordinates >> 16));
            if (x < 0 || x >= CheckboxClickWidth || y < 0 || y >= ClientSize.Height) return null;
            return GetItemAt(x, y) ?? GetItemAt(CheckboxClickWidth, y);
        }
    }
}
