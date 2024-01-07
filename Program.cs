namespace BackendScraper
{
    class Program
    {
        static void Main()
        {
            string rootUrl = "https://books.toscrape.com/";
            string outputDirectory = "./DownloadOutput";
            int maxParallelDownloads = 8;

            // Ensure output exists
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            // Pre-fetch all page urls to download
            List<string> pageUrls = GetPageUrls(rootUrl);

            // TODO: Set up thread pooling to run things in parallell

            // TODO: Some kind of progress indicator based on the number of pageUrls processed

            Console.WriteLine("Hello, World!");
        }

        static List<string> GetPageUrls(string rootUrl)
        {
            // TODO
            
            // Recursively traverse and gather up all urls

            return new List<string>();
        }

        static void DownloadPage(string url, string rootUrl, string outputDirectory) {
            // TODO

            // Replace all internal href's with local file path 
        }
    }
}