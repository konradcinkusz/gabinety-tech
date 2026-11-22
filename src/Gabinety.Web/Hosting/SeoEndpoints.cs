using System.Globalization;
using System.Security;
using System.Text;
using Gabinety.Web.Api;
using Microsoft.AspNetCore.OutputCaching;

namespace Gabinety.Web.Hosting;

/// <summary>The pure content of <c>sitemap.xml</c> and <c>robots.txt</c>: the rules of the Next.js app (<c>web/src/lib/seo/sitemap.ts</c>), so the two hosts say the same to a crawler.</summary>
public static class SeoContent
{
    /// <summary>Paths crawlers must not visit (SPEC 6.5).</summary>
    public static readonly string[] DisallowedPaths = ["/moje", "/gabinet-programisty", "/admin", "/rezerwacja", "/checkout", "/api"];

    /// <summary>Every approved practitioner and only the categories the API marks indexable (a thin category is <c>noindex</c> and so is not offered).</summary>
    public static string Sitemap(string baseUrl, SitemapDto dto)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        foreach (var c in dto.Categories.Where(c => c.Indexable))
            xml.Append("<url><loc>").Append(SecurityElement.Escape(baseUrl + c.Path)).Append("</loc></url>\n");
        foreach (var p in dto.Practitioners ?? [])
            xml.Append("<url><loc>").Append(SecurityElement.Escape($"{baseUrl}/gabinet/{Uri.EscapeDataString(p.Slug)}")).Append("</loc><lastmod>")
               .Append(p.LastModified.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)).Append("</lastmod></url>\n");
        return xml.Append("</urlset>\n").ToString();
    }

    public static string Robots(string baseUrl) =>
        "User-Agent: *\nAllow: /\n" + string.Concat(DisallowedPaths.Select(p => $"Disallow: {p}\n")) + $"\nSitemap: {baseUrl}/sitemap.xml\n";
}

public static class SeoEndpoints
{
    /// <summary><c>/sitemap.xml</c> and <c>/robots.txt</c>: anonymous, cached like the pages. A Directory that does not answer is a 503, never an empty sitemap (a crawler would take that as "everything was removed").</summary>
    public static WebApplication MapSeoEndpoints(this WebApplication app)
    {
        app.MapGet("/sitemap.xml", async (DirectoryClient directory, WebOptions options, CancellationToken ct) =>
        {
            var result = await directory.SitemapAsync(ct);
            return result.IsOk ? Results.Text(SeoContent.Sitemap(options.SiteBaseUrl, result.Value!), "application/xml", Encoding.UTF8) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous().CacheOutput(PublicPageCachePolicy.Name);
        app.MapGet("/robots.txt", (WebOptions options) => Results.Text(SeoContent.Robots(options.SiteBaseUrl), "text/plain", Encoding.UTF8)).AllowAnonymous().CacheOutput(PublicPageCachePolicy.Name);
        return app;
    }
}
