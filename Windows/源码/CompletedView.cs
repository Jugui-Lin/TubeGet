using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private readonly Dictionary<string, Image> completedCovers = new Dictionary<string, Image>();
        private readonly Queue<string> completedCoverOrder = new Queue<string>();
        private readonly HashSet<string> loadingCompletedCovers = new HashSet<string>();
        private ImageList completedCardSpacing;
        private MaterialButton completedViewButton;

        private bool CompletedCardsActive
        {
            get { return completedFolder == null ? options.CompletedCards : options.CompletedFolderCards; }
        }

        private void ToggleCompletedView()
        {
            if (completedFolder == null) options.CompletedCards = !options.CompletedCards;
            else options.CompletedFolderCards = !options.CompletedFolderCards;
            PagePainting.Update(taskListHost, ApplyTaskView);
            UpdateCompletedViewButton();
            SaveOptions();
        }

        private void UpdateCompletedViewButton()
        {
            completedViewButton.Glyph = CompletedCardsActive ? "queue" : "grid";
            string action = CompletedCardsActive ? "切换到列表视图" : "切换到封面卡片";
            completedViewButton.AccessibleName = action;
            tips.SetToolTip(completedViewButton, action);
            completedViewButton.Invalidate();
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendListMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        private void ConfigureCompletedCards()
        {
            completedCardSpacing = CreateListSpacing(Math.Min(256, Ui(180)), Math.Min(256, Ui(130)));
            list.LargeImageList = completedCardSpacing;
        }

        private void ApplyTaskView()
        {
            bool cards = page == 1 && CompletedCardsActive;
            var target = cards ? View.LargeIcon : View.Details;
            if (list.View != target)
            {
                list.View = target;
                if (cards)
                {
                    const int setIconSpacing = 0x1035;
                    SendListMessage(list.Handle, setIconSpacing, IntPtr.Zero, new IntPtr((Ui(161) << 16) | Ui(195)));
                }
            }
            list.HeaderStyle = page == DownloadPage || cards ? ColumnHeaderStyle.None : ColumnHeaderStyle.Clickable;
            ResizeColumns();
            list.Invalidate();
        }

        private void PaintCompletedCard(object sender, DrawListViewItemEventArgs e)
        {
            if (list.View != View.LargeIcon) return;
            var folder = e.Item.Tag as CompletedFolder;
            var file = e.Item.Tag as CompletedFile;
            var job = file == null ? e.Item.Tag as MediaTask : file.Job;
            if (job == null && folder == null) return;
            // LargeIcon item bounds include the native label, whose height varies with wrapping.
            var bounds = new Rectangle(e.Bounds.Left + Ui(3), e.Bounds.Top + Ui(3), Ui(189), Ui(147));
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool selected = e.Item.Selected;
            Material.Fill(g, bounds, 12, selected ? Material.Accent : Material.Container,
                selected ? Material.Primary : Material.Line);
            int inset = Ui(5);
            int coverWidth = bounds.Width - 2 * inset;
            var cover = new Rectangle(bounds.Left + inset, bounds.Top + inset, coverWidth, (coverWidth * 9 + 8) / 16);
            Material.Fill(g, cover, 7, Material.Card);
            Image image = null;
            if (folder != null)
            {
                string url = folder.Jobs.Select(member => member.CollectionCoverUrl)
                    .FirstOrDefault(address => !String.IsNullOrWhiteSpace(address));
                string key = "folder|" + folder.Path + "|" + folder.Jobs[0].Id + "|" + folder.Count + "|" + url;
                image = GetCompletedCover(key, () => ReadCompletedFolderCover(url, folder.Jobs));
            }
            else
            {
                string path = file == null ? job.File : file.Path;
                image = GetCompletedCover(job.Id + "|" + path, () => ReadCompletedCover(path, job.Kind));
            }
            if (image != null)
            {
                float scale = Math.Min(cover.Width / (float)image.Width, cover.Height / (float)image.Height);
                float width = image.Width * scale, height = image.Height * scale;
                var state = g.Save();
                using (var clip = Material.Shape(cover, 7)) g.SetClip(clip, CombineMode.Intersect);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, new RectangleF(cover.X + (cover.Width - width) / 2, cover.Y + (cover.Height - height) / 2, width, height));
                g.Restore(state);
            }
            else Material.Icon(g, job != null && job.Kind == "视频" ? "play" : "folder",
                new Rectangle(cover.X + (cover.Width - 28) / 2, cover.Y + (cover.Height - 28) / 2, 28, 28), Material.Muted);

            var text = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, e.Item.Text, Font,
                new Rectangle(cover.X, cover.Bottom + Ui(3), cover.Width, Ui(19)), Material.Ink, text);
            TextRenderer.DrawText(g, folder == null ? job.Kind + "  ·  " + job.Added.ToString("MM-dd HH:mm") : "合集  ·  " + folder.Count + " 项", Font,
                new Rectangle(cover.X, cover.Bottom + Ui(22), cover.Width, Ui(16)), Material.Muted, text);
        }

        private Image GetCompletedCover(string key, Func<Task<Image>> read)
        {
            Image image;
            if (!completedCovers.TryGetValue(key, out image) &&
                loadingCompletedCovers.Count < 2 && loadingCompletedCovers.Add(key))
                LoadCompletedCover(key, read);
            return image;
        }

        private async void LoadCompletedCover(string key, Func<Task<Image>> read)
        {
            Image image = await Task.Run(read);
            loadingCompletedCovers.Remove(key);
            if (IsDisposed || Disposing)
            {
                if (image != null) image.Dispose();
                return;
            }
            completedCovers.Add(key, image);
            completedCoverOrder.Enqueue(key);
            if (completedCoverOrder.Count > 128)
            {
                string oldest = completedCoverOrder.Dequeue();
                Image old = completedCovers[oldest];
                if (old != null) old.Dispose();
                completedCovers.Remove(oldest);
            }
            if (page == 1 && list.View == View.LargeIcon) list.Invalidate();
        }

        internal static async Task<Image> ReadCompletedFolderCover(string url, MediaTask[] jobs)
        {
            if (!String.IsNullOrWhiteSpace(url))
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    Image image = await ReadPreview(url, timeout.Token, new Size(320, 180)).ConfigureAwait(false);
                    if (image != null) return image;
                }
            }

            var files = jobs.SelectMany(job => PlaylistFiles(job).Select(path => new { Path = path, job.Kind })).ToArray();
            foreach (var file in files.Where(file => File.Exists(file.Path)).Take(6))
            {
                Image image = await ReadCompletedCover(file.Path, null).ConfigureAwait(false);
                if (image != null) return image;
            }
            foreach (var file in files.Where(file => file.Kind == "视频" && File.Exists(file.Path)).Take(3))
            {
                Image image = await ReadCompletedCover(file.Path, file.Kind).ConfigureAwait(false);
                if (image != null) return image;
            }
            return null;
        }

        internal static async Task<Image> ReadCompletedCover(string path, string kind)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string temporary = Path.Combine(Path.GetTempPath(), "tubeget-cover-" + Guid.NewGuid().ToString("N") + ".jpg");
            try
            {
                string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".avif" };
                bool isImage = imageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
                string source = isImage && File.Exists(path) ? path :
                    imageExtensions.Select(extension => Path.ChangeExtension(path, extension)).FirstOrDefault(File.Exists);
                string filter = "scale=320:180:force_original_aspect_ratio=decrease";
                if (source == null)
                {
                    if (kind != "视频" || !File.Exists(path)) return null;
                    source = path;
                    filter += ",thumbnail=16";
                }
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    await Engine.Capture(Engine.FFmpeg(), new[] { "-nostdin", "-v", "error", "-y", "-i", source,
                        "-vf", filter, "-frames:v", "1", temporary }, timeout.Token).ConfigureAwait(false);
                if (!File.Exists(temporary)) return null;
                using (var opened = Image.FromFile(temporary)) return new Bitmap(opened);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (OperationCanceledException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (ArgumentException) { return null; }
            finally { Engine.TryDelete(temporary); }
        }

        private void DisposeCompletedCovers()
        {
            foreach (var image in completedCovers.Values) if (image != null) image.Dispose();
            completedCovers.Clear();
            if (completedCardSpacing != null) completedCardSpacing.Dispose();
        }
    }
}
