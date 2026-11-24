using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private static void OpenDoubao()
        {
            try
            {
                foreach (var location in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
                {
                    string root = Environment.GetFolderPath(location);
                    if (String.IsNullOrEmpty(root)) continue;
                    string shortcut = new[] { Path.Combine(root, "豆包", "豆包.lnk"), Path.Combine(root, "豆包.lnk") }
                        .FirstOrDefault(File.Exists);
                    if (shortcut == null) continue;
                    using (Process.Start(new ProcessStartInfo(shortcut) { UseShellExecute = true })) { }
                    return;
                }
                throw new InvalidOperationException("找不到豆包桌面版的开始菜单快捷方式。请安装豆包桌面版后重试。");
            }
            catch (Win32Exception ex) { throw new InvalidOperationException("无法打开豆包桌面版，请检查安装。", ex); }
        }
    }
}
