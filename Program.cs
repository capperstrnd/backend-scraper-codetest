using System.Diagnostics;
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

        static async Task Main()
        {
            HashSet<string> uniquePageUrls = new HashSet<string>();

            // Ensure output exists
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            Console.WriteLine("Pre-fetching all page urls to download... Be patient, takes around 5-10 min.");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            await GetPageUrls(rootUrl, rootUrl, uniquePageUrls);
            
            stopwatch.Stop();

            Console.WriteLine($"Gathered {uniquePageUrls.Count} urls, it took {stopwatch.ElapsedMilliseconds / 1000}sec");

            // TODO: Set up thread pooling to run things in parallell

            // TODO: Some kind of progress indicator based on the number of pageUrls processed
        }

        static async Task GetPageUrls(string rootUrl, string currentUrl, HashSet<string> uniquePageUrls, string prevUrl = "")
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

        static void DownloadPage(string url, string rootUrl, string outputDirectory)
        {
            // TODO

            // Replace all internal href's with local file path 
        }
    }
}