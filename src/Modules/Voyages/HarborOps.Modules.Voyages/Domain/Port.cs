namespace HarborOps.Modules.Voyages.Domain;

internal sealed class Port
{
    internal const int CityMaxLength = 100;
    internal const int CountryMaxLength = 100;
    internal const int NameMaxLength = 200;
    internal const int ShipyardNameMaxLength = 200;
    internal const int TimeZoneMaxLength = 100;
    internal const int UnLocodeLength = 5;

    private Port()
    {
    }

    private Port(
        Guid id,
        string name,
        string country,
        string? city,
        string? unLocode,
        decimal? latitude,
        decimal? longitude,
        string? timeZone,
        string? shipyardName)
    {
        Id = id;
        Name = name;
        Country = country;
        City = city;
        UnLocode = unLocode;
        Latitude = latitude;
        Longitude = longitude;
        TimeZone = timeZone;
        ShipyardName = shipyardName;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string? City { get; private set; }

    public string Country { get; private set; } = null!;

    public string? UnLocode { get; private set; }

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    public string? TimeZone { get; private set; }

    public string? ShipyardName { get; private set; }

    internal static Port Create(
        string name,
        string country,
        string? city = null,
        string? unLocode = null,
        decimal? latitude = null,
        decimal? longitude = null,
        string? timeZone = null,
        string? shipyardName = null)
    {
        return new Port(
            Guid.NewGuid(),
            name,
            country,
            city,
            unLocode,
            latitude,
            longitude,
            timeZone,
            shipyardName);
    }
}
