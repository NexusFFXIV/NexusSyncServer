# NexusSyncServer.Ux

The component kit server pages are built from — including the ones that ship in the box.

Plain HTML and one stylesheet. No component library, no build step, no JavaScript. A server
operator should be able to read this, understand it and replace it without learning a front-end
toolchain, and a page that renders without JS keeps working when something in the browser does
not.

## Public API

| Component | File | Purpose |
|---|---|---|
| `NexusPageLayout` | `NexusPageLayout.razor` | The shell: header with navigation, content, footer. Slots for `NavItems`, `HeaderRight` and the content. |
| `NexusAuthorized` | `NexusAuthorized.razor` | Renders content only when the signed-in user carries a scope. |

| Asset | Path |
|---|---|
| Stylesheet | `_content/NexusSyncServer.Ux/nexussyncserver.css` |

## Using the layout

```razor
<NexusPageLayout Brand="My Server">
    <NavItems>
        <NavLink href="/things">Things</NavLink>
    </NavItems>
    <HeaderRight>
        <span>@User?.Identity?.Name</span>
    </HeaderRight>
    <ChildContent>
        @Body
    </ChildContent>
</NexusPageLayout>
```

It deliberately does **not** inherit `LayoutComponentBase`. A layout's content arrives as
`Body`, which a caller cannot supply — and this has to work both as the guts of a layout and as
a plain wrapper inside somebody else's page.

## `NexusAuthorized` decides what to draw, not what is allowed

```razor
<NexusAuthorized User="Http?.User" Scope="observations:push">
    <button class="nx-btn">Submit an observation</button>
</NexusAuthorized>
```

**This is not access control.** It decides what to render; a URL is reachable whether or not
anything linked to it. The page must still check, and the API always does regardless of what
any page thinks.

## Restyling

Override the custom properties. That is the whole extension story:

```css
:root {
    --nx-accent: #c9a227;
    --nx-surface: #1a1512;
    --nx-radius: 3px;
}
```

The full set is at the top of `nexussyncserver.css`: background, surfaces, border, text, muted,
accent, danger, ok, radius, monospace family. Light and dark are handled by
`prefers-color-scheme`.

Replacing the stylesheet entirely means dropping the `<link>` in your own `App.razor` and
shipping your own — nothing in the components depends on a specific rule, only on the class
names.

## Class names

| Class | Where |
|---|---|
| `nx-card` | A bordered section |
| `nx-table` | Data table |
| `nx-btn`, `nx-btn-primary`, `nx-btn-danger` | Buttons and links styled as buttons |
| `nx-field` | Labelled form input |
| `nx-reveal` | The one-time key display — loud on purpose |
| `nx-muted`, `nx-mono`, `nx-warn`, `nx-badge` | Text treatments |

## What is not here yet

The plan calls for `NexusDataTable`, `NexusRecordForm` and `NexusCrudPage` — paging, sorting,
filtering and generated CRUD driven off a contract's field definitions. They are not built.
What exists today is the layout, the scope guard and the stylesheet the built-in pages use.

## Further reading

| Document | What it covers |
|---|---|
| [docs/building-pages.md](docs/building-pages.md) | Adding your own pages, and replacing the built-in ones |

## License

**AGPL-3.0-only.**
