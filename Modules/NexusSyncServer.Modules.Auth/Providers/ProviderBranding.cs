namespace NexusSyncServer.Modules.Auth.Providers;

/// <summary>
/// How a provider's sign-in button should look.
/// <para>Supplied by the provider plugin rather than by the page, because only the plugin
/// knows what it is a button <i>for</i>. A picker that renders every option identically makes
/// people read before they can choose; a recognisable mark and colour is read at a glance,
/// and the point of a sign-in choice is that it is obvious.</para>
/// <para>Nothing here is required. The default is a plain button that inherits the page's own
/// styling, so a provider that has no brand assets — or an operator who would rather everything
/// looked the same — simply does not set it.</para>
/// </summary>
/// <param name="Accent">
/// CSS colour for the button background, e.g. <c>#5865F2</c>. Null keeps the default styling.
/// </param>
/// <param name="OnAccent">Text and icon colour to use on top of <paramref name="Accent"/>.</param>
/// <param name="IconSvg">
/// An inline <c>&lt;svg&gt;</c> element, or null for no icon.
/// <para>Rendered as raw markup, so it must come from the provider assembly and never from
/// configuration or a request. That is safe here because provider plugins are compiled in —
/// there is no runtime plugin loading, deliberately — but it is the reason this is a code
/// property rather than a configurable one.</para>
/// </param>
public sealed record ProviderBranding(string? Accent = null, string? OnAccent = null, string? IconSvg = null)
{
    /// <summary>Inherit the page's own button styling.</summary>
    public static ProviderBranding Default { get; } = new();

    /// <summary>True when this asks for anything other than the page default.</summary>
    public bool HasAccent => !string.IsNullOrWhiteSpace(Accent);
}
