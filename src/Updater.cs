using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PoeStashPricer
{
    /// <summary>A newer release on GitHub.</summary>
    public class UpdateInfo
    {
        public Version Version;
        public string Tag;        // e.g. v1.4.1
        public string ZipUrl;
        public string SumsUrl;    // SHA256SUMS.txt of the release
        public string PageUrl;    // the release page
    }

    /// <summary>
    /// Update to the latest release: asks GitHub which release is the latest, and when the user agrees downloads
    /// its zip, checks it against the release's SHA-256 list, and swaps the exe. Only this repository's releases
    /// are ever downloaded.
    /// </summary>
    public static class Updater
    {
        const string Repo = "tugayilik/poe2-stash-pricing";
        const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
        const string DownloadBase = "https://github.com/" + Repo + "/releases/download/";
        const string ExeName = "PoeStashPricer.exe";
        const long MaxDownload = 50L * 1024 * 1024;

        static string UserAgent { get { return "PoeStashPricer/" + MainForm.Version + " (update check)"; } }

        static string TempDir { get { return Path.Combine(Path.GetTempPath(), "PoeStashPricer-update"); } }

        /// <summary>The latest release if it is newer than this app, otherwise null. Throws on network errors.</summary>
        public static UpdateInfo Check()
        {
            Dictionary<string, object> rel = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(Get(LatestApi, 1024 * 1024, "application/vnd.github+json")))
                                             as Dictionary<string, object>;
            if (rel == null) return null;
            string tag = Str(rel, "tag_name");
            Version v;
            if (tag == null || !tag.StartsWith("v") || !Version.TryParse(tag.Substring(1), out v)) return null;
            if (v <= Version.Parse(MainForm.Version)) return null;

            UpdateInfo u = new UpdateInfo { Version = v, Tag = tag, PageUrl = "https://github.com/" + Repo + "/releases/tag/" + tag };
            object assets;
            if (rel.TryGetValue("assets", out assets) && assets is IEnumerable)
                foreach (object o in (IEnumerable)assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Str(a, "name"), url = Str(a, "browser_download_url");
                    // Only files of this very release in this repository.
                    if (url == null || url != DownloadBase + tag + "/" + name) continue;
                    if (name == "PoeStashPricer-" + tag + ".zip") u.ZipUrl = url;
                    else if (name == "SHA256SUMS.txt") u.SumsUrl = url;
                }
            return u;
        }

        /// <summary>
        /// Downloads the release, checks the zip and the exe in it against the release's SHA-256 list and returns the
        /// path of the new exe, ready for <see cref="Install"/>.
        /// </summary>
        public static string Download(UpdateInfo u, Action<string> progress)
        {
            if (u.ZipUrl == null || u.SumsUrl == null) throw new InvalidDataException("the release has no zip or SHA256SUMS.txt");
            if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true);
            Directory.CreateDirectory(TempDir);

            progress("Downloading " + u.Tag + "...");
            Dictionary<string, string> sums = ParseSums(Encoding.UTF8.GetString(Get(u.SumsUrl, 64 * 1024, null)));
            string zipName = "PoeStashPricer-" + u.Tag + ".zip";
            string zipSum, exeSum;
            if (!sums.TryGetValue(zipName, out zipSum) || !sums.TryGetValue(ExeName, out exeSum))
                throw new InvalidDataException("SHA256SUMS.txt doesn't list " + zipName + " and " + ExeName);

            byte[] zip = Get(u.ZipUrl, MaxDownload, null);
            progress("Checking the download...");
            if (Sha256(zip) != zipSum) throw new InvalidDataException("the downloaded zip doesn't match its SHA-256");
            string zipPath = Path.Combine(TempDir, zipName);
            File.WriteAllBytes(zipPath, zip);

            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                ZipArchiveEntry e = archive.Entries.FirstOrDefault(x => x.FullName == ExeName);
                if (e == null || e.Length > MaxDownload) throw new InvalidDataException("the zip has no " + ExeName);
                byte[] exe;
                using (Stream s = e.Open())
                using (MemoryStream ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    exe = ms.ToArray();
                }
                if (Sha256(exe) != exeSum) throw new InvalidDataException("the exe in the zip doesn't match its SHA-256");
                string exePath = Path.Combine(TempDir, ExeName);
                File.WriteAllBytes(exePath, exe);
                return exePath;
            }
        }

        /// <summary>
        /// Puts the new exe in place of this one and starts it. A running exe can't be overwritten but can be
        /// renamed, so this one becomes PoeStashPricer.exe.old (removed at the next start). The caller then exits.
        /// </summary>
        public static void Install(string newExe)
        {
            string cur = Application.ExecutablePath, old = cur + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(cur, old);
            try { File.Copy(newExe, cur); }
            catch
            {
                File.Move(old, cur);   // put this version back
                throw;
            }
            Process.Start(new ProcessStartInfo(cur, "--updated") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(cur) });
        }

        /// <summary>Removes what an update left behind (the previous exe, the download).</summary>
        public static void CleanUp()
        {
            try { string old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true); } catch { }
        }

        static Dictionary<string, string> ParseSums(string text)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string raw in text.TrimStart('﻿').Split('\n'))
            {
                string[] p = raw.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 2 && p[0].Length == 64) d[p[1].Trim().TrimStart('*')] = p[0].ToLowerInvariant();
            }
            return d;
        }

        static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        static byte[] Get(string url, long max, string accept)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            if (accept != null) req.Accept = accept;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            using (WebResponse resp = req.GetResponse())
            {
                // Redirects (GitHub serves downloads from its CDN) must stay on HTTPS.
                if (resp.ResponseUri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("download left HTTPS");
                using (Stream s = resp.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[64 * 1024];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        ms.Write(buf, 0, n);
                        if (ms.Length > max) throw new InvalidDataException("download too large");
                    }
                    return ms.ToArray();
                }
            }
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object o;
            return d.TryGetValue(k, out o) && o != null ? o.ToString() : null;
        }
    }
}
