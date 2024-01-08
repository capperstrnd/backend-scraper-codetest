using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using HtmlAgilityPack;

namespace BackendScraper
{

    class Program
    {
        private static string rootUrl = "https://books.toscrape.com/";
        private static string outputDirectory = "./DownloadOutput/";
        private static int maxRetries = 5; // Max Retries for HttpClient
        private static int maxParallelActivities = 8;
        private static SemaphoreSlim semaphore = new SemaphoreSlim(12); // Limit concurrent requests
        private static int pageUrlsCollectorsRunning = 0;
        private static int pageUrlsCollectorsDone = 0;
        private static int completedDownloads = 0;
        static ActionBlock<string>? getPageUrlsBlock;
        static HashSet<string> uniquePageUrls = new HashSet<string>();

        static async Task Main()
        {
            // Ensure output exists
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            // Configure thread pool
            var bufferBlockOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxParallelActivities
            };

            Console.WriteLine("Pre-fetching all page urls to download... Be patient, can take up to 5-10 min.");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            getPageUrlsBlock = new ActionBlock<string>(
                async url => await GetPageUrls(url),
                bufferBlockOptions
            );

            getPageUrlsBlock.Post(rootUrl);

            int progressTicker = 0;
            int sanityChecks = 0;

            while (true)
            {
                progressTicker++;
                int progressSpinner = progressTicker % 51;

                Console.Write($"\rProgress: [{new string('#', progressSpinner)}{new string('_', 50 - progressSpinner)}] {uniquePageUrls.Count} urls collected");

                if (pageUrlsCollectorsRunning == pageUrlsCollectorsDone)
                    sanityChecks++;
                else
                    sanityChecks = 0;

                // checked 3 times, exit progress indicator neatly with 100%
                if (sanityChecks >= 3 && progressSpinner == 50) 
                    break;

                // DEBUG: For faster testing
                // if (uniquePageUrls.Count > 100)
                //     break;

                await Task.Delay(100);
            }

            getPageUrlsBlock.Complete();
            await getPageUrlsBlock.Completion;

            stopwatch.Stop();
            Console.WriteLine($"\nPage URL gathering took {stopwatch.ElapsedMilliseconds / 1000} seconds total");
            
            Console.WriteLine($"\nProceeding to download pages...");
            stopwatch.Restart();

            // Use a BufferBlock to store URLs and propagate them to parallel downloads
            var downloadBlock = new ActionBlock<string>(
                async url => await DownloadPage(url, rootUrl, outputDirectory),
                bufferBlockOptions
            );

            foreach (var pageUrl in uniquePageUrls)
            {
                downloadBlock.Post(pageUrl);
            }

            // Progress indicator
            int totalDownloads = uniquePageUrls.Count;
            while (completedDownloads < totalDownloads)
            {
                int progressCompletedCapped = completedDownloads * 50 / totalDownloads;

                Console.Write($"\rProgress: [{new string('#', progressCompletedCapped)}{new string('_', 50 - progressCompletedCapped)}] {completedDownloads * 100 / totalDownloads}%");
                await Task.Delay(100);
            }

            // Signal the block and wait for all the downloads
            downloadBlock.Complete();
            await downloadBlock.Completion;

            // Terminate to 100%
            Console.Write($"\rProgress: [{new string('#', 50)}] {100}%");
            
            stopwatch.Stop();
            Console.WriteLine($"\nDownload completed, took {stopwatch.ElapsedMilliseconds / 1000} seconds total");
        }

        static async Task GetPageUrls(string currentUrl)
        {
            int retryCount = 0;
            Interlocked.Increment(ref pageUrlsCollectorsRunning);

            try
            {
                List<string> newUrls = new List<string>();
                var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(20) };
                string html = "";

                while (retryCount < maxRetries)
                {
                    try
                    {
                        await semaphore.WaitAsync();

                        html = await client.GetStringAsync(currentUrl);

                        break;
                    }
                    catch (Exception ex)
                    {
                        if (retryCount == (maxRetries - 1))
                            Console.WriteLine($"Couldn't fetch {currentUrl}");

                        // If timeout occurred, keep trying
                        if (ex is TaskCanceledException && ex.Message.Contains("HttpClient.Timeout of 20 seconds"))
                            retryCount++;
                        else
                            throw; // Escape to outer catch
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Extract the urls
                var nodes = doc.DocumentNode.SelectNodes("//a[@href]");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var href = node.GetAttributeValue("href", "");

                        string absoluteUrl = GetAbsoluteUrl(currentUrl, href);

                        // Ensure it's unique
                        if (!uniquePageUrls.Contains(absoluteUrl))
                        {
                            newUrls.Add(absoluteUrl);
                        }
                    }
                }

                // Continue fetching recursively
                foreach (var newUrl in newUrls)
                {
                    uniquePageUrls.Add(newUrl);
                    getPageUrlsBlock?.Post(newUrl);
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error on url {currentUrl}: {ex.Message}");
            }

            Interlocked.Increment(ref pageUrlsCollectorsDone);
        }

        // Resolves relative paths if present - i.e. "../../" within the href
        static string GetAbsoluteUrl(string baseUrl, string relativeUrl)
        {
            Uri baseUri = new Uri(baseUrl);
            Uri relativeUri = new Uri(relativeUrl, UriKind.RelativeOrAbsolute);

            if (relativeUri.IsAbsoluteUri)
            {
                return relativeUri.AbsoluteUri;
            }
            else
            {
                Uri absoluteUri = new Uri(baseUri, relativeUri);

                return absoluteUri.AbsoluteUri;
            }
        }

        static async Task DownloadPage(string url, string rootUrl, string outputDirectory)
        {
            try
            {
                string rootPath = outputDirectory;
                string relativePath = GetRelativePath(url, rootUrl);

                string urlFilename = Path.GetFileName(url);
                string urlFolderpath = Path.Combine(rootPath, relativePath);
                string targetFilepath = Path.Combine(rootPath, relativePath, urlFilename);

                if (File.Exists(targetFilepath))
                {
                    Interlocked.Increment(ref completedDownloads);
                    return; // Skip if file already exists
                }

                await semaphore.WaitAsync();
                var client = new HttpClient();
                var html = await client.GetStringAsync(url);
                semaphore.Release();

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Ensure directory exists
                Directory.CreateDirectory(urlFolderpath);
                // Save html file
                await File.WriteAllTextAsync(targetFilepath, html);

                // Download remaining assets (e.g. styling, images, scripts)
                var resourceUrls = GetResourceUrls(doc, url);
                foreach (var resourceUrl in resourceUrls)
                {
                    await DownloadResource(resourceUrl, rootPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {url}: {ex.Message}");
            }

            // Increment the completed downloads count
            Interlocked.Increment(ref completedDownloads);
        }

        private static async Task DownloadResource(string url, string rootPath)
        {
            try
            {
                int retryCount = 0;
                byte[]? content = null;
                
                string resourceFilename = Path.GetFileName(url);
                var relativePath = GetRelativePath(url, rootUrl);

                var targetFilepath = Path.Combine(rootPath, relativePath, resourceFilename);

                if (File.Exists(targetFilepath))
                    return; // Skip if file already exists

                while (retryCount < maxRetries)
                {
                    try
                    {
                        await semaphore.WaitAsync();
                        var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(20) };
                        content = await client.GetByteArrayAsync(url);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (retryCount == (maxRetries - 1))
                            Console.WriteLine($"Couldn't fetch {url}");

                        // If timeout occurred, keep trying
                        if (ex is TaskCanceledException && ex.Message.Contains("HttpClient.Timeout of 20 seconds"))
                            retryCount++;
                        else
                            throw; // Escape to outer catch
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }

                // Ensure directory exists
                Directory.CreateDirectory(Path.Combine(rootPath, relativePath));
                await File.WriteAllBytesAsync(targetFilepath, content!);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"(Resource) Error downloading {url}: {ex.Message}");
            }
        }

        private static List<string> GetResourceUrls(HtmlDocument doc, string pageUrl)
        {
            var imageUrls = doc.DocumentNode
                .SelectNodes("//img[@src]")
                .Select(img => GetAbsoluteUrl(pageUrl, img.GetAttributeValue("src", "")));

            var stylesheetUrls = doc.DocumentNode
                .SelectNodes("//link[@rel='stylesheet']")
                .Select(link => GetAbsoluteUrl(pageUrl, link.GetAttributeValue("href", "")));

            var scriptUrls = doc.DocumentNode
                .SelectNodes("//script[@src]")
                .Select(script => GetAbsoluteUrl(pageUrl, script.GetAttributeValue("src", "")));
            
            // Room for other assets

            return imageUrls.Concat(stylesheetUrls).Concat(scriptUrls)
                .Where(url => url.StartsWith(rootUrl, StringComparison.OrdinalIgnoreCase)) // Skip external resources
                .ToList();
        }

        private static string GetRelativePath(string url, string rootUrl)
        {
            Uri baseUri = new Uri(rootUrl);
            Uri absoluteUri = new Uri(url);
            Uri relativeUri = baseUri.MakeRelativeUri(absoluteUri);

            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            // Check if the last segment is a filename and exclude it!
            string lastSegment = Path.GetFileName(relativePath);
            if (!string.IsNullOrEmpty(lastSegment) && lastSegment.Contains('.'))
            {
                int lastSegmentIndex = relativePath.LastIndexOf(lastSegment);
                relativePath = relativePath.Substring(0, lastSegmentIndex);
            }

            return relativePath;
        }
    }
}