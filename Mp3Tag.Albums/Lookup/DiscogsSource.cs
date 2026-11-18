using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mp3Tag.Albums.Lookup;

/// <summary>
/// Releases from the Discogs API. Reading a release by URL or id needs no account; searching needs a personal
/// access token. Data provided by Discogs.
/// </summary>
/// <remarks>Ids are "r123" for a release and "m123" for a master, which is resolved to its main release.</remarks>
public sealed partial class DiscogsSource : IAlbumSource
{
    private const int PageSize = 25;
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://api.discogs.com/") };
    private readonly bool _canSearch;

    /// <param name="token">The personal access token from Discogs developer settings, or null to read releases only.</param>
    public DiscogsSource(string? token)
    {
        // Discogs rejects requests without a descriptive User-Agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mp3TagFromApi/1.0");

        _canSearch = !string.IsNullOrWhiteSpace(token);
        if (_canSearch)
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Discogs token={token!.Trim()}");
    }

    public string Name => "Discogs";

    public string? SearchProblem => _canSearch ? null
        : "Searching Discogs needs a personal access token. Add it in the settings, or paste a release URL.";

    public int SearchLimit => PageSize;

    /// <summary>Remaining requests in the current minute, as Discogs last reported (25 without a token, 60 with).</summary>
    public int? RateLimitRemaining { get; private set; }

    /// <summary>
    /// https://www.discogs.com/release/1605685-Various-Tangokat, /da/release/..., [r1605685] or 1605685 → "r1605685";
    /// .../master/1265218-... or [m1265218] → "m1265218".
    /// </summary>
    public string? ParseId(string input)
    {
        var trimmed = input.Trim();
        var release = ReleaseId().Match(trimmed);
        if (release.Success)
            return "r" + (release.Groups[1].Success ? release.Groups[1].Value : release.Groups[2].Value);
        var master = MasterId().Match(trimmed);
        if (master.Success)
            return "m" + (master.Groups[1].Success ? master.Groups[1].Value : master.Groups[2].Value);
        return null;
    }

    public async Task<IReadOnlyList<AlbumHit>> SearchAsync(string text, CancellationToken cancellation = default)
    {
        if (SearchProblem is { } problem)
            throw new LookupException(problem);

        var result = await GetAsync<SearchResult>($"database/search?type=release&per_page={PageSize}&q={Uri.EscapeDataString(text)}", cancellation);

        // Search results have no track counts, so show what tells editions apart instead.
        return result.Results.Select(r =>
        {
            // "James Taylor (2) - Gorilla" → artist "James Taylor", title "Gorilla"
            var title = NumberSuffix().Replace(r.Title, "");
            var dash = title.IndexOf(" - ", StringComparison.Ordinal);
            var (artist, album) = dash > 0 ? (title[..dash], title[(dash + 3)..]) : ("", title);
            var details = string.Join(", ", new[]
            {
                r.Year, r.Country,
                r.Format is { Count: > 0 } f ? string.Join(" ", f.Distinct()) : null,
                string.Join(" ", new[] { r.Label?.FirstOrDefault(), r.Catno }.Where(s => !string.IsNullOrWhiteSpace(s))),
            }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return new AlbumHit($"r{r.Id}", artist, album, null, details);
        }).ToList();
    }

    public async Task<AlbumInfo> GetAlbumAsync(string id, CancellationToken cancellation = default)
    {
        var releaseId = id;
        if (id.StartsWith('m'))
        {
            var master = await GetAsync<Master>($"masters/{id[1..]}", cancellation);
            releaseId = master.MainRelease.ToString();
        }
        else if (id.StartsWith('r'))
        {
            releaseId = id[1..];
        }

        var release = await GetAsync<Release>($"releases/{releaseId}", cancellation);
        return ToAlbumInfo(release);
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellation)
    {
        using var response = await _http.GetAsync(url, cancellation);

        if (response.Headers.TryGetValues("X-Discogs-Ratelimit-Remaining", out var left) && int.TryParse(left.First(), out var remaining))
            RateLimitRemaining = remaining;

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new LookupException("Discogs allows only so many requests a minute. Wait a minute and try again.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new LookupException("Discogs refused the personal access token. Check it in the settings.");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new LookupException("Discogs has no release with that id. Check the URL.");
        if (!response.IsSuccessStatusCode)
            throw new LookupException($"Discogs returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        return await response.Content.ReadFromJsonAsync<T>(cancellation) ?? throw new LookupException("Discogs sent an empty response.");
    }

    private AlbumInfo ToAlbumInfo(Release r)
    {
        var entries = FlattenTracks(r.Tracklist).ToList();

        // Positions are "1", "A1" (vinyl sides) or "1-3", "CD2-3" (multi-disc). Number tracks 1–N per disc.
        var discs = entries.Select(e => DiscOf(e.Position)).ToList();
        var multiDisc = discs.Distinct().Count() > 1;
        var counters = new Dictionary<int, int>();
        var tracks = new List<AlbumTrack>();
        for (var i = 0; i < entries.Count; i++)
        {
            var disc = multiDisc ? discs[i] : 1;
            counters[disc] = counters.GetValueOrDefault(disc) + 1;
            tracks.Add(new AlbumTrack(disc, counters[disc], TrackArtist(entries[i], r), entries[i].Title, Durations.TryParse(entries[i].Duration)));
        }

        var label = r.Labels?.FirstOrDefault();
        return new AlbumInfo
        {
            Source = Name,
            Id = r.Id.ToString(),
            Url = r.Uri,
            Artist = ArtistCredit(r.Artists),
            Title = r.Title,
            Year = r.Year,
            Genre = r.Genres?.FirstOrDefault(),
            Label = label is null ? null : WithoutNumber(label.Name),
            CatalogNumber = label?.Catno,
            Country = r.Country,
            Tracks = tracks,
        };
    }

    private static int DiscOf(string position)
    {
        var m = DiscPosition().Match(position.Trim());
        return m.Success ? int.Parse(m.Groups[1].Value) : 1;
    }

    // The track list mixes real tracks with "heading" rows (e.g. "CD 1") and "index" rows that group
    // sub-tracks (medleys). Keep the tracks, and expand index rows into their sub-tracks.
    private static IEnumerable<TrackEntry> FlattenTracks(List<TrackEntry> tracklist)
    {
        foreach (var t in tracklist)
        {
            if (t.Type == "track") yield return t;
            else if (t.Type == "index" && t.SubTracks is { } subs)
                foreach (var s in subs.Where(s => s.Type == "track")) yield return s;
        }
    }

    // Tracks on a compilation carry their own artists; otherwise fall back to the release artist.
    private static string TrackArtist(TrackEntry t, Release r) =>
        t.Artists is { Count: > 0 } a ? ArtistCredit(a) : ArtistCredit(r.Artists);

    // Discogs adds " (2)" etc. to tell artists with the same name apart, and "anv" holds the name
    // as printed on this release. "join" is the text between two artists (",", "&", "Feat." ...).
    private static string ArtistCredit(List<ArtistRef> artists)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < artists.Count; i++)
        {
            var a = artists[i];
            sb.Append(!string.IsNullOrWhiteSpace(a.Anv) ? a.Anv : WithoutNumber(a.Name));
            if (i < artists.Count - 1)
            {
                var join = string.IsNullOrWhiteSpace(a.Join) ? "," : a.Join.Trim();
                sb.Append(join == "," ? ", " : $" {join} ");
            }
        }
        return sb.ToString();
    }

    private static string WithoutNumber(string name) => NumberSuffix().Replace(name, "");

    public void Dispose() => _http.Dispose();

    [GeneratedRegex(@"release/(\d+)|^\[?r?(\d+)\]?$")]
    private static partial Regex ReleaseId();

    [GeneratedRegex(@"master/(\d+)|^\[?m(\d+)\]?$")]
    private static partial Regex MasterId();

    [GeneratedRegex(@" \(\d+\)(?= - |$|,)")]
    private static partial Regex NumberSuffix();

    [GeneratedRegex(@"^(?:CD|Disc\s*)?(\d+)[-.](\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DiscPosition();

    // --- Response shapes (only the fields used) ---

    private sealed record ArtistRef(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("anv")] string? Anv,
        [property: JsonPropertyName("join")] string? Join);

    private sealed record TrackEntry(
        [property: JsonPropertyName("position")] string Position,
        [property: JsonPropertyName("type_")] string Type,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("duration")] string? Duration,
        [property: JsonPropertyName("artists")] List<ArtistRef>? Artists,
        [property: JsonPropertyName("sub_tracks")] List<TrackEntry>? SubTracks);

    private sealed record LabelRef(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("catno")] string? Catno);

    private sealed record Release(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("year")] int Year,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("uri")] string Uri,
        [property: JsonPropertyName("artists")] List<ArtistRef> Artists,
        [property: JsonPropertyName("labels")] List<LabelRef>? Labels,
        [property: JsonPropertyName("genres")] List<string>? Genres,
        [property: JsonPropertyName("tracklist")] List<TrackEntry> Tracklist);

    private sealed record Master([property: JsonPropertyName("main_release")] long MainRelease);

    private sealed record SearchHit(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("year")] string? Year,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("catno")] string? Catno,
        [property: JsonPropertyName("format")] List<string>? Format,
        [property: JsonPropertyName("label")] List<string>? Label);

    private sealed record SearchResult([property: JsonPropertyName("results")] List<SearchHit> Results);
}
