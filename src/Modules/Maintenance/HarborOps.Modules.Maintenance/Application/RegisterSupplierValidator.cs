using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class RegisterSupplierValidator
{
    internal const string NameField = "name";
    internal const string ContactPersonField = "contactPerson";
    internal const string PhoneEmailField = "phoneEmail";
    internal const string AddressField = "address";
    internal const string PaymentTermsField = "paymentTerms";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterSupplierRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors[NameField] = ["Required."];
        }
        else if (request.Name.Trim().Length > Supplier.NameMaxLength)
        {
            errors[NameField] = [$"Must not exceed {Supplier.NameMaxLength} characters."];
        }

        ValidateOptionalText(request.ContactPerson, ContactPersonField, Supplier.ContactPersonMaxLength, errors);
        ValidateOptionalText(request.PhoneEmail, PhoneEmailField, Supplier.PhoneEmailMaxLength, errors);
        ValidateOptionalText(request.Address, AddressField, Supplier.AddressMaxLength, errors);
        ValidateOptionalText(request.PaymentTerms, PaymentTermsField, Supplier.PaymentTermsMaxLength, errors);

        return errors;
    }

    private static void ValidateOptionalText(
        string? value,
        string field,
        int maxLength,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"Must not exceed {maxLength} characters."];
        }
    }
}
