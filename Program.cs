using HtmlAgilityPack;

namespace BackendScraper
{

    class Program
    {
        static async Task Main()
        {
            string rootUrl = "https://books.toscrape.com/";
            string outputDirectory = "./DownloadOutput";
            HashSet<string> uniquePageUrls = new HashSet<string>();

            int maxParallelDownloads = 8;

            // Ensure output exists
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            Console.WriteLine("Pre-fetching all page urls to download...");

            await GetPageUrls(rootUrl, rootUrl, uniquePageUrls);

            Console.WriteLine(uniquePageUrls.Count);

            foreach (var url in uniquePageUrls)
                Console.WriteLine(url);

            // TODO: Set up thread pooling to run things in parallell

            // TODO: Some kind of progress indicator based on the number of pageUrls processed
        }

        static async Task GetPageUrls(string rootUrl, string currentUrl, HashSet<string> uniquePageUrls, string prevUrl = "")
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var html = await client.GetStringAsync(currentUrl);
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
                                uniquePageUrls.Add(absoluteUrl);
                                // Continue fetching recursively
                                await GetPageUrls(rootUrl, absoluteUrl, uniquePageUrls, prevUrl = currentUrl);
                            }
                        }
                    }
                }
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