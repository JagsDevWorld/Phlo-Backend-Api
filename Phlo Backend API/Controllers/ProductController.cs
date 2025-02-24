using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Phlo_Backend_API.Models;
using System.Text.RegularExpressions;

namespace Phlo_Backend_API.Controllers
{
    [ApiController]
    [Route("filter")]
    public class ProductController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ProductController> _logger;
        private const string PrimaryDataUrl = "https://run.mocky.io/v3/cc147902-4a5a-4b1a-bc00-2220bafb49fd";
        private const string BackupDataUrl = "https://pastebin.com/raw/JucRNpWs";

        public ProductController(IHttpClientFactory httpClientFactory, ILogger<ProductController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetFilteredProducts([FromQuery] decimal? minPrice = null, [FromQuery] decimal? maxPrice = null, [FromQuery] string? size = null, [FromQuery] string? highlight = null)
        {
            var products = await FetchProducts();
            if (products == null) return StatusCode(503, "Service unavailable. Could not fetch product data.");

            var filteredProducts = products.AsQueryable().ToList();

            // Apply filtering
            if (minPrice.HasValue)
                filteredProducts = filteredProducts.Where(p => p.Price >= minPrice).ToList();
            if (maxPrice.HasValue)
                filteredProducts = filteredProducts.Where(p => p.Price <= maxPrice).ToList();
            if (!string.IsNullOrEmpty(size))
                filteredProducts = filteredProducts.Where(p => p.Size?.Equals(size, StringComparison.OrdinalIgnoreCase) == true).ToList();

            // Apply highlighting
            if (!string.IsNullOrEmpty(highlight))
            {
                var wordsToHighlight = highlight.Split(',');
                foreach (var product in filteredProducts)
                {
                    product.Description = HighlightWords(product.Description, wordsToHighlight);
                }
            }

            // Create filter metadata
            var filterData = new
            {
                MinPrice = products.Min(p => p.Price),
                MaxPrice = products.Max(p => p.Price),
                Sizes = products.Select(p => p.Size).Distinct().ToArray(),
                CommonWords = GetMostCommonWords(products.Select(p => p.Description), 10)
            };

            return Ok(new { Filter = filterData, Products = filteredProducts });
        }

        private async Task<List<Product>> FetchProducts()
        {
            var client = _httpClientFactory.CreateClient();
            try
            {
                var response = await client.GetStringAsync(PrimaryDataUrl);
                _logger.LogInformation("Received response from Primary URL");

                var jsonObject = JsonConvert.DeserializeObject<dynamic>(response);
                var productsJson = jsonObject.products.ToString();
                return JsonConvert.DeserializeObject<List<Product>>(productsJson);
            }
            catch (HttpRequestException)
            {
                _logger.LogWarning("Primary URL failed. Attempting backup URL.");
                try
                {
                    var backupResponse = await client.GetStringAsync(BackupDataUrl);
                    _logger.LogInformation("Received response from Backup URL");

                    var jsonObject = JsonConvert.DeserializeObject<dynamic>(backupResponse);
                    var productsJson = jsonObject.products.ToString();
                    return JsonConvert.DeserializeObject<List<Product>>(productsJson);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError("Failed to fetch product data from both sources: {Message}", ex.Message);
                    return null;
                }
            }
        }


        private static string HighlightWords(string description, string[] words)
        {
            foreach (var word in words)
            {
                var regex = new Regex($"\\b{Regex.Escape(word)}\\b", RegexOptions.IgnoreCase);
                description = regex.Replace(description, match => $"<em>{match.Value}</em>");
            }
            return description;
        }

        private static string[] GetMostCommonWords(IEnumerable<string> descriptions, int count)
        {
            var commonWords = new HashSet<string> { "the", "and", "for", "with", "this" }; // Excluding most common words
            return descriptions
                .SelectMany(d => d.Split(new[] { ' ', ',', '.', '!' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(word => !commonWords.Contains(word.ToLower()))
                .GroupBy(word => word.ToLower())
                .OrderByDescending(g => g.Count())
                .Take(count)
                .Select(g => g.Key)
                .ToArray();
        }
    }

}
