using Hi3Helper;
using Hi3Helper.Http;
using Hi3Helper.Http.Legacy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using static Hi3Helper.Logger;

namespace ApplyUpdate
{
    public struct CDNURLProperty
    {
        public string URLPrefix { get; set; }
        public string Name { get; set; }
        public bool PartialDownloadSupport { get; set; }
    }

    public static class FallbackCDNUtil
    {
        public static List<CDNURLProperty> CDNList =
        [
            new()
            {
                Name = "Cloudflare",
                URLPrefix = "https://r2.bagelnl.my.id/cl-cdn",
                PartialDownloadSupport = true
            },


            new()
            {
                Name = "DigitalOcean",
                URLPrefix = "https://cdn.collapselauncher.com/cl-cdn",
                PartialDownloadSupport = true
            },


            new()
            {
                Name = "GitHub",
                URLPrefix = "https://github.com/CollapseLauncher/CollapseLauncher-ReleaseRepo/raw/main",
                PartialDownloadSupport = true
            },


            new()
            {
                Name = "GitLab",
                URLPrefix = "https://gitlab.com/bagusnl/CollapseLauncher-ReleaseRepo/-/raw/main/",
            },


            new()
            {
                Name = "Coding" + $" [{Locale.Lang._Misc.Tag_Deprecated}]",
                URLPrefix = "https://ohly-generic.pkg.coding.net/collapse/release/",
            },


            new()
            {
                Name = "CNB",
                URLPrefix = "https://cnb.cool/CollapseLauncher/ReleaseRepo/-/git/raw/main/",
            }
        ];

        public static event EventHandler<DownloadEvent> DownloadProgress;

        public static async Task DownloadCDNFallbackContent(Http httpInstance, string outputPath, int parallelThread, string relativeURL, CancellationToken token)
        {
            // Get the preferred CDN first and try get the content
            CDNURLProperty preferredCDN = GetPreferredCDN();
            bool isSuccess = await TryGetCDNContent(preferredCDN, httpInstance, outputPath, relativeURL, parallelThread, token);

            // If successful, then return
            if (isSuccess) return;

            // If the fail return code occurred by the token, then throw cancellation exception
            token.ThrowIfCancellationRequested();

            // If not, then continue to get the content from another CDN
            foreach (CDNURLProperty fallbackCDN in CDNList.Where(x => !x.Equals(preferredCDN)))
            {
                isSuccess = await TryGetCDNContent(fallbackCDN, httpInstance, outputPath, relativeURL, parallelThread, token);

                // If successful, then return
                if (isSuccess) return;
            }

            // If all of them failed, then throw an exception
            if (!isSuccess)
            {
                throw new AggregateException($"All available CDNs aren't reachable for your network while getting content: {relativeURL}. Please check your internet!");
            }
        }

        public static async Task DownloadCDNFallbackContent(DownloadClient downloadClient, string outputPath, int parallelThread, string relativeURL, CancellationToken token)
        {
            // Get the preferred CDN first and try get the content
            CDNURLProperty preferredCDN = GetPreferredCDN();
            bool isSuccess = await TryGetCDNContent(preferredCDN, downloadClient, outputPath, relativeURL, parallelThread, token);

            // If successful, then return
            if (isSuccess) return;

            // If the fail return code occurred by the token, then throw cancellation exception
            token.ThrowIfCancellationRequested();

            // If not, then continue to get the content from another CDN
            foreach (CDNURLProperty fallbackCDN in CDNList.Where(x => !x.Equals(preferredCDN)))
            {
                isSuccess = await TryGetCDNContent(fallbackCDN, downloadClient, outputPath, relativeURL, parallelThread, token);

                // If successful, then return
                if (isSuccess) return;
            }

            // If all of them failed, then throw an exception
            if (!isSuccess)
            {
                throw new AggregateException($"All available CDNs aren't reachable for your network while getting content: {relativeURL}. Please check your internet!");
            }
        }

        public static async Task DownloadCDNFallbackContent(Http httpInstance, Stream outputStream, string relativeURL, CancellationToken token)
        {
            // Argument check
            PerformStreamCheckAndSeek(outputStream);

            // Get the preferred CDN first and try get the content
            CDNURLProperty preferredCDN = GetPreferredCDN();
            bool isSuccess = await TryGetCDNContent(preferredCDN, httpInstance, outputStream, relativeURL, token);

            // If successful, then return
            if (isSuccess) return;

            // If the fail return code occurred by the token, then throw cancellation exception
            token.ThrowIfCancellationRequested();

            // If not, then continue to get the content from another CDN
            foreach (CDNURLProperty fallbackCDN in CDNList.Where(x => !x.Equals(preferredCDN)))
            {
                isSuccess = await TryGetCDNContent(fallbackCDN, httpInstance, outputStream, relativeURL, token);

                // If successful, then return
                if (isSuccess) return;
            }

            // If all of them failed, then throw an exception
            if (!isSuccess)
            {
                throw new AggregateException($"All available CDNs aren't reachable for your network while getting content: {relativeURL}. Please check your internet!");
            }
        }

        private static void PerformStreamCheckAndSeek(Stream outputStream)
        {
            // Throw if output stream can't write and seek
            if (!outputStream.CanWrite) throw new ArgumentException($"outputStream must be writable!", "outputStream");
            if (!outputStream.CanSeek) throw new ArgumentException($"outputStream must be seekable!", "outputStream");

            // Reset the outputStream position
            outputStream.Position = 0;
        }

        private static async Task<bool> TryGetCDNContent(CDNURLProperty cdnProp, Http httpInstance, Stream outputStream, string relativeURL, CancellationToken token)
        {
            try
            {
                // Subscribe the progress to the adapter
                httpInstance.DownloadProgress += HttpInstance_DownloadProgressAdapter!;

                // Get the URL Status then return boolean and and URLStatus
                (bool, string) urlStatus = await TryGetURLStatus(cdnProp, httpInstance, relativeURL, token);

                // If URL status is false, then return false
                if (!urlStatus.Item1) return false;

                // Continue to get the content and return true if successful
                await httpInstance.Download(urlStatus.Item2, outputStream, null, null, token);
                return true;
            }
            // Handle the error and log it. If fails, then log it and return false
            catch (Exception ex)
            {
                LogWriteLine($"Failed while getting CDN content from: {cdnProp.Name} (prefix: {cdnProp.URLPrefix}) (relPath: {relativeURL})\r\n{ex}", Hi3Helper.LogType.Error);
                return false;
            }
            // Finally, unsubscribe the progress from the adapter
            finally
            {
                httpInstance.DownloadProgress -= HttpInstance_DownloadProgressAdapter!;
            }
        }

        public static async Task DownloadCDNFallbackContent(DownloadClient downloadClient, Stream outputStream, string relativeURL, CancellationToken token)
        {
            // Argument check
            PerformStreamCheckAndSeek(outputStream);

            // Get the preferred CDN first and try get the content
            CDNURLProperty preferredCDN = GetPreferredCDN();
            bool isSuccess = await TryGetCDNContent(preferredCDN, downloadClient, outputStream, relativeURL, token);

            // If successful, then return
            if (isSuccess) return;

            // If the fail return code occurred by the token, then throw cancellation exception
            token.ThrowIfCancellationRequested();

            // If not, then continue to get the content from another CDN
            foreach (CDNURLProperty fallbackCDN in CDNList.Where(x => !x.Equals(preferredCDN)))
            {
                isSuccess = await TryGetCDNContent(fallbackCDN, downloadClient, outputStream, relativeURL, token);

                // If successful, then return
                if (isSuccess) return;
            }

            // If all of them failed, then throw an exception
            if (!isSuccess)
            {
                throw new AggregateException($"All available CDNs aren't reachable for your network while getting content: {relativeURL}. Please check your internet!");
            }
        }

        private static async ValueTask<bool> TryGetCDNContent(CDNURLProperty cdnProp, DownloadClient downloadClient, string outputPath, string relativeURL, int parallelThread, CancellationToken token)
        {
            try
            {
                // Get the URL Status then return boolean and and URLStatus
                (bool, string) urlStatus = await TryGetURLStatus(cdnProp, downloadClient, relativeURL, token);

                // If URL status is false, then return false
                if (!urlStatus.Item1) return false;

                // Continue to get the content and return true if successful
                if (!cdnProp.PartialDownloadSupport)
                {
                    // If the CDN marked to not supporting the partial download, then use single thread mode download.
                    using FileStream stream = File.Create(outputPath);
                    await downloadClient.DownloadAsync(urlStatus.Item2, stream, false, HttpInstance_DownloadProgressAdapter, null, null, cancelToken: token);
                    return true;
                }
                await downloadClient.DownloadAsync(urlStatus.Item2, outputPath, true, progressDelegateAsync: HttpInstance_DownloadProgressAdapter, maxConnectionSessions: parallelThread, cancelToken: token);
                return true;
            }
            // Handle the error and log it. If fails, then log it and return false
            catch (Exception ex)
            {
                LogWriteLine($"Failed while getting CDN content from: {cdnProp.Name} (prefix: {cdnProp.URLPrefix}) (relPath: {relativeURL})\r\n{ex}", LogType.Error, true);
                return false;
            }
        }

        private static async ValueTask<bool> TryGetCDNContent(CDNURLProperty cdnProp, DownloadClient downloadClient, Stream outputStream, string relativeURL, CancellationToken token)
        {
            try
            {
                // Get the URL Status then return boolean and and URLStatus
                (bool, string) urlStatus = await TryGetURLStatus(cdnProp, downloadClient, relativeURL, token);

                // If URL status is false, then return false
                if (!urlStatus.Item1) return false;

                // Continue to get the content and return true if successful
                await downloadClient.DownloadAsync(urlStatus.Item2, outputStream, false, HttpInstance_DownloadProgressAdapter, null, null, cancelToken: token);
                return true;
            }
            // Handle the error and log it. If fails, then log it and return false
            catch (Exception ex)
            {
                LogWriteLine($"Failed while getting CDN content from: {cdnProp.Name} (prefix: {cdnProp.URLPrefix}) (relPath: {relativeURL})\r\n{ex}", LogType.Error, true);
                return false;
            }
        }

        private static async Task<bool> TryGetCDNContent(CDNURLProperty cdnProp, Http httpInstance, string outputPath, string relativeURL, int parallelThread, CancellationToken token)
        {
            try
            {
                // Subscribe the progress to the adapter
                httpInstance.DownloadProgress += HttpInstance_DownloadProgressAdapter!;

                // Get the URL Status then return boolean and and URLStatus
                (bool, string) urlStatus = await TryGetURLStatus(cdnProp, httpInstance, relativeURL, token);

                // If URL status is false, then return false
                if (!urlStatus.Item1) return false;

                // Continue to get the content and return true if successful
                if (!cdnProp.PartialDownloadSupport)
                {
                    // If the CDN marked to not supporting the partial download, then use single thread mode download.
                    await httpInstance.Download(urlStatus.Item2, outputPath, true, null, null, token);
                    return true;
                }
                await httpInstance.Download(urlStatus.Item2, outputPath, (byte)parallelThread, true, token);
                await httpInstance.Merge(token);
                return true;
            }
            // Handle the error and log it. If fails, then log it and return false
            catch (Exception ex)
            {
                LogWriteLine($"Failed while getting CDN content from: {cdnProp.Name} (prefix: {cdnProp.URLPrefix}) (relPath: {relativeURL})\r\n{ex}", Hi3Helper.LogType.Error);
                return false;
            }
            // Finally, unsubscribe the progress from the adapter
            finally
            {
                httpInstance.DownloadProgress -= HttpInstance_DownloadProgressAdapter!;
            }
        }

        private static async Task<(bool, string)> TryGetURLStatus(CDNURLProperty cdnProp, Http httpInstance, string relativeURL, CancellationToken token)
        {
            // Concat the URL Prefix and Relative URL
            string absoluteURL = CombineURLFromString(cdnProp.URLPrefix, relativeURL);

            LogWriteLine($"Getting CDN Content from: {cdnProp.Name} at URL: {absoluteURL}");

            // Try check the status of the URL
            Tuple<int, bool> returnCode = await httpInstance.GetURLStatus(absoluteURL, token);

            // If it's not a successful code, then return false
            if (!returnCode.Item2)
            {
                LogWriteLine($"CDN content from: {cdnProp.Name} (prefix: {cdnProp.URLPrefix}) (relPath: {relativeURL}) has returned error code: {returnCode.Item1}", Hi3Helper.LogType.Warning);
                return (false, absoluteURL);
            }

            // Otherwise, return true
            return (true, absoluteURL);
        }

        private static async Task<(bool, string)> TryGetURLStatus(CDNURLProperty cdnProp, DownloadClient downloadClient, string relativeURL, CancellationToken token)
        {
            // Concat the URL Prefix and Relative URL
            string absoluteURL = CombineURLFromString(cdnProp.URLPrefix, relativeURL);

            LogWriteLine($"Getting CDN Content from: {cdnProp.Name} at URL: {absoluteURL}", LogType.Default, true);

            // Try check the status of the URL
            (HttpStatusCode, bool) returnCode = await downloadClient.GetURLStatus(absoluteURL, token);

            // If it's not a successful code, then return false
            if (!returnCode.Item2)
            {
                LogWriteLine($"CDN content from: {cdnProp.Name} (prefix: {cdnProp.URLPrefix}) (relPath: {relativeURL}) has returned error code: {returnCode.Item1} ({(int)returnCode.Item1})", LogType.Error, true);
                return (false, absoluteURL);
            }

            // Otherwise, return true
            return (true, absoluteURL);
        }

        public static string CombineURLFromString(string baseURL, params string[] segments)
        {
            StringBuilder builder = new StringBuilder().Append(baseURL.TrimEnd('/'));

            foreach (string a in segments)
            {
                if (a.Length == 0) continue;

                bool isMacros = a.StartsWith("?");
                if (!isMacros)
                {
                    builder.Append('/');
                }
                builder.Append(a.Trim('/'));
            }

            return builder.ToString();
        }

        public static int PreferredCDNIndex = 0;
        public static CDNURLProperty GetPreferredCDN() => CDNList[PreferredCDNIndex];

        // Re-send the events to the static DownloadProgress
        private static void HttpInstance_DownloadProgressAdapter(object sender, DownloadEvent e) => DownloadProgress?.Invoke(sender, e);

        private static DownloadEvent DownloadClientAdapter = new DownloadEvent();

        private static void HttpInstance_DownloadProgressAdapter(int read, DownloadProgress downloadProgress)
        {
            DownloadClientAdapter.SizeToBeDownloaded = downloadProgress.BytesTotal;
            DownloadClientAdapter.SizeDownloaded = downloadProgress.BytesDownloaded;
            DownloadClientAdapter.Read = read;
            DownloadProgress?.Invoke(null, DownloadClientAdapter);
        }
    }
}
