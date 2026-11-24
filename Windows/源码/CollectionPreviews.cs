using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private CancellationTokenSource collectionPreviewSession;
        private readonly Dictionary<string, Image> collectionPreviews = new Dictionary<string, Image>();
        private readonly Queue<string> collectionPreviewOrder = new Queue<string>();
        private readonly HashSet<string> pendingCollectionPreviews = new HashSet<string>();

        private void ClearCollectionPreviews()
        {
            var previous = collectionPreviewSession;
            collectionPreviewSession = null;
            if (previous != null)
            {
                previous.Cancel();
                previous.Dispose();
            }
            foreach (Image image in collectionPreviews.Values)
                if (image != null) image.Dispose();
            collectionPreviews.Clear();
            collectionPreviewOrder.Clear();
            pendingCollectionPreviews.Clear();
        }

        private void PaintCollectionPreview(Graphics graphics, Rectangle row, Entry entry)
        {
            var bounds = new Rectangle(row.Left + Ui(25), row.Top + (row.Height - Ui(38)) / 2, Ui(68), Ui(38));
            Material.Fill(graphics, bounds, Ui(4), Material.Container);
            string address = entry == null ? null : entry.Thumbnail;
            if (!string.IsNullOrEmpty(address))
            {
                Image image;
                if (collectionPreviews.TryGetValue(address, out image))
                {
                    if (image != null)
                    {
                        graphics.DrawImageUnscaled(image, bounds.Left + (bounds.Width - image.Width) / 2,
                            bounds.Top + (bounds.Height - image.Height) / 2);
                        return;
                    }
                }
                else if (collectionPreviewSession != null && pendingCollectionPreviews.Count < 3 &&
                    pendingCollectionPreviews.Add(address))
                    LoadCollectionPreview(address, bounds.Size, collectionPreviewSession);
            }
            int size = Ui(16);
            Material.Icon(graphics, "play", new Rectangle(bounds.Left + (bounds.Width - size) / 2,
                bounds.Top + (bounds.Height - size) / 2, size, size), Material.Muted);
        }

        private async void LoadCollectionPreview(string address, Size size, CancellationTokenSource session)
        {
            Image image = await ReadPreview(address, session.Token, size);
            if (session != collectionPreviewSession || IsDisposed || Disposing)
            {
                if (image != null) image.Dispose();
                return;
            }
            pendingCollectionPreviews.Remove(address);
            collectionPreviews.Add(address, image);
            collectionPreviewOrder.Enqueue(address);
            if (collectionPreviewOrder.Count > 96)
            {
                string oldest = collectionPreviewOrder.Dequeue();
                Image expired = collectionPreviews[oldest];
                if (expired != null) expired.Dispose();
                collectionPreviews.Remove(oldest);
            }
            if (resultMode == CollectionLinks) choices.Invalidate();
        }
    }
}
