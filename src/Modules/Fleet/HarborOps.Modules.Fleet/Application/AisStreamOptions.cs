namespace HarborOps.Modules.Fleet.Application;

internal sealed class AisStreamOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public double[][][] BoundingBoxes { get; set; } = [[[-90, -180], [90, 180]]];
}
