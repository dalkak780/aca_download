using System.Net;
using ArcaDownloader.Core.Services;

namespace ArcaDownloader.Core.Download;

public static class HttpClientFactory
{
    public static HttpClient Create(Uri referer, CookieContainer cookies)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ko-KR,ko;q=0.9,en;q=0.8");
        client.DefaultRequestHeaders.Referrer = referer;
        return client;
    }

}
