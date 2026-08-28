using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class ContainerTests
{
    private static readonly Guid CargoBookingId = Guid.Parse("2c1b0a99-8877-4655-b433-2211ffeeddcc");

    [Theory]
    [InlineData("MSKU1234567", "MSKU1234567")]
    [InlineData("  MSKU1234567  ", "MSKU1234567")]
    [InlineData("msku1234567", "MSKU1234567")]
    [InlineData("\tMsKu1234567\r\n", "MSKU1234567")]
    public void NormalizeNumber_trims_and_upper_cases_the_number(string input, string expected)
    {
        Assert.Equal(expected, Container.NormalizeNumber(input));
    }

    [Fact]
    public void NormalizeNumber_rejects_a_missing_number()
    {
        Assert.Throws<ArgumentNullException>(() => Container.NormalizeNumber(null!));
    }

    [Fact]
    public void Register_keeps_the_vessel_unset_by_default()
    {
        var container = RegisterContainer();

        Assert.NotEqual(Guid.Empty, container.Id);
        Assert.Equal("MSKU1234567", container.ContainerNumber);
        Assert.Equal("40HC", container.ContainerType);
        Assert.Equal(CargoStatus.Registered, container.Status);
        Assert.Equal(CargoBookingId, container.CargoBookingId);
        Assert.Null(container.VesselId);
    }

    [Fact]
    public void ApplyOperationOutcome_settles_a_load_as_loaded()
    {
        var container = RegisterContainer();

        container.ApplyOperationOutcome(CargoOperationType.Load);

        Assert.Equal(CargoStatus.Loaded, container.Status);
    }

    [Fact]
    public void ApplyOperationOutcome_settles_an_unload_as_unloaded()
    {
        var container = RegisterContainer();

        container.ApplyOperationOutcome(CargoOperationType.Unload);

        Assert.Equal(CargoStatus.Unloaded, container.Status);
    }

    [Fact]
    public void ApplyOperationOutcome_can_reverse_an_earlier_outcome()
    {
        var container = RegisterContainer();
        container.ApplyOperationOutcome(CargoOperationType.Load);

        container.ApplyOperationOutcome(CargoOperationType.Unload);

        Assert.Equal(CargoStatus.Unloaded, container.Status);
    }

    private static Container RegisterContainer()
    {
        return Container.Register(
            "MSKU1234567",
            "40HC",
            CargoStatus.Registered,
            cargoBookingId: CargoBookingId);
    }
}
