using Mp3TagUpdater.Services;

namespace Mp3TagUpdater.Tests;

public sealed class FileNameTagsTests
{
    [Theory]
    [InlineData("1-01 Cuarteto del Centenario - Dame la lata", 1u, 1u, "Cuarteto del Centenario", "Dame la lata")]
    [InlineData("2-15 Astor Piazzolla - Adios Nonino - en vivo", 2u, 15u, "Astor Piazzolla", "Adios Nonino - en vivo")]
    [InlineData("02 Kim Larsen - Papirsklip", 0u, 2u, "Kim Larsen", "Papirsklip")]
    [InlineData("02 - Kim Larsen - Papirsklip", 0u, 2u, "Kim Larsen", "Papirsklip")]
    [InlineData("7. Papirsklip", 0u, 7u, null, "Papirsklip")]
    [InlineData("12-Papirsklip", 0u, 12u, null, "Papirsklip")]
    [InlineData("Kim Larsen - Papirsklip", 0u, 0u, "Kim Larsen", "Papirsklip")]
    [InlineData("Papirsklip", 0u, 0u, null, "Papirsklip")]
    [InlineData("1999 - Prince", 0u, 0u, "1999", "Prince")]
    public void Reads_disc_track_artist_and_title(string name, uint disc, uint track, string? artist, string? title) =>
        Assert.Equal(new FileNameInfo(disc, track, artist, title), FileNameTags.Parse(name));

    [Fact]
    public void Plain_number_is_part_of_the_name_unless_the_folder_is_numbered()
    {
        Assert.Equal(new FileNameInfo(0, 0, "50 Cent", "In da Club"), FileNameTags.Parse("50 Cent - In da Club", numberedFolder: false));
        Assert.Equal(new FileNameInfo(0, 50, "Cent", "In da Club"), FileNameTags.Parse("50 Cent - In da Club", numberedFolder: true));
        // A leading zero or a separator makes it a track number either way.
        Assert.Equal(new FileNameInfo(0, 5, "Cent", "In da Club"), FileNameTags.Parse("05 Cent - In da Club", numberedFolder: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_name_says_nothing(string? name) => Assert.Null(FileNameTags.Parse(name));
}
