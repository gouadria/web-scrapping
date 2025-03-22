using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using WebScrapingApp.Data.Models;

namespace WebScrapingApp.Data.Services
{
    public class ScraperService
    {
        private readonly ILogger<ScraperService> _logger;
        private const string HarajUrl = "https://haraj.com.sa";
        private const int DesiredCount = 1000; // Nombre maximum de publications souhaitées

        public ScraperService(ILogger<ScraperService> logger)
        {
            _logger = logger;
        }

        // Extraction principale d'une URL
        public async Task<List<HarajListing>> ScrapeAsync(string url)
        {
            var listings = new List<HarajListing>();

            try
            {
                _logger.LogInformation("Récupération du contenu dynamique de : {Url}", url);
                string renderedHtml = GetRenderedHtml(url);
                _logger.LogDebug("HTML récupéré (jusqu'à 20 000 caractères): {Snippet}",
                    renderedHtml.Substring(0, Math.Min(20000, renderedHtml.Length)));

                var config = Configuration.Default;
                var context = BrowsingContext.New(config);
                var document = await context.OpenAsync(req => req.Content(renderedHtml));

                _logger.LogInformation("Extraction des publications...");
                var items = document.QuerySelectorAll("a h3");
                _logger.LogInformation("Publications trouvées: {Count}", items.Length);

                foreach (var item in items)
                {
                    var listing = new HarajListing();
                    listing.Title = item.TextContent.Trim();

                    // Remonter jusqu'à l'élément <a> parent
                    IElement parentA = item;
                    while (parentA != null && parentA.NodeName.ToLower() != "a")
                        parentA = parentA.ParentElement;
                    if (parentA != null)
                    {
                        var href = parentA.GetAttribute("href") ?? "";
                        listing.Url = href.StartsWith("http") ? href : HarajUrl + href;
                    }
                    else
                    {
                        listing.Url = "N/A";
                    }

                    IElement container = parentA?.ParentElement;
                    listing.Price = container?.QuerySelector("div.mb-8.flex.w-full.justify-end.px-2 > strong")?.TextContent.Trim() ?? "non défini";
                    listing.Localisation = container?.QuerySelector("span.city")?.TextContent.Trim() ?? "N/A";

                    // Extraction du numéro de téléphone via la page de détail
                    if (listing.Url != "N/A")
                    {
                        _logger.LogInformation("Extraction du téléphone pour la publication : {Url}", listing.Url);
                        listing.Phone = GetAuthenticatedPhone(listing.Url);
                        if (listing.Phone.Trim().Equals("تواصل", StringComparison.OrdinalIgnoreCase)
                            || !Regex.IsMatch(listing.Phone, @"\d"))
                        {
                            listing.Phone = "non défini";
                        }
                    }
                    else
                    {
                        listing.Phone = "non défini";
                    }

                    listing.Description = container?.QuerySelector("article[data-testid='post-article']")?.TextContent.Trim() ?? "N/A";
                    listing.Name = container?.QuerySelector("a[data-testid='post-author']")?.TextContent.Trim() ?? "N/A";

                    // Si certaines infos sont manquantes, essayer d'aller sur la page détail
                    if (listing.Url != "N/A" && (listing.Price == "non défini" || listing.Description == "N/A" || listing.Phone == "non défini" || listing.Name == "N/A"))
                    {
                        var detailFields = await GetDetailFieldsAsync(listing.Url);
                        if (detailFields.ContainsKey("price") && detailFields["price"] != "N/A")
                            listing.Price = detailFields["price"];
                        if (detailFields.ContainsKey("localisation") && detailFields["localisation"] != "N/A")
                            listing.Localisation = detailFields["localisation"];
                        if (detailFields.ContainsKey("phone") && detailFields["phone"] != "N/A")
                            listing.Phone = detailFields["phone"];
                        if (detailFields.ContainsKey("description") && detailFields["description"] != "N/A")
                            listing.Description = detailFields["description"];
                        if (detailFields.ContainsKey("name") && detailFields["name"] != "N/A")
                            listing.Name = detailFields["name"];
                    }

                    listings.Add(listing);
                    if (listings.Count >= DesiredCount)
                        break;
                }
                _logger.LogInformation("Extraction terminée. {Count} publications récupérées depuis {Url}", listings.Count, url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du scraping pour l'URL: {Url}", url);
            }
            return listings;
        }

        // Extraction de plusieurs sections via une liste d'URLs
        public async Task<List<HarajListing>> ScrapeMultipleSectionsAsync(List<string> urls)
        {
            var allListings = new List<HarajListing>();
            foreach (var url in urls)
            {
                _logger.LogInformation("Scraping de la section : {SectionUrl}", url);
                var listings = await ScrapeAsync(url);
                allListings.AddRange(listings);
                if (allListings.Count >= DesiredCount)
                {
                    allListings = allListings.Take(DesiredCount).ToList();
                    break;
                }
            }
            _logger.LogInformation("Extraction multi-sections terminée. Total : {Count}", allListings.Count);
            return allListings;
        }

        // Extraction de toutes les sections depuis une page d'accueil
        public async Task<List<HarajListing>> ScrapeAllSectionsAsync(string homepageUrl)
        {
            var allListings = new List<HarajListing>();

            try
            {
                _logger.LogInformation("Extraction des liens de section depuis la page d'accueil...");
                string homepageHtml = GetRenderedHtml(homepageUrl);
                var config = Configuration.Default;
                var context = BrowsingContext.New(config);
                var document = await context.OpenAsync(req => req.Content(homepageHtml));

                var sectionAnchors = document.QuerySelectorAll("div.custom-scroll.flex.max-w-full.flex-nowrap.overflow-x-auto.items-center a");
                _logger.LogInformation("Nombre de sections trouvées : {Count}", sectionAnchors.Length);

                List<string> sectionUrls = new List<string>();
                foreach (var anchor in sectionAnchors)
                {
                    var href = anchor.GetAttribute("href") ?? "";
                    if (!string.IsNullOrEmpty(href))
                    {
                        string fullUrl = href.StartsWith("http") ? href : HarajUrl + href;
                        sectionUrls.Add(fullUrl);
                    }
                }

                foreach (var sectionUrl in sectionUrls)
                {
                    _logger.LogInformation("Scraping de la section : {SectionUrl}", sectionUrl);
                    var listings = await ScrapeAsync(sectionUrl);
                    allListings.AddRange(listings);
                    if (allListings.Count >= DesiredCount)
                    {
                        allListings = allListings.Take(DesiredCount).ToList();
                        break;
                    }
                }

                _logger.LogInformation("Total publications extraites de toutes les sections : {Count}", allListings.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du scraping de la page d'accueil.");
            }

            return allListings;
        }

        // Méthode modifiée pour augmenter le nombre d'annonces chargées via scrolling
        private string GetRenderedHtml(string url)
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            string pageSource = string.Empty;
            using (IWebDriver driver = new ChromeDriver(options))
            {
                driver.Navigate().GoToUrl(url);
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
                wait.Until(ExpectedConditions.ElementExists(By.CssSelector("a h3")));

                int iterations = 0;
                int maxIterations = 150; // Augmenté pour permettre plus de chargement
                int lastHeight = Convert.ToInt32(((IJavaScriptExecutor)driver).ExecuteScript("return document.body.scrollHeight"));
                while (iterations < maxIterations)
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                    System.Threading.Thread.Sleep(8000); // Pause augmentée à 8 secondes
                    int newHeight = Convert.ToInt32(((IJavaScriptExecutor)driver).ExecuteScript("return document.body.scrollHeight"));
                    try
                    {
                        var loadMoreButton = driver.FindElement(By.CssSelector("button.load-more"));
                        loadMoreButton.Click();
                        System.Threading.Thread.Sleep(8000); // Pause après le clic
                    }
                    catch (NoSuchElementException) { }
                    if (newHeight == lastHeight)
                        break;
                    lastHeight = newHeight;
                    iterations++;
                }
                pageSource = driver.PageSource;
                driver.Quit();
            }
            return pageSource;
        }

        private async Task<Dictionary<string, string>> GetDetailFieldsAsync(string detailUrl)
        {
            var detailFields = new Dictionary<string, string>
            {
                { "price", "N/A" },
                { "localisation", "N/A" },
                { "phone", "N/A" },
                { "description", "N/A" },
                { "name", "N/A" }
            };

            int retryCount = 3;
            while (retryCount > 0)
            {
                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        // En-têtes pour imiter un navigateur
                        httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36");
                        httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                        httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                        httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
                        httpClient.DefaultRequestHeaders.Referrer = new Uri(HarajUrl);

                        var detailHtml = await httpClient.GetStringAsync(detailUrl);
                        var config = Configuration.Default;
                        var context = BrowsingContext.New(config);
                        var document = await context.OpenAsync(req => req.Content(detailHtml));

                        var priceElement = document.QuerySelector("div.mb-8.flex.w-full.justify-end.px-2 > strong")
                                           ?? document.QuerySelector(".price")
                                           ?? document.QuerySelector(".item-price");
                        if (priceElement != null)
                            detailFields["price"] = priceElement.TextContent.Trim();

                        var localisationElement = document.QuerySelector("span.city");
                        if (localisationElement != null)
                            detailFields["localisation"] = localisationElement.TextContent.Trim();

                        var phoneElement = document.QuerySelector("button[data-testid='post-contact']");
                        if (phoneElement != null)
                        {
                            string phoneText = phoneElement.TextContent.Trim();
                            if (phoneText.Equals("تواصل", StringComparison.OrdinalIgnoreCase))
                                detailFields["phone"] = GetAuthenticatedPhone(detailUrl);
                            else
                                detailFields["phone"] = phoneText;
                        }
                        else
                        {
                            detailFields["phone"] = GetAuthenticatedPhone(detailUrl);
                        }

                        var descriptionElement = document.QuerySelector("article[data-testid='post-article']")
                                                 ?? document.QuerySelector("article")
                                                 ?? document.QuerySelector("p");
                        if (descriptionElement != null)
                            detailFields["description"] = descriptionElement.TextContent.Trim();

                        var nameElement = document.QuerySelector("a[data-testid='post-author']");
                        if (nameElement != null)
                            detailFields["name"] = nameElement.TextContent.Trim();
                    }
                    break; // Si tout se passe bien, sortir de la boucle
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Erreur HTTP lors de l'extraction des détails de {DetailUrl}", detailUrl);
                    retryCount--;
                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'extraction des détails de {DetailUrl}", detailUrl);
                    break;
                }
            }
            return detailFields;
        }

        // Méthode GetAuthenticatedPhone avec vérification finale pour éviter "تواصل"
        private string GetAuthenticatedPhone(string listingUrl)
        {
            string phone = "non défini";
            var options = new ChromeOptions();
            // Pour le débogage, décommentez la ligne suivante si besoin :
            // options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            using (IWebDriver driver = new ChromeDriver(options))
            {
                try
                {
                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));

                    // --- Connexion sur la page d'accueil ---
                    _logger.LogInformation("Étape 1 : Ouverture de la page d'accueil : {HarajUrl}", HarajUrl);
                    driver.Navigate().GoToUrl(HarajUrl);

                    _logger.LogInformation("Étape 2 : Recherche du bouton de connexion via XPath...");
                    wait.Until(ExpectedConditions.ElementToBeClickable(By.XPath("//span[contains(text(),'دخــــول')]")));
                    var loginButtonSpan = driver.FindElement(By.XPath("//span[contains(text(),'دخــــول')]"));
                    _logger.LogInformation("Étape 3 : Clic sur le bouton de connexion...");
                    loginButtonSpan.Click();

                    _logger.LogInformation("Étape 4 : Attente de l'apparition du modal de connexion...");
                    wait.Until(ExpectedConditions.ElementIsVisible(By.CssSelector("#modal-test > div > div > div")));
                    _logger.LogInformation("Modal de connexion affiché.");

                    _logger.LogInformation("Étape 5 : Remplissage du champ 'username'...");
                    var usernameField = driver.FindElement(By.Id("username"));
                    usernameField.Clear();
                    usernameField.SendKeys("anis2240");
                    _logger.LogInformation("Champ 'username' rempli.");

                    _logger.LogInformation("Étape 6 : Clic sur le bouton 'التالي'...");
                    wait.Until(ExpectedConditions.ElementToBeClickable(By.CssSelector("#modal-test > div > div > div > form > button > span")));
                    var nextButton = driver.FindElement(By.CssSelector("#modal-test > div > div > div > form > button > span"));
                    nextButton.Click();
                    _logger.LogInformation("Bouton 'التالي' cliqué.");

                    _logger.LogInformation("Étape 7 : Remplissage du champ 'password'...");
                    wait.Until(ExpectedConditions.ElementIsVisible(By.Id("password")));
                    var passwordField = driver.FindElement(By.Id("password"));
                    passwordField.Clear();
                    passwordField.SendKeys("Gouadria@1982");
                    _logger.LogInformation("Champ 'password' rempli.");

                    _logger.LogInformation("Étape 8 : Clic sur le bouton de validation de connexion...");
                    wait.Until(ExpectedConditions.ElementToBeClickable(By.CssSelector("#modal-test > div > div > div > form > button")));
                    var loginButton = driver.FindElement(By.CssSelector("#modal-test > div > div > div > form > button"));
                    loginButton.Click();
                    _logger.LogInformation("Bouton de validation cliqué.");

                    _logger.LogInformation("Étape 9 : Attente de la disparition du modal de connexion...");
                    wait.Until(ExpectedConditions.InvisibilityOfElementLocated(By.CssSelector("#modal-test")));
                    _logger.LogInformation("Modal de connexion disparu. Connexion probablement réussie.");

                    // --- Navigation vers la page de détail de la publication ---
                    _logger.LogInformation("Étape 10 : Navigation vers la page de détail de la publication : {listingUrl}", listingUrl);
                    driver.Navigate().GoToUrl(listingUrl);

                    // --- Recherche du bouton "تواصل" ---
                    _logger.LogInformation("Étape 11 : Recherche du bouton 'تواصل' via XPath générique...");
                    string contactXPath = "//button[contains(normalize-space(.), 'تواصل')]";
                    wait.Until(ExpectedConditions.ElementToBeClickable(By.XPath(contactXPath)));
                    var contactButton = driver.FindElement(By.XPath(contactXPath));
                    _logger.LogInformation("Bouton 'تواصل' trouvé via XPath.");

                    _logger.LogInformation("Étape 12 : Clic sur le bouton 'تواصل'...");
                    contactButton.Click();

                    _logger.LogInformation("Étape 13 : Attente du modal de contact...");
                    string targetSelector = "#modal-test > div > div > div > a:nth-child(3) > div:nth-child(2)";
                    wait.Until(ExpectedConditions.ElementIsVisible(By.CssSelector(targetSelector)));

                    phone = wait.Until(driverObj =>
                    {
                        try
                        {
                            var phoneElement = driverObj.FindElement(By.CssSelector(targetSelector));
                            string text = phoneElement.Text.Trim();
                            return !text.Equals("تواصل", StringComparison.OrdinalIgnoreCase) ? text : null;
                        }
                        catch
                        {
                            return null;
                        }
                    });

                    if (string.IsNullOrEmpty(phone))
                    {
                        var phoneElement = driver.FindElement(By.CssSelector(targetSelector));
                        string href = phoneElement.GetAttribute("href") ?? "";
                        if (!string.IsNullOrEmpty(href) && href.StartsWith("tel:"))
                            phone = href.Substring("tel:".Length).Trim();
                        else
                            phone = phoneElement.Text.Trim();
                    }

                    if (phone.Trim().Equals("تواصل", StringComparison.OrdinalIgnoreCase)
                        || !Regex.IsMatch(phone, @"\d"))
                    {
                        phone = "non défini";
                    }

                    _logger.LogInformation("Numéro de téléphone récupéré : {Phone}", phone);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de la récupération du téléphone authentifié pour {listingUrl}", listingUrl);
                }
                finally
                {
                    driver.Quit();
                }
            }
            return phone;
        }

        public byte[] ExportToExcel(List<HarajListing> listings)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Publications");

            worksheet.Cells[1, 1].Value = "Titre";
            worksheet.Cells[1, 2].Value = "Prix";
            worksheet.Cells[1, 3].Value = "URL";
            worksheet.Cells[1, 4].Value = "Localisation";
            worksheet.Cells[1, 5].Value = "Téléphone";
            worksheet.Cells[1, 6].Value = "Description";
            worksheet.Cells[1, 7].Value = "Name";

            for (int i = 0; i < listings.Count; i++)
            {
                worksheet.Cells[i + 2, 1].Value = listings[i].Title;
                worksheet.Cells[i + 2, 2].Value = listings[i].Price;
                worksheet.Cells[i + 2, 3].Value = listings[i].Url;
                worksheet.Cells[i + 2, 4].Value = listings[i].Localisation;
                worksheet.Cells[i + 2, 5].Value = listings[i].Phone;
                worksheet.Cells[i + 2, 6].Value = listings[i].Description;
                worksheet.Cells[i + 2, 7].Value = listings[i].Name;
            }
            return package.GetAsByteArray();
        }
    }
}







