using Microsoft.JSInterop;

namespace HarborOps.Web.Components;

internal sealed class ThemePreferenceState(IJSRuntime js)
{
    internal const string System = "system";
    internal const string Light = "light";
    internal const string Dark = "dark";

    private bool initialized;
    private string preference = System;

    internal event Action? Changed;

    internal string Preference => preference;

    internal async Task InitializeAsync()
    {
        if (initialized)
        {
            return;
        }

        preference = Normalize(await js.InvokeAsync<string>("themeInterop.getPreference"));
        initialized = true;
        Changed?.Invoke();
    }

    internal async Task SetAsync(string value)
    {
        preference = Normalize(value);
        initialized = true;
        await js.InvokeVoidAsync("themeInterop.set", preference);
        Changed?.Invoke();
    }

    internal async Task ToggleAsync()
    {
        await InitializeAsync();

        var resolved = await js.InvokeAsync<string>("themeInterop.getResolved");
        await SetAsync(string.Equals(resolved, Dark, StringComparison.OrdinalIgnoreCase) ? Light : Dark);
    }

    private static string Normalize(string? value) => value?.ToLowerInvariant() switch
    {
        Light => Light,
        Dark => Dark,
        _ => System,
    };
}
