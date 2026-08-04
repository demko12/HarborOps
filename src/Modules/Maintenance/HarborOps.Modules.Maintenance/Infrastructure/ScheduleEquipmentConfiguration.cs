using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class ScheduleEquipmentConfiguration : IEntityTypeConfiguration<ScheduleEquipment>
{
    public void Configure(EntityTypeBuilder<ScheduleEquipment> builder)
    {
        builder.ToTable("ScheduleEquipment", MaintenanceModule.Schema);

        builder.HasKey(scheduleEquipment => new
        {
            scheduleEquipment.ScheduleId,
            scheduleEquipment.EquipmentId,
        });

        builder.Property(scheduleEquipment => scheduleEquipment.ScheduleId)
            .IsRequired();

        builder.Property(scheduleEquipment => scheduleEquipment.EquipmentId)
            .IsRequired();

        builder.HasOne<MaintenanceSchedule>()
            .WithMany()
            .HasForeignKey(scheduleEquipment => scheduleEquipment.ScheduleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Equipment>()
            .WithMany()
            .HasForeignKey(scheduleEquipment => scheduleEquipment.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
