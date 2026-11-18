namespace Mp3Tag.Albums;

/// <summary>One track as listed by Spotify or Discogs.</summary>
public sealed record AlbumTrack(int Disc, int Number, string Artist, string Title, TimeSpan? Duration);

/// <summary>An album as returned by a music API, in a shape that doesn't depend on the API.</summary>
public sealed record AlbumInfo
{
    /// <summary>"Spotify" or "Discogs".</summary>
    public required string Source { get; init; }
    public required string Id { get; init; }
    public required string Url { get; init; }

    /// <summary>The album artist as credited by the source, e.g. "James Taylor" or "Various".</summary>
    public required string Artist { get; init; }
    public required string Title { get; init; }
    public int Year { get; init; }
    public string? Genre { get; init; }
    public string? Label { get; init; }
    public string? CatalogNumber { get; init; }
    public string? Country { get; init; }
    public required IReadOnlyList<AlbumTrack> Tracks { get; init; }

    /// <summary>
    /// True for "Various" albums, and for albums where most tracks are credited to someone other than
    /// the album artist (Spotify credits Åh Abe! compilations to the series, with each track's own artist).
    /// </summary>
    public bool IsCompilation =>
        Artist.Equals("Various", StringComparison.OrdinalIgnoreCase)
        || Artist.Equals("Various Artists", StringComparison.OrdinalIgnoreCase)
        || Tracks.Count(t => !t.Artist.Contains(Artist, StringComparison.OrdinalIgnoreCase)) * 2 > Tracks.Count;

    /// <summary>The album artist to tag: "Various Artists" on compilations, so players keep them as one album.</summary>
    public string AlbumArtist => IsCompilation ? "Various Artists" : Artist;

    public int DiscCount => Tracks.Count == 0 ? 0 : Tracks.Max(t => t.Disc);

    /// <summary>
    /// The same album with remaster notes removed from the album and track titles:
    /// "Gorilla (2019 Remaster)" → "Gorilla", "Mexico - 2019 Remaster" → "Mexico".
    /// </summary>
    public AlbumInfo WithoutRemasterNotes() => this with
    {
        Title = RemasterNotes.Remove(Title),
        Tracks = Tracks.Select(t => t with { Title = RemasterNotes.Remove(t.Title) }).ToList(),
    };

    /// <summary>How many titles <see cref="WithoutRemasterNotes"/> changed, compared with <paramref name="original"/>.</summary>
    public int TitlesChangedFrom(AlbumInfo original) =>
        (Title != original.Title ? 1 : 0) + Tracks.Zip(original.Tracks).Count(p => p.First.Title != p.Second.Title);

    /// <summary>"1975, Warner Bros. BS 2866, Rock, 10 tracks, compilation": what tells editions apart.</summary>
    public string Details => string.Join(", ", new[]
    {
        Year > 0 ? Year.ToString() : null,
        Country,
        string.Join(" ", new[] { Label, CatalogNumber }.Where(s => !string.IsNullOrWhiteSpace(s))),
        Genre,
        $"{Tracks.Count} tracks",
        IsCompilation ? "compilation" : null,
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Thrown by an album lookup for errors worth showing to the user as they are.</summary>
public sealed class LookupException(string message) : Exception(message);
