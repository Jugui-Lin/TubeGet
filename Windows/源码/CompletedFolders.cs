using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private string completedFolder;
        private string completedRootFilter = "";

        private sealed class CompletedFolder
        {
            internal string Path;
            internal MediaTask[] Jobs;
            internal string Name { get { return System.IO.Path.GetFileName(Path); } }
            internal int Count { get { return Jobs.Sum(job => job.Playlist ? Math.Max(1, PlaylistFiles(job).Count()) : 1); } }
        }

        private sealed class CompletedFile
        {
            internal MediaTask Job;
            internal string Path;
        }

        private static IEnumerable<string> PlaylistFiles(MediaTask job)
        {
            return (job.Files ?? new[] { job.File }).Where(path => !String.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string FullFolder(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path)) return null;
            try
            {
                string fullPath = Path.GetFullPath(path);
                return fullPath.Length == Path.GetPathRoot(fullPath).Length ? fullPath :
                    fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        private static string LegacyBilibiliPart(MediaTask job, out int part)
        {
            part = 0;
            Uri uri;
            if (!Uri.TryCreate(job.Source, UriKind.Absolute, out uri) || CookieSessions.Site(uri) != "bilibili.com") return null;
            var video = Regex.Match(uri.AbsolutePath, @"^/video/((?:BV[A-Za-z0-9]+|av[0-9]+))/?$", RegexOptions.IgnoreCase);
            return video.Success && Int32.TryParse(HttpUtility.ParseQueryString(uri.Query)["p"], out part) && part > 0
                ? video.Groups[1].Value : null;
        }

        private Dictionary<MediaTask, string> CompletedFolderPaths(MediaTask[] completed)
        {
            var paths = new Dictionary<MediaTask, string>();
            foreach (var job in completed)
            {
                if (String.IsNullOrWhiteSpace(job.PlaylistDirectoryName)) continue;
                string folder = FullFolder(job.Folder);
                if (folder == null) continue;
                if (job.Playlist)
                {
                    string name = Engine.SafeDirectoryName(job.PlaylistDirectoryName);
                    if (name.Length == 0) continue;
                    folder = Path.Combine(folder, name);
                }
                paths[job] = folder;
            }

            // Older Bilibili part downloads used the same output directory without recording the collection name.
            string defaultFolder = FullFolder(new Options().Downloads), configuredFolder = FullFolder(options.Downloads);
            var legacy = completed.Where(job => !job.Playlist && !paths.ContainsKey(job))
                .Select(job =>
                {
                    int part;
                    string video = LegacyBilibiliPart(job, out part);
                    return new { Job = job, Folder = FullFolder(job.Folder), Video = video, Part = part };
                })
                .Where(item => item.Video != null && item.Folder != null &&
                    !String.Equals(item.Folder, defaultFolder, StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(item.Folder, configuredFolder, StringComparison.OrdinalIgnoreCase));
            foreach (var group in legacy.GroupBy(item => item.Folder, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var video in group.GroupBy(item => item.Video, StringComparer.Ordinal))
                {
                    bool multiple = video.Select(item => item.Part).Distinct().Take(2).Count() == 2;
                    foreach (var item in video)
                    {
                        string title = item.Job.Title ?? "";
                        var prefix = Regex.Match(title, @"^(.*?)\s+p\d+\b", RegexOptions.IgnoreCase);
                        if (multiple || (prefix.Success && String.Equals(Engine.SafeDirectoryName(prefix.Groups[1].Value),
                            Path.GetFileName(group.Key), StringComparison.OrdinalIgnoreCase)))
                            paths[item.Job] = group.Key;
                    }
                }
            }
            return paths;
        }

        private object[] CompletedRows(out MediaTask[] visible)
        {
            var completed = jobs.Where(job => !job.Local && job.State == "已完成").ToArray();
            var paths = CompletedFolderPaths(completed);
            var groups = completed.Where(paths.ContainsKey).GroupBy(job => paths[job], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => new CompletedFolder { Path = group.Key, Jobs = group.ToArray() },
                    StringComparer.OrdinalIgnoreCase);

            var rows = new List<object>();
            var matchingJobs = new List<MediaTask>();
            if (completedFolder != null)
            {
                CompletedFolder folder;
                if (groups.TryGetValue(completedFolder, out folder))
                {
                    foreach (var job in folder.Jobs)
                    {
                        if (!job.Playlist)
                        {
                            if (!MatchesTaskFilter(job.Title ?? job.Source)) continue;
                            rows.Add(job);
                        }
                        else
                        {
                            var files = PlaylistFiles(job).Where(path => MatchesTaskFilter(Path.GetFileName(path)))
                                .Select(path => new CompletedFile { Job = job, Path = path }).ToArray();
                            if (files.Length == 0) continue;
                            rows.AddRange(files);
                        }
                        matchingJobs.Add(job);
                    }
                }
            }
            else
            {
                var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var job in completed)
                {
                    string path;
                    if (!paths.TryGetValue(job, out path))
                    {
                        if (!MatchesTaskFilter(job.Title ?? job.Source)) continue;
                        rows.Add(job);
                        matchingJobs.Add(job);
                        continue;
                    }
                    if (!added.Add(path)) continue;
                    var folder = groups[path];
                    if (!MatchesTaskFilter(folder.Name) && !folder.Jobs.Any(member => MatchesTaskFilter(member.Title ?? member.Source)))
                        continue;
                    rows.Add(folder);
                    matchingJobs.AddRange(folder.Jobs);
                }
            }

            visible = matchingJobs.ToArray();
            return rows.ToArray();
        }

        private bool MatchesTaskFilter(string text)
        {
            return filter.Length == 0 || (text ?? "").IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static object TaskRowKey(object row)
        {
            var folder = row as CompletedFolder;
            if (folder != null) return "folder|" + folder.Path.ToUpperInvariant();
            var file = row as CompletedFile;
            return file == null ? row : "file|" + file.Job.Id + "|" + file.Path.ToUpperInvariant();
        }

        private void NavigateCompletedFolder(string path)
        {
            if (path != null) completedRootFilter = filter;
            completedFolder = path;
            filter = path == null ? completedRootFilter : "";
            PagePainting.Update(taskCard, () =>
            {
                heading.Text = path == null ? PageNames[1] : Path.GetFileName(path);
                ConfigureActions();
                AttachTaskList();
                RefreshTasks();
            });
        }

        private void OpenTaskRow(object row)
        {
            var folder = row as CompletedFolder;
            if (folder != null) { NavigateCompletedFolder(folder.Path); return; }
            var file = row as CompletedFile;
            if (file != null)
            {
                if (File.Exists(file.Path)) ShellOpen(file.Path);
                else MessageBox.Show(this, "找不到输出文件。", Text);
                return;
            }
            var job = row as MediaTask;
            if (job != null) OpenTask(job);
        }
    }
}
