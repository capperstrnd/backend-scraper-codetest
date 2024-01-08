using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using HtmlAgilityPack;

namespace BackendScraper
{

    class Program
    {
        private static string rootUrl = "https://books.toscrape.com/";
        private static string outputDirectory = "./DownloadOutput";
        private static int maxRetries = 5; // Max Retries for HttpClient
        private static int maxParallelDownloads = 8;
        private static SemaphoreSlim semaphore = new SemaphoreSlim(12); // Limit concurrent requests
        private static int completedDownloads = 0;

        static async Task Main()
        {
            HashSet<string> uniquePageUrls = new HashSet<string>();

            // Ensure output exists
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            Console.WriteLine("Pre-fetching all page urls to download... Be patient, takes around 5-10 min.");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            // Fix progress indicator on this, except here it just spins and resets with the number at the end being the total collected
            await GetPageUrls(rootUrl, rootUrl, uniquePageUrls);
            
            stopwatch.Stop();

            Console.WriteLine($"Gathered {uniquePageUrls.Count} urls, it took {stopwatch.ElapsedMilliseconds / 1000}sec");

             // Configure thread pool
            var downloadOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxParallelDownloads
            };

            // Use a BufferBlock to store URLs and propagate them to parallel downloads
            var downloadBlock = new ActionBlock<string>(
                async url => await DownloadPage(url, rootUrl, outputDirectory),
                downloadOptions
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

        static async Task GetPageUrls(string rootUrl, string currentUrl, HashSet<string> uniquePageUrls)
        {
            int retryCount = 0;

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
                Parallel.ForEach(newUrls, newUrl => 
                {
                    uniquePageUrls.Add(newUrl);
                    GetPageUrls(rootUrl, newUrl, uniquePageUrls).Wait();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error on url {currentUrl}: {ex.Message}");
            }
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