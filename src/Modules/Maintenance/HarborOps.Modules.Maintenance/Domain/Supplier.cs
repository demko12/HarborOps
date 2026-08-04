using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class Supplier
{
    internal const int NameMaxLength = 200;
    internal const int ContactPersonMaxLength = 200;
    internal const int PhoneEmailMaxLength = 200;
    internal const int AddressMaxLength = 300;
    internal const int PaymentTermsMaxLength = 200;

    private Supplier()
    {
    }

    private Supplier(
        Guid id,
        string name,
        string? contactPerson,
        string? phoneEmail,
        string? address,
        string? paymentTerms)
    {
        Id = id;
        Name = name;
        ContactPerson = contactPerson;
        PhoneEmail = phoneEmail;
        Address = address;
        PaymentTerms = paymentTerms;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string? ContactPerson { get; private set; }

    public string? PhoneEmail { get; private set; }

    public string? Address { get; private set; }

    public string? PaymentTerms { get; private set; }

    internal static Supplier Register(
        string name,
        string? contactPerson,
        string? phoneEmail,
        string? address,
        string? paymentTerms)
    {
        return new Supplier(
            Guid.NewGuid(),
            name,
            contactPerson,
            phoneEmail,
            address,
            paymentTerms);
    }
}
