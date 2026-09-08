using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Cargo;

internal static class CargoFormFailure
{
    internal static void Apply(
        CargoApiFailure failure,
        EditContext context,
        ValidationMessageStore store,
        IReadOnlyDictionary<string, string> fieldMap,
        Action<string> setGeneral)
    {
        var unmappedMessages = new List<string>();

        foreach (var entry in failure.Errors)
        {
            if (!fieldMap.TryGetValue(entry.Key, out var property))
            {
                unmappedMessages.AddRange(entry.Value);

                continue;
            }

            store.Add(new FieldIdentifier(context.Model, property), entry.Value);
        }

        if (unmappedMessages.Count > 0)
        {
            setGeneral(string.Join(" ", unmappedMessages));
        }
        else if (failure.Errors.Count == 0)
        {
            setGeneral($"{failure.Title} {failure.Message}".Trim());
        }

        context.NotifyValidationStateChanged();
    }
}
