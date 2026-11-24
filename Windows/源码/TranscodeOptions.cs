using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TubeGet
{
    public sealed class TranscodePlan
    {
        public string Codec { get; set; }
        public int Height { get; set; }
        public double BitrateMbps { get; set; }
        public string Device { get; set; }
        public bool AddDetailsToName { get; set; }

        internal string CodecFor(string container)
        {
            return String.IsNullOrEmpty(Codec) || Codec == "自动" ? (container == "webm" ? "VP9" : "H.264") : Codec;
        }

        internal void AddEncoder(List<string> args, string container)
        {
            string codec = CodecFor(container);
            if (container == "webm" && codec != "VP9" && codec != "AV1")
                throw new InvalidOperationException("WebM 输出需要 VP9 或 AV1。请调整转码参数。");
            if (container == "avi" && codec != "H.264")
                throw new InvalidOperationException("AVI 输出请选择 H.264。");

            string software = codec == "AV1" ? "libaom-av1" : codec == "VP9" ? "libvpx-vp9" :
                codec == "H.265" ? "libx265" : "libx264";
            string stem = codec == "AV1" ? "av1" : codec == "H.265" ? "hevc" : "h264";
            string encoder = software;
            bool hardware = !String.IsNullOrEmpty(Device) && Device != "CPU";
            if (hardware)
            {
                if (codec == "VP9") throw new InvalidOperationException("VP9 请使用 CPU 转码。");
                string suffix = Device == "NVIDIA" ? "nvenc" : Device == "Intel" ? "qsv" : "amf";
                encoder = stem + "_" + suffix;
            }
            args.AddRange(new[] { "-c:v", encoder, "-pix_fmt", encoder.EndsWith("_qsv") ? "nv12" : "yuv420p" });
            if (!hardware)
            {
                if (codec == "H.264" || codec == "H.265") args.AddRange(new[] { "-preset", "fast" });
                if (codec == "AV1") args.AddRange(new[] { "-cpu-used", "6" });
                if (BitrateMbps <= 0)
                {
                    args.AddRange(new[] { "-crf", codec == "VP9" ? "31" : codec == "AV1" ? "30" : "20" });
                    if (codec == "VP9") args.AddRange(new[] { "-b:v", "0" });
                }
            }
            if (BitrateMbps > 0) args.AddRange(new[] {
                "-b:v", Math.Round(BitrateMbps * 1000).ToString(CultureInfo.InvariantCulture) + "k"
            });
            args.AddRange(new[] { "-c:a", container == "webm" ? "libopus" : "aac" });
        }
    }

    internal sealed partial class MainForm
    {
        private void EditTranscode()
        {
            var current = options.Transcode ?? new TranscodePlan();
            using (var dialog = Dialog("转码参数", 660, 448, new FirstPaintWindow()))
            {
                var group = new PreferenceCard("视频输出");
                var codec = group.Select("视频编码", new[] { "自动", "H.264", "H.265", "VP9", "AV1" }, current.Codec);
                var height = group.Select("目标高度上限", new[] { "原始", "2160", "1440", "1080", "720", "480", "360" },
                    current.Height == 0 ? "原始" : current.Height.ToString(CultureInfo.InvariantCulture));
                var bitrate = group.Text("视频码率（Mbps，留空自动）", current.BitrateMbps > 0 ? current.BitrateMbps.ToString(CultureInfo.CurrentCulture) : "");
                var device = group.Select("转码设备", new[] { "CPU", "NVIDIA", "Intel", "AMD" }, current.Device);
                var folder = FolderField(group, "转换输出目录", options.Converted, dialog);
                var name = group.Check("文件名附加视频编码和目标高度", current.AddDetailsToName);
                var footer = Footer();
                footer.Controls.Add(CloseButton(dialog));
                footer.Controls.Add(ButtonOf("保存参数", 102, () =>
                {
                    try
                    {
                        string output = folder.Text.Trim();
                        if (!Path.IsPathRooted(output)) throw new ArgumentException("请输入完整的输出目录。");
                        output = Path.GetFullPath(output);
                        double rate = 0;
                        if (bitrate.Text.Trim().Length > 0 && (!Double.TryParse(bitrate.Text, NumberStyles.Float,
                            CultureInfo.CurrentCulture, out rate) || !(rate >= 0.05 && rate <= 500)))
                            throw new ArgumentException("视频码率应在 0.05 至 500 Mbps 之间。");
                        int maxHeight;
                        Int32.TryParse(height.Text, out maxHeight);
                        var plan = new TranscodePlan {
                            Codec = codec.Text, Height = maxHeight, BitrateMbps = rate, Device = device.Text,
                            AddDetailsToName = name.Checked
                        };
                        options.Transcode = plan;
                        options.Converted = output;
                        foreach (var task in EditableConversions())
                        {
                            task.TranscodeOptions = plan;
                            task.Folder = output;
                        }
                        SaveOptions();
                        Changed();
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "转码参数"); }
                }, null, null, ButtonStyle.Filled));
                dialog.Controls.Add(PreferencePage(group));
                dialog.Controls.Add(footer);
                ShowAppDialog(dialog, this);
            }
        }
    }
}
