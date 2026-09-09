namespace HarborOps.Modules.Voyages.Contracts;

public sealed record BerthAvailabilityRequest(DateTime? FromUtc, DateTime? ToUtc, decimal? Draft, decimal? Length);
