namespace Mp3Tag.Albums.Lookup;

/// <summary>One search result: enough to tell albums and editions apart before loading one.</summary>
/// <param name="Id">The id to pass to <see cref="IAlbumSource.GetAlbumAsync"/>.</param>
/// <param name="TrackCount">The number of tracks, when the search tells (Spotify does, Discogs doesn't).</param>
/// <param name="Details">Year, country, format, label: whatever the source has.</param>
public sealed record AlbumHit(string Id, string Artist, string Title, int? TrackCount, string Details)
{
    public override string ToString() => $"{Artist} - {Title} ({Details})";
}

/// <summary>A music database that can find albums and list their tracks.</summary>
public interface IAlbumSource : IDisposable
{
    /// <summary>"Spotify" or "Discogs".</summary>
    string Name { get; }

    /// <summary>Why searching by name won't work (missing keys), or null when it will.</summary>
    string? SearchProblem { get; }

    /// <summary>
    /// The most albums <see cref="SearchAsync"/> returns. When a search returns this many, there may be more
    /// that weren't returned: more words in the search, or the album's URL, find them.
    /// </summary>
    int SearchLimit { get; }

    /// <summary>The album id in a pasted URL or id, or null when <paramref name="input"/> is search text.</summary>
    string? ParseId(string input);

    /// <summary>Albums matching <paramref name="text"/>, best first.</summary>
    /// <exception cref="LookupException">The search failed in a way worth showing as it is.</exception>
    Task<IReadOnlyList<AlbumHit>> SearchAsync(string text, CancellationToken cancellation = default);

    /// <summary>The album with its tracks.</summary>
    /// <exception cref="LookupException">The album couldn't be loaded.</exception>
    Task<AlbumInfo> GetAlbumAsync(string id, CancellationToken cancellation = default);
}

/// <summary>
/// The keys for the two sources. Spotify always needs its client id and secret; Discogs only needs a
/// personal access token for searching, as releases can be read by URL or id without one.
/// </summary>
public sealed record ApiKeys
{
    public string? SpotifyClientId { get; init; }
    public string? SpotifyClientSecret { get; init; }

    /// <summary>The personal access token from Discogs developer settings.</summary>
    public string? DiscogsToken { get; init; }
}
