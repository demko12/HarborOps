using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class BillOfLadingConfiguration : IEntityTypeConfiguration<BillOfLading>
{
    public void Configure(EntityTypeBuilder<BillOfLading> builder)
    {
        builder.ToTable("BillsOfLading", CargoModule.Schema, table => table.IsTemporal());

        builder.HasKey(bill => bill.Id);

        builder.Property(bill => bill.Id)
            .ValueGeneratedNever();

        builder.Property(bill => bill.CargoBookingId)
            .IsRequired();

        builder.Property(bill => bill.BillNumber)
            .HasMaxLength(BillOfLading.BillNumberMaxLength)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(bill => bill.Status)
            .HasConversion<string>()
            .HasMaxLength(BillOfLading.StatusMaxLength)
            .IsRequired();

        builder.Property(bill => bill.ShipperName)
            .HasMaxLength(BillOfLading.PartyNameMaxLength)
            .IsRequired();

        builder.Property(bill => bill.ShipperAddress)
            .HasMaxLength(BillOfLading.PartyAddressMaxLength)
            .IsRequired();

        builder.Property(bill => bill.ConsigneeName)
            .HasMaxLength(BillOfLading.PartyNameMaxLength)
            .IsRequired();

        builder.Property(bill => bill.ConsigneeAddress)
            .HasMaxLength(BillOfLading.PartyAddressMaxLength)
            .IsRequired();

        builder.Property(bill => bill.NotifyPartyName)
            .HasMaxLength(BillOfLading.PartyNameMaxLength);

        builder.Property(bill => bill.NotifyPartyAddress)
            .HasMaxLength(BillOfLading.PartyAddressMaxLength);

        builder.Property(bill => bill.PlaceOfReceipt)
            .HasMaxLength(BillOfLading.LocationMaxLength)
            .IsRequired();

        builder.Property(bill => bill.PortOfLoading)
            .HasMaxLength(BillOfLading.LocationMaxLength)
            .IsRequired();

        builder.Property(bill => bill.PortOfDischarge)
            .HasMaxLength(BillOfLading.LocationMaxLength)
            .IsRequired();

        builder.Property(bill => bill.PlaceOfDelivery)
            .HasMaxLength(BillOfLading.LocationMaxLength)
            .IsRequired();

        builder.Property(bill => bill.FreightTerms)
            .HasConversion<string>()
            .HasMaxLength(BillOfLading.FreightTermsMaxLength)
            .IsRequired();

        builder.Property(bill => bill.CargoMarks)
            .HasMaxLength(BillOfLading.CargoMarksMaxLength)
            .IsRequired();

        builder.Property(bill => bill.CargoDescription)
            .HasMaxLength(BillOfLading.CargoDescriptionMaxLength)
            .IsRequired();

        builder.Property(bill => bill.WeightKg)
            .HasPrecision(BillOfLading.WeightKgPrecision, BillOfLading.WeightKgScale);

        builder.Property(bill => bill.IssuePlace)
            .HasMaxLength(BillOfLading.LocationMaxLength)
            .IsRequired();

        builder.Property(bill => bill.VoidReason)
            .HasMaxLength(BillOfLading.VoidReasonMaxLength);

        builder.Property(bill => bill.SnapshotBookingNumber)
            .HasMaxLength(BillOfLading.BookingNumberMaxLength);

        builder.Property(bill => bill.SnapshotCargoType)
            .HasMaxLength(BillOfLading.CargoTypeMaxLength);

        builder.Property(bill => bill.SnapshotBookingDescription)
            .HasMaxLength(BillOfLading.BookingDescriptionMaxLength);

        builder.Property(bill => bill.SnapshotBookingWeightKg)
            .HasPrecision(BillOfLading.WeightKgPrecision, BillOfLading.WeightKgScale);

        builder.Property(bill => bill.SnapshotBookingStatus)
            .HasConversion<string>()
            .HasMaxLength(BillOfLading.StatusMaxLength);

        builder.Property(bill => bill.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(bill => bill.BillNumber)
            .IsUnique();

        builder.HasIndex(bill => new { bill.CargoBookingId, bill.BillNumber });

        builder.HasOne<CargoBooking>()
            .WithMany()
            .HasForeignKey(bill => bill.CargoBookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
