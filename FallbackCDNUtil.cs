using ApplyUpdate;
using Hi3Helper.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CollapseLauncher
{
    public struct CDNURLProperty
    {
        public string URLPrefix { get; set; }
        public string Name { get; set; }
        public bool PartialDownloadSupport { get; set; }
    }
    internal static class FallbackCDNUtil
    {
        public static List<CDNURLProperty> CDNList => new List<CDNURLProperty>
        {
            new CDNURLProperty
            {
                Name = "GitHub",
                URLPrefix = "https://github.com/neon-nyan/CollapseLauncher-ReleaseRepo/raw/main",
                PartialDownloadSupport = true
            },
            new CDNURLProperty
            {
                Name = "Cloudflare",
                URLPrefix = "https://r2-render.bagelnl.my.id/cl-cdn",
                PartialDownloadSupport = true
            },
            new CDNURLProperty
            {
                Name = "Bitbucket",
                URLPrefix = "https://bitbucket.org/neon-nyan/collapselauncher-releaserepo/raw/main",
            },
            new CDNURLProperty
            {
                Name = "Statically",
                URLPrefix = "https://cdn.statically.io/gh/neon-nyan/CollapseLauncher-ReleaseRepo/main",
                PartialDownloadSupport = true
            },
            new CDNURLProperty
            {
                Name = "jsDelivr",
                URLPrefix = "https://cdn.jsdelivr.net/gh/neon-nyan/CollapseLauncher-ReleaseRepo@latest",
                PartialDownloadSupport = true
            }
        };

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

        private static async ValueTask<bool> TryGetCDNContent(CDNURLProperty cdnProp, Http httpInstance, Stream outputStream, string relativeURL, CancellationToken token)
        {
            try
            {
                // Subscribe the progress to the adapter
                httpInstance.DownloadProgress += HttpInstance_DownloadProgressAdapter;

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
                Console.WriteLine($"Failed while getting CDN content from: {cdnProp.Name} (prefix: {cdnProp.URLPrefix}) (relPath: {relativeURL})\r\n{ex}");
                return false;
            }
            // Finally, unsubscribe the progress from the adapter
            finally
            {
                httpInstance.DownloadProgress -= HttpInstance_DownloadProgressAdapter;
            }
        }

        private static async ValueTask<bool> TryGetCDNContent(CDNURLProperty cdnProp, Http httpInstance, string outputPath, string relativeURL, int parallelThread, CancellationToken token)
        {
            try
            {
                // Subscribe the progress to the adapter
                httpInstance.DownloadProgress += HttpInstance_DownloadProgressAdapter;

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
                await httpInstance.Merge();
                return true;
            }
            // Handle the error and log it. If fails, then log it and return false
            catch (Exception ex)
            {
                Console.WriteLine($"Failed while getting CDN content from: {cdnProp.Name} (prefix: {cdnProp.URLPrefix}) (relPath: {relativeURL})\r\n{ex}");
                return false;
            }
            // Finally, unsubscribe the progress from the adapter
            finally
            {
                httpInstance.DownloadProgress -= HttpInstance_DownloadProgressAdapter;
            }
        }

        private static async Task<(bool, string)> TryGetURLStatus(CDNURLProperty cdnProp, Http httpInstance, string relativeURL, CancellationToken token)
        {
            // Concat the URL Prefix and Relative URL
            string absoluteURL = CombineURLFromString(cdnProp.URLPrefix, relativeURL);

            Console.WriteLine($"Getting CDN Content from: {cdnProp.Name} at URL: {absoluteURL}");

            // Try check the status of the URL
            (int, bool) returnCode = await httpInstance.GetURLStatus(absoluteURL, token);

            // If it's not a successful code, then return false
            if (!returnCode.Item2)
            {
                Console.WriteLine($"CDN content from: {cdnProp.Name} (prefix: {cdnProp.URLPrefix}) (relPath: {relativeURL}) has returned error code: {returnCode.Item1}");
                return (false, absoluteURL);
            }

            // Otherwise, return true
            return (true, absoluteURL);
        }

        public static string CombineURLFromString(ReadOnlySpan<char> baseURL, params string[] segments)
        {
            StringBuilder builder = new StringBuilder().Append(baseURL.TrimEnd('/'));

            foreach (ReadOnlySpan<char> a in segments)
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

        public static CDNURLProperty GetPreferredCDN() => CDNList[Program.PreferredCDNIndex];

        // Re-send the events to the static DownloadProgress
        private static void HttpInstance_DownloadProgressAdapter(object sender, DownloadEvent e) => DownloadProgress?.Invoke(sender, e);
    }
}
