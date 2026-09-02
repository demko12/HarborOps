using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class CargoSafetyInfoMapper
{
    internal static CargoSafetyInfoDetails ToDetails(CargoSafetyInfo safetyInfo)
    {
        return new CargoSafetyInfoDetails(
            safetyInfo.Id,
            safetyInfo.ImdgClass,
            safetyInfo.UnNumber,
            safetyInfo.StorageConditions,
            safetyInfo.HandlingNotes,
            safetyInfo.ReviewStatus.ToString(),
            safetyInfo.SubmittedByUserId,
            safetyInfo.SubmittedAtUtc,
            safetyInfo.ReviewedByUserId,
            safetyInfo.ReviewedAtUtc,
            safetyInfo.ReviewNotes);
    }
}
