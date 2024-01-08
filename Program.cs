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

            Console.WriteLine("Pre-fetching all page urls to download... Be patient, takes around 5-10 min.");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            getPageUrlsBlock = new ActionBlock<string>(
                async url => await GetPageUrls(url),
                bufferBlockOptions
            );

            getPageUrlsBlock.Post(rootUrl);
            
            int progressTicker = 0;
            int sanityChecks = 0;

            while (true) {
                progressTicker++;
                int progressSpinner = progressTicker % 51;

                Console.Write($"\rProgress: [{new string('#', progressSpinner)}{new string('_', 50 - progressSpinner)}] {uniquePageUrls.Count} urls collected");
                
                if (pageUrlsCollectorsRunning == pageUrlsCollectorsDone)
                    sanityChecks++;
                else
                    sanityChecks = 0;
                
                if (sanityChecks >= 3 && progressSpinner == 50) // checked 3 times, exit progress indicator neatly with 100%
                    break;

                // For faster debugging
                // if (uniquePageUrls.Count > 100)
                //     break;

                await Task.Delay(100);
            }

            getPageUrlsBlock.Complete();
            await getPageUrlsBlock.Completion;

            stopwatch.Stop();

            Console.WriteLine($"\nPage URL gathering took {stopwatch.ElapsedMilliseconds / 1000} seconds total");

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
                    foreach(var node in nodes) 
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
                foreach(var newUrl in newUrls) 
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
                await semaphore.WaitAsync();
                var client = new HttpClient();
                var html = await client.GetStringAsync(url);
                semaphore.Release();

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                string rootPath = outputDirectory;
                string relativePath = GetRelativePath(url);

                // Ensure directory exists
                Directory.CreateDirectory(Path.Combine(rootPath, relativePath));
                // Save html file
                await File.WriteAllTextAsync(Path.Combine(rootPath, relativePath, "index.html"), html);

                // Download remaining assets (e.g. styling, images, scripts)
                var resourceUrls = GetResourceUrls(doc);
                foreach (var resourceUrl in resourceUrls) 
                {
                    // Download
                }

            } 
            catch (Exception ex) 
            {
                Console.WriteLine($"Error fetching {url}: {ex.Message}");
            }

            // Increment the completed downloads count
            Interlocked.Increment(ref completedDownloads);
        }

        private static List<string> GetResourceUrls(HtmlDocument html)
        {
            return new List<string>();
        }

        private static string GetRelativePath(string url)
        {
            throw new NotImplementedException();
        }
    }
}