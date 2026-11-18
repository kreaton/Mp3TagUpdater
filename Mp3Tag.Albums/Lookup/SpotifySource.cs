using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mp3Tag.Albums.Lookup;

/// <summary>
/// Albums from the Spotify Web API, with the "client credentials" flow: the client id and secret of an app
/// on developer.spotify.com give a short-lived token. No user login, and no redirect URI is used.
/// </summary>
public sealed partial class SpotifySource(string? clientId, string? clientSecret) : IAlbumSource
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://api.spotify.com/v1/") };
    private string? _token;
    private DateTime _tokenExpires;

    public string Name => "Spotify";

    public string? SearchProblem =>
        string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)
            ? "Spotify needs a Client ID and Client secret. Add them in the settings."
            : null;

    /// <summary>10 is the most Spotify returns per page to apps in development mode; more is a 400 Bad Request.</summary>
    public int SearchLimit => 10;

    /// <summary>Accepts https://open.spotify.com/album/&lt;id&gt;?si=..., spotify:album:&lt;id&gt;, or a bare 22-character id.</summary>
    public string? ParseId(string input)
    {
        var m = AlbumId().Match(input.Trim());
        return m.Success ? m.Groups["id"].Value : null;
    }

    public async Task<IReadOnlyList<AlbumHit>> SearchAsync(string text, CancellationToken cancellation = default)
    {
        var result = await GetAsync<SearchResult>($"search?type=album&limit={SearchLimit}&q={Uri.EscapeDataString(text)}", cancellation);
        return result.Albums.Items
            .Select(a => new AlbumHit(a.Id, Artists(a.Artists), a.Name, a.TotalTracks,
                $"{Year(a.ReleaseDate)}, {a.TotalTracks} tracks".TrimStart(',', ' ')))
            .ToList();
    }

    public async Task<AlbumInfo> GetAlbumAsync(string id, CancellationToken cancellation = default)
    {
        // The album has the first 50 tracks; follow "next" links for the rest.
        var album = await GetAsync<Album>($"albums/{id}", cancellation);
        var tracks = new List<Track>(album.Tracks.Items);
        for (var next = album.Tracks.Next; next is not null;)
        {
            var page = await GetAsync<Paging<Track>>(next, cancellation);
            tracks.AddRange(page.Items);
            next = page.Next;
        }

        _ = int.TryParse(Year(album.ReleaseDate), out var year);
        return new AlbumInfo
        {
            Source = Name,
            Id = album.Id,
            Url = $"https://open.spotify.com/album/{album.Id}",
            Artist = Artists(album.Artists),
            Title = album.Name,
            Year = year,
            Genre = album.Genres?.FirstOrDefault(),
            Label = album.Label,
            Tracks = tracks
                .Select(t => new AlbumTrack(t.DiscNumber, t.TrackNumber, TrackArtist(t, album), t.Name, TimeSpan.FromMilliseconds(t.DurationMs)))
                .ToList(),
        };
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellation)
    {
        if (_token is not null && DateTime.UtcNow < _tokenExpires)
            return _token;
        if (SearchProblem is { } problem)
            throw new LookupException(problem);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }),
        };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId!.Trim()}:{clientSecret!.Trim()}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await _http.SendAsync(request, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new LookupException($"Spotify refused the keys ({(int)response.StatusCode}). Check the Client ID and Client secret under API keys.");
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellation)
            ?? throw new LookupException("Spotify's token response was empty.");

        _token = token.AccessToken;
        _tokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60));
        return _token;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellation));
        using var response = await _http.SendAsync(request, cancellation);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new LookupException($"Spotify asks to wait {response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 30:0} seconds before the next request.");
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest && url.StartsWith("albums/", StringComparison.Ordinal))
            throw new LookupException("Spotify has no album with that id. Check the URL.");
        if (!response.IsSuccessStatusCode)
            throw new LookupException($"Spotify returned {(int)response.StatusCode} {response.ReasonPhrase}{await ErrorMessageAsync(response, cancellation)}.");
        return await response.Content.ReadFromJsonAsync<T>(cancellation) ?? throw new LookupException("Spotify sent an empty response.");
    }

    /// <summary>": Invalid limit" from Spotify's { "error": { "message": ... } }, or nothing.</summary>
    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellation);
            return error?.Error?.Message is { Length: > 0 } message ? $": {message}" : "";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return "";
        }
    }

    // On compilations Spotify often lists the series as a second artist on every track
    // ("Monique, Åh Abe!"). Drop album-level artists when the track has others of its own.
    private static string TrackArtist(Track t, Album album)
    {
        var albumArtists = album.Artists.Select(a => a.Name).ToHashSet();
        var own = t.Artists.Where(a => !albumArtists.Contains(a.Name)).ToList();
        return Artists(own.Count > 0 ? own : t.Artists);
    }

    private static string Artists(IEnumerable<Artist> artists) => string.Join(", ", artists.Select(a => a.Name));

    private static string Year(string? releaseDate) => releaseDate is { Length: >= 4 } ? releaseDate[..4] : "";

    public void Dispose() => _http.Dispose();

    [GeneratedRegex(@"album[/:](?<id>[0-9A-Za-z]{22})|^(?<id>[0-9A-Za-z]{22})$")]
    private static partial Regex AlbumId();

    // --- Response shapes (only the fields used) ---

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record Artist([property: JsonPropertyName("name")] string Name);

    private sealed record Track(
        [property: JsonPropertyName("track_number")] int TrackNumber,
        [property: JsonPropertyName("disc_number")] int DiscNumber,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("duration_ms")] int DurationMs,
        [property: JsonPropertyName("artists")] List<Artist> Artists);

    private sealed record Paging<T>(
        [property: JsonPropertyName("items")] List<T> Items,
        [property: JsonPropertyName("next")] string? Next);

    private sealed record Album(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("release_date")] string? ReleaseDate,
        [property: JsonPropertyName("label")] string? Label,
        [property: JsonPropertyName("genres")] List<string>? Genres,
        [property: JsonPropertyName("artists")] List<Artist> Artists,
        [property: JsonPropertyName("tracks")] Paging<Track> Tracks);

    private sealed record SimpleAlbum(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("release_date")] string? ReleaseDate,
        [property: JsonPropertyName("total_tracks")] int TotalTracks,
        [property: JsonPropertyName("artists")] List<Artist> Artists);

    private sealed record SearchResult([property: JsonPropertyName("albums")] Paging<SimpleAlbum> Albums);

    private sealed record ErrorDetail([property: JsonPropertyName("message")] string? Message);

    private sealed record ErrorResponse([property: JsonPropertyName("error")] ErrorDetail? Error);
}
