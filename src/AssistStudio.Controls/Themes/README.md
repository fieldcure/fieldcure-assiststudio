# Themes — ControlTemplate authoring notes

This folder contains `Generic.xaml` and the merged per-control `ResourceDictionary` files
that define the default `ControlTemplate` for every `TemplatedControl` in this library
(ChatPanel, ComposeBar, AttachmentPreviewBar, ToolApprovalPanel, ToolElicitationPanel, ...).

## ⚠️ Do NOT use `x:Uid` inside a `ControlTemplate`

`x:Uid` does not work reliably on elements that live inside a `<ControlTemplate>`:

- **`x:Uid` inside `ControlTemplate` is not resolved against `.resw` at load time.**
  WinUI 3's x:Uid resolver processes markup loaded from pages / user controls, not the
  instantiated template tree. Any `{Uid}.PropertyName` key defined in Resources.resw will
  simply be ignored for these elements.
- **Worse: it can silently erase imperative settings.** If the XAML declares any
  attached property that participates in the same system (e.g.
  `ToolTipService.Placement="Mouse"`), the parser initializes that attached-property
  subsystem for the element. The later `x:Uid` post-processing pass then sees the
  subsystem as "activated" and attempts to resolve `{Uid}.ToolTipService.ToolTip`
  against the resw map. That lookup fails (because `x:Uid` in templates isn't wired up
  correctly), and the resolver writes `null` over whatever the element already had — so
  `ToolTipService.SetToolTip(part, "...")` called from `OnApplyTemplate()` disappears.

Net effect: tooltip not showing, `AutomationProperties.Name` not applied, placeholders
empty — with no error and no log entry.

## What to do instead

1. **`AutomationProperties.AutomationId`** — the only attribute it is safe to declare
   directly on PART elements in XAML. It is a plain string value, does not participate in
   x:Uid lookup, and is required to stay stable (it's used by UI automation tests).
   Keep it in XAML using PascalCase, e.g.
   `AutomationProperties.AutomationId="ChatPanelSearchPrevButton"`.

2. **Everything else localizable (`Tooltip`, `Name`, `HelpText`, `PlaceholderText`,
   `Header`, `Content`, etc.)** — set it from code-behind in `OnApplyTemplate()` after
   `GetTemplateChild("PART_...")`:

   ```csharp
   var btn = GetTemplateChild("PART_SearchPrevButton") as Button;
   if (btn is not null)
   {
       SetBottomRightToolTip(btn, Res.GetString("Chat_SearchPrevTooltip"));
       AutomationHelper.SetAutomation(btn, "ChatPanelSearchPrevButton",
           nameKey: "Chat_SearchPrevName");
   }
   ```

   Use `AutomationHelper.SetAutomation` (in `Helpers/AutomationHelper.cs`) for
   AutomationId + Name + HelpText and `SetBottomRightToolTip` for tooltips — both apply
   the project's `PlacementMode.Mouse` convention and swallow missing-resource errors.

3. **If a localized string needs to be settable by the consumer of the library**, expose
   it as a Dependency Property on the control, and bind it from the template via
   `{TemplateBinding}`. Consumers can then use `x:Uid` on the control instance itself
   (at page scope, where x:Uid does work) to localize per-app.

## Naming conventions for resw keys used by PART elements

- Tooltip string: `<Control>_<Part>Tooltip` — e.g. `Chat_SearchPrevTooltip`
- AutomationProperties.Name: `<Control>_<Part>Name` — e.g. `Chat_SearchPrevName`
- Placeholder / content / other: `<Control>_<Part><Role>` — e.g. `ToolApproval_NotePlaceholder`

Do **not** use the old UWP-style compound keys like
`Chat_SearchPrevButton.ToolTipService.ToolTip` for PART elements — those are the `x:Uid`
mapping convention and only belong in `.resw` if there is a matching `x:Uid="..."` on a
page-scope element. Inside templates they are just dead weight.

## Tooltip placement

Every tooltip in this app uses `PlacementMode.Mouse` (a project-wide UX rule). The
`SetBottomRightToolTip` helper applies it automatically. Do not rely on the XAML
attribute `ToolTipService.Placement="Mouse"` on PART elements — combined with missing
`x:Uid` resolution it can cause the null-overwrite behavior described above.
