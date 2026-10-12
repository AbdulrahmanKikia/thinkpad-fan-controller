using System.Net;
using System.Net.Http;

namespace LibraryFan.Setup;

/// <summary>
/// The payload is the latest public GitHub release asset. The installer does not embed it.
/// </summary>
public static class GitHubRelease
{
    public const string Owner = "AbdulrahmanKikia";
    public const string Repository = "thinkpad-fan-controller";
    public const string Asset = "LibraryFan-try.zip";
    public const int MaxBytes = 20 * 1024 * 1024;

    public static Uri LatestAsset { get; } = new(
        $"https://github.com/{Owner}/{Repository}/releases/latest/download/{Asset}");

    public static bool IsAllowed(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    public static async Task<byte[]> DownloadAsync(HttpMessageHandler handler, Uri? start = null, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(2) };
        var uri = start ?? LatestAsset;
        for (var hop = 0; hop < 5; hop++)
        {
            if (!IsAllowed(uri))
                throw new InvalidOperationException("The download left GitHub. The app was not installed.");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", "LibraryFanSetup");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var next = response.Headers.Location ?? throw new InvalidOperationException("GitHub sent a redirect with no destination.");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException("GitHub has no public LibraryFan-try.zip on the latest release. The app was not installed.");

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("GitHub did not return the package. The app was not installed.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0 || bytes.Length > MaxBytes)
                throw new InvalidOperationException("The GitHub package is the wrong size. The app was not installed.");
            return bytes;
        }

        throw new InvalidOperationException("The download left GitHub. The app was not installed.");
    }
}
