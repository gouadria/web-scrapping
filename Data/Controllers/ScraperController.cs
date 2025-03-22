using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebScrapingApp.Data.Services;
using WebScrapingApp.Data.Models;

namespace WebScrapingApp.Data.Controllers
{
    [ApiController]
    [Route("api/scraper")]
    public class ScraperController : ControllerBase
    {
        private readonly ScraperService _scraperService;

        public ScraperController(ScraperService scraperService)
        {
            _scraperService = scraperService;
        }

        [HttpGet("scrape-data")]
        public async Task<IActionResult> ScrapeData([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url))
                return BadRequest(new { error = "L'URL est requise." });
            var data = await _scraperService.ScrapeAsync(url);
            if (data == null || data.Count == 0)
                return NotFound(new { error = "Aucune donnée trouvée." });
            return Ok(new { publications = data });
        }

        [HttpGet("export-excel")]
        public async Task<IActionResult> ExportExcel([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url))
                return BadRequest(new { error = "L'URL est requise." });
            var data = await _scraperService.ScrapeAsync(url);
            if (data == null || data.Count == 0)
                return NotFound(new { error = "Aucune donnée trouvée." });
            var excelBytes = _scraperService.ExportToExcel(data);
            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "publications.xlsx");
        }

        [HttpGet("scrape-multi")]
        public async Task<IActionResult> ScrapeMultiple([FromQuery] string urls)
        {
            if (string.IsNullOrEmpty(urls))
                return BadRequest(new { error = "Les URLs sont requises (séparées par des virgules)." });
            var urlList = urls.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(u => u.Trim())
                              .ToList();
            var data = await _scraperService.ScrapeMultipleSectionsAsync(urlList);
            if (data == null || data.Count == 0)
                return NotFound(new { error = "Aucune donnée trouvée." });
            return Ok(new { publications = data });
        }

        [HttpGet("export-excel-multi")]
        public async Task<IActionResult> ExportExcelMultiple([FromQuery] string urls)
        {
            if (string.IsNullOrEmpty(urls))
                return BadRequest(new { error = "Les URLs sont requises (séparées par des virgules)." });
            var urlList = urls.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(u => u.Trim())
                              .ToList();
            var data = await _scraperService.ScrapeMultipleSectionsAsync(urlList);
            if (data == null || data.Count == 0)
                return NotFound(new { error = "Aucune donnée trouvée." });
            var excelBytes = _scraperService.ExportToExcel(data);
            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "publications.xlsx");
        }

        [HttpGet("scrape-all")]
        public async Task<IActionResult> ScrapeAll([FromQuery] string homepageUrl)
        {
            if (string.IsNullOrEmpty(homepageUrl))
                return BadRequest(new { error = "L'URL de la page d'accueil est requise." });
            var data = await _scraperService.ScrapeAllSectionsAsync(homepageUrl);
            if (data == null || data.Count == 0)
                return NotFound(new { error = "Aucune donnée trouvée." });
            return Ok(new { publications = data });
        }

        [HttpGet("export-excel-all")]
        public async Task<IActionResult> ExportExcelAll([FromQuery] string homepageUrl)
        {
            if (string.IsNullOrEmpty(homepageUrl))
                return BadRequest(new { error = "L'URL de la page d'accueil est requise." });
            var data = await _scraperService.ScrapeAllSectionsAsync(homepageUrl);
            if (data == null || data.Count == 0)
                return NotFound(new { error = "Aucune donnée trouvée." });
            var excelBytes = _scraperService.ExportToExcel(data);
            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "publications.xlsx");
        }
    }
}



