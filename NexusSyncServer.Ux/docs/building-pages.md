# Building pages (NexusSyncServer.Ux)

Adding your own, and replacing the ones that ship.

## The principle

**Modules ship components. A page is a thin default mounting of one.**

That is what gives an operator a choice. `NexusApiKeyManager` is the reusable thing;
`/account/keys` is three lines that mount it. Embed the component in your own interface, or run
the page as delivered — the module does not decide for you.

A module offering only fixed pages would force the first option, which is why `IPortalPageModule`
hands over an assembly and navigation entries rather than rendered pages.

## Adding a page

In a module project (Razor SDK, referencing `NexusSyncServer.Ux`):

```razor
@page "/things"
@namespace Acme.Things.Pages

<PageTitle>Things</PageTitle>

<div class="nx-card">
    <h2>Things</h2>
    <table class="nx-table">…</table>
</div>
```

Then tell the host about the assembly and the navigation entry:

```csharp
public sealed class ThingsModule : IServerModule, IPortalPageModule
{
    public string Id => "acme.things";

    public Assembly ComponentAssembly => typeof(ThingsModule).Assembly;

    public IEnumerable<PortalPage> Pages =>
    [
        new PortalPage("/things", "Things", Order: 20),
    ];

    public void Register(IServiceCollection services, IServerContext context) { }
}
```

`PortalPage` describes the **link**. Routes come from the `@page` attribute, which is why the
assembly is handed over separately — a module whose pages compile but never appear has usually
forgotten `ComponentAssembly`.

## Replacing what ships

Three levels, in increasing order of commitment:

**Restyle.** Override the custom properties in your own stylesheet. Nothing else changes.

**Re-mount.** Build your own pages using `NexusSignIn` and `NexusApiKeyManager`, and leave the
built-in ones unrouted by composing without the module's pages — or simply link to yours and
ignore theirs.

```razor
@page "/welcome"

<div class="nx-card">
    <h1>Welcome to the Acme hub</h1>
    <p>Sign in to get a key for the Acme plugin.</p>
</div>

<NexusSignIn Heading="Get started" />
```

**Replace the shell.** `App.razor` and `ServerLayout.razor` live in the server project, not in
the framework — they are the files an operator is most likely to want, so nothing reaches into
them. Change the document, the layout, or both.

## The landing page

`/` is a component in the server project like any other. Replace it by mounting your own at `/`
and deleting `Home.razor`.

The one that ships answers what someone arriving actually asks — what is this server, which
contracts does it speak, how do I get a key, where is the API — rather than listing links the
header already shows.

## No interactivity, on purpose

Everything here is static server-side rendering with plain form posts. No Blazor circuit, no
SignalR.

The built-in pages list and submit; neither needs interactivity, and not having it means they
work with JavaScript disabled and survive a dropped connection. For the page people use to
recover their access, that is worth more than snappier updates.

A module that genuinely needs interactivity can opt in for its own components — nothing here
prevents it.

## Access control

`NexusAuthorized` and `PortalPage.RequiredScope` both **hide** things. Neither protects
anything: a URL is reachable whether or not something linked to it.

Check in the page:

```razor
@code {
    [CascadingParameter] private HttpContext? Http { get; set; }

    protected override void OnInitialized()
    {
        if (Http?.User.IsInRole("operator") != true)
            Nav.NavigateTo("/", forceLoad: true);
    }
}
```

And remember the API enforces independently, on every request, regardless of what any page
believes.
