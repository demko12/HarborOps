using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewMemberConfiguration : IEntityTypeConfiguration<CrewMember>
{
    public void Configure(EntityTypeBuilder<CrewMember> builder)
    {
        builder.ToTable(
            "CrewMembers",
            CrewModule.Schema,
            tableBuilder => tableBuilder.IsTemporal(temporalBuilder =>
            {
                temporalBuilder.HasPeriodStart("ValidFrom");
                temporalBuilder.HasPeriodEnd("ValidTo");
                temporalBuilder.UseHistoryTable("CrewMembersHistory", CrewModule.Schema);
            }));

        builder.HasKey(crewMember => crewMember.Id);

        builder.Property(crewMember => crewMember.Id)
            .ValueGeneratedNever();

        builder.Property(crewMember => crewMember.BaseRankId)
            .IsRequired();

        builder.Property(crewMember => crewMember.Name)
            .HasMaxLength(CrewMember.NameMaxLength)
            .IsRequired();

        builder.Property(crewMember => crewMember.DateOfBirth)
            .IsRequired();

        builder.Property(crewMember => crewMember.Nationality)
            .HasMaxLength(CrewMember.NationalityMaxLength)
            .IsRequired();

        builder.Property(crewMember => crewMember.ImoSeafarerId)
            .HasMaxLength(CrewMember.ImoSeafarerIdMaxLength)
            .IsRequired();

        builder.Property(crewMember => crewMember.PassportNumber)
            .HasMaxLength(CrewMember.PassportNumberMaxLength)
            .IsRequired();

        builder.Property(crewMember => crewMember.PassportExpiry)
            .IsRequired();

        builder.Property(crewMember => crewMember.CdcNumber)
            .HasMaxLength(CrewMember.CdcNumberMaxLength)
            .IsRequired();

        builder.Property(crewMember => crewMember.HireDate)
            .IsRequired();

        builder.Property(crewMember => crewMember.EmploymentStatus)
            .HasMaxLength(CrewMember.EmploymentStatusMaxLength)
            .IsRequired();
        builder.Property(crewMember => crewMember.CreatedUtc)
            .IsRequired();
    }
}
