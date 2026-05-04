// SPDX-FileCopyrightText: FieldCure
// SPDX-License-Identifier: MIT
//
// Artifact preview JS literals for WebViewChatRenderer.
//
// Moved out of WebViewChatRenderer.cs because they are large blocks of
// JavaScript that change frequently and would otherwise dominate the diff
// of any unrelated edit. The three constants below are interpolated into
// the marked.js configuration script via C# raw-string interpolation
// (`$$"""..."""` with `{{Name}}` placeholders).
//
// - ArtifactPreviewHelpersJs : JSX import map, lib CDN URLs, deps, shadcn
//                              CSS variables / Tailwind config / component
//                              shim, and the transformJsxArtifact /
//                              utf8ToBase64 helpers.
// - HtmlPreviewBranchJs      : Body of the `if (lang === 'html')` branch
//                              inside renderer.code — emits a sandboxed
//                              iframe (Preview) plus an hljs Code view.
// - JsxPreviewBranchJs       : Body of the `if (lang === 'jsx' || tsx)`
//                              branch — same shape as HTML but wraps the
//                              user code in a React + Babel + Tailwind
//                              host shell with a global error catcher.

using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Reflection;

namespace FieldCure.AssistStudio.Controls.Rendering;

internal partial class WebViewChatRenderer
{
    /// <summary>
    /// Virtual host name that serves the bundled JSX runtime libraries
    /// (React, Babel, Tailwind, recharts, etc.) from embedded resources.
    /// The artifact-preview iframe loads everything from this host instead
    /// of unpkg.com / cdn.tailwindcss.com so the feature works air-gapped
    /// (data-sovereignty story for medical / IP customers) and so old
    /// .astx conversations re-render identically against the same locked
    /// versions years later. Served by <see cref="TryServeVendorResource"/>.
    /// </summary>
    internal const string VendorHostName = "assiststudio.vendor";

    /// <summary>
    /// Filename → manifest-resource-name lookup for the JSX vendor bundle.
    /// Built once on first request by scanning the executing assembly's
    /// embedded resources for anything under <c>Resources/vendor/jsx/</c>.
    /// Keyed case-insensitively on the filename only (so requests to
    /// <c>https://assiststudio.vendor/react-18.3.1.development.js</c>
    /// resolve regardless of the assembly's default namespace prefix).
    /// </summary>
    private static readonly Lazy<Dictionary<string, string>> VendorResourceMap = new(() =>
    {
        const string marker = ".vendor.jsx.";
        var assembly = Assembly.GetExecutingAssembly();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var idx = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            map[name[(idx + marker.Length)..]] = name;
        }
        return map;
    });

    /// <summary>
    /// External CDN routes the artifact-preview iframe is allowed to reach.
    /// Vendored libs (see <see cref="VendorHostName"/>) cover the locked 17,
    /// so these only fire for (a) JSX imports the user wrote that aren't in
    /// <c>JSX_LIB_CDN</c> — auto-routed to esm.sh by transformJsxArtifact —
    /// or (b) raw <c>&lt;script src="…"&gt;</c> tags inside HTML artifacts that
    /// hard-code one of these hosts. Every entry is (host, path-prefix);
    /// empty prefix matches any path on the host.
    /// </summary>
    internal static readonly (string Host, string PathPrefix)[] AllowedCdnRoutes =
    [
        // JSX import auto-fallback — modern ESM CDN, Cloudflare-backed.
        // transformJsxArtifact rewrites unmapped imports as dynamic import()
        // against this host; the bootstrap awaits each one before render.
        ("esm.sh", ""),
        // Anthropic's official artifact CDN. Claude.ai itself loads long-tail
        // libraries from here, so HTML artifacts that came from Claude often
        // hard-code cdnjs <script> tags — keep this allowed for compat.
        ("cdnjs.cloudflare.com", "/ajax/libs/"),
        // jsDelivr — the other widely-used npm CDN. Models often pick this
        // for raw <script src=…> in JSX/HTML artifacts when they don't trust
        // the import system to resolve a library (e.g. mammoth, pdf.js).
        ("cdn.jsdelivr.net", "/npm/"),
        // Secondary fallback — esm.sh outage or packages it does not serve
        // (rare, but unpkg has broader long-tail npm coverage).
        ("unpkg.com", ""),
    ];

    /// <summary>
    /// JS helpers (import maps, lib registry, shadcn shim, transforms)
    /// shared by both the HTML and JSX preview branches. Defined inside
    /// the marked configuration IIFE so the renderer.code closure can
    /// reach them.
    /// </summary>
    private const string ArtifactPreviewHelpersJs = """
        // ---- JSX/TSX preview helpers ----
        // Maps ES module imports to their UMD globals exposed by the host shell.
        var JSX_IMPORT_MAP = {
            'react': 'React',
            'react-dom': 'ReactDOM',
            'react-dom/client': 'ReactDOM',
            'recharts': 'Recharts',
            'lucide-react': 'lucideReact',
            'd3': 'd3',
            'three': 'THREE',
            'lodash': '_',
            'mathjs': 'math',
            'papaparse': 'Papa',
            'chart.js': 'Chart',
            'tone': 'Tone',
            'xlsx': 'XLSX',
            'mammoth': 'mammoth',
            '@tensorflow/tfjs': 'tf'
        };

        // Vendored library URLs — served from embedded assembly resources
        // through the assiststudio.vendor virtual host (see TryServeVendorResource
        // in WebViewChatRenderer.ArtifactPreview.cs). React, Babel, and Tailwind
        // are always loaded by the host shell below; everything here is opt-in
        // based on what the user code actually imports. Filenames must match
        // Resources/vendor/jsx/ exactly — no fallback to the network.
        var JSX_LIB_CDN = {
            'lucide-react':      'https://assiststudio.vendor/lucide-react-0.383.0.min.js',
            'recharts':          'https://assiststudio.vendor/recharts-2.12.7.js',
            'd3':                'https://assiststudio.vendor/d3-7.9.0.min.js',
            'three':             'https://assiststudio.vendor/three-0.128.0.min.js',
            'lodash':            'https://assiststudio.vendor/lodash-4.17.21.min.js',
            'mathjs':            'https://assiststudio.vendor/mathjs-13.2.0.js',
            'papaparse':         'https://assiststudio.vendor/papaparse-5.4.1.min.js',
            'chart.js':          'https://assiststudio.vendor/chart-4.4.4.umd.js',
            'tone':              'https://assiststudio.vendor/tone-15.0.4.js',
            // Beyond the spec-100 list — common Claude artifact deps.
            'xlsx':              'https://assiststudio.vendor/xlsx-0.18.5.full.min.js',
            'mammoth':           'https://assiststudio.vendor/mammoth-1.8.0.browser.min.js',
            // tfjs is ~1.4MB. Loaded only when the artifact actually imports it.
            '@tensorflow/tfjs':  'https://assiststudio.vendor/tfjs-4.22.0.min.js'
        };

        // Implicit dependencies — UMD bundles that require other globals
        // beyond React/ReactDOM. Loaded as a side-effect of pulling the
        // primary lib (e.g. recharts inspects window.PropTypes for its
        // internal validators).
        var JSX_LIB_DEPS = {
            'recharts': ['prop-types']
        };
        var JSX_DEP_CDN = {
            'prop-types': 'https://assiststudio.vendor/prop-types-15.8.1.min.js'
        };

        // Some UMD bundles expect React on a differently-cased global
        // (lucide-react@0.383 looks up `window.react` rather than the
        // canonical `window.React`). Run before any lib script.
        //
        // Also expose React hooks (useState, useEffect, useRef, …) as bare
        // globals so artifacts that skip `import { useState } from "react"`
        // — the Anthropic artifact convention — resolve the hooks instead
        // of throwing `ReferenceError: useRef is not defined`. Models
        // trained on Claude's hosted artifacts assume hooks are top-level;
        // without this shim every hook reference in import-less code fails.
        var JSX_PRE_LIB_SHIM_JS =
            'window.react = window.React;' +
            'window.reactDom = window.ReactDOM;' +
            '(function(R){if(!R)return;' +
                'var hooks=["useState","useEffect","useRef","useCallback","useMemo",' +
                    '"useContext","useReducer","useLayoutEffect","useImperativeHandle",' +
                    '"useDebugValue","useId","useTransition","useDeferredValue",' +
                    '"useSyncExternalStore","useInsertionEffect"];' +
                'for(var i=0;i<hooks.length;i++){if(R[hooks[i]])window[hooks[i]]=R[hooks[i]];}' +
                'var utils=["Fragment","createElement","createContext","memo","forwardRef",' +
                    '"lazy","Suspense","StrictMode"];' +
                'for(var j=0;j<utils.length;j++){if(R[utils[j]])window[utils[j]]=R[utils[j]];}' +
            '})(window.React);';

        // After UMD libs initialize, normalize their exported globals to
        // the names our import map expects. lucide-react@0.383 exposes
        // `LucideReact`; older builds expose `lucide`.
        var JSX_POST_LIB_SHIM_JS =
            'window.lucideReact = window.lucideReact || window.LucideReact || window.lucide;' +
            'window.Recharts = window.Recharts || window.recharts;' +
            'window.THREE = window.THREE || window.three;';

        // shadcn/ui CSS variables (HSL triplets) — lets `bg-primary`,
        // `text-muted-foreground`, etc. resolve to actual colors. Light
        // theme on :root, dark via [data-theme="dark"] (matches our
        // tailwind config below).
        var SHADCN_CSS_VARS_CSS =
            ':root{' +
                '--background:0 0% 100%;--foreground:222.2 84% 4.9%;' +
                '--card:0 0% 100%;--card-foreground:222.2 84% 4.9%;' +
                '--popover:0 0% 100%;--popover-foreground:222.2 84% 4.9%;' +
                '--primary:222.2 47.4% 11.2%;--primary-foreground:210 40% 98%;' +
                '--secondary:210 40% 96.1%;--secondary-foreground:222.2 47.4% 11.2%;' +
                '--muted:210 40% 96.1%;--muted-foreground:215.4 16.3% 46.9%;' +
                '--accent:210 40% 96.1%;--accent-foreground:222.2 47.4% 11.2%;' +
                '--destructive:0 84.2% 60.2%;--destructive-foreground:210 40% 98%;' +
                '--border:214.3 31.8% 91.4%;--input:214.3 31.8% 91.4%;--ring:222.2 84% 4.9%;' +
                '--radius:0.5rem;' +
            '}' +
            '[data-theme="dark"]{' +
                '--background:222.2 84% 4.9%;--foreground:210 40% 98%;' +
                '--card:222.2 84% 4.9%;--card-foreground:210 40% 98%;' +
                '--popover:222.2 84% 4.9%;--popover-foreground:210 40% 98%;' +
                '--primary:210 40% 98%;--primary-foreground:222.2 47.4% 11.2%;' +
                '--secondary:217.2 32.6% 17.5%;--secondary-foreground:210 40% 98%;' +
                '--muted:217.2 32.6% 17.5%;--muted-foreground:215 20.2% 65.1%;' +
                '--accent:217.2 32.6% 17.5%;--accent-foreground:210 40% 98%;' +
                '--destructive:0 62.8% 30.6%;--destructive-foreground:210 40% 98%;' +
                '--border:217.2 32.6% 17.5%;--input:217.2 32.6% 17.5%;--ring:212.7 26.8% 83.9%;' +
            '}';

        // Tailwind config that wires the shadcn HSL vars into utility
        // classes (`bg-primary`, `text-muted-foreground`, …). Set via
        // the global `tailwind.config` AFTER the CDN tailwind script
        // loads and BEFORE the user code paints anything.
        var SHADCN_TAILWIND_CONFIG_JS =
            'if(window.tailwind){tailwind.config={' +
                'darkMode:["class","[data-theme=\\"dark\\"]"],' +
                'theme:{extend:{' +
                    'colors:{' +
                        'border:"hsl(var(--border))",input:"hsl(var(--input))",ring:"hsl(var(--ring))",' +
                        'background:"hsl(var(--background))",foreground:"hsl(var(--foreground))",' +
                        'primary:{DEFAULT:"hsl(var(--primary))",foreground:"hsl(var(--primary-foreground))"},' +
                        'secondary:{DEFAULT:"hsl(var(--secondary))",foreground:"hsl(var(--secondary-foreground))"},' +
                        'destructive:{DEFAULT:"hsl(var(--destructive))",foreground:"hsl(var(--destructive-foreground))"},' +
                        'muted:{DEFAULT:"hsl(var(--muted))",foreground:"hsl(var(--muted-foreground))"},' +
                        'accent:{DEFAULT:"hsl(var(--accent))",foreground:"hsl(var(--accent-foreground))"},' +
                        'popover:{DEFAULT:"hsl(var(--popover))",foreground:"hsl(var(--popover-foreground))"},' +
                        'card:{DEFAULT:"hsl(var(--card))",foreground:"hsl(var(--card-foreground))"}' +
                    '},' +
                    'borderRadius:{lg:"var(--radius)",md:"calc(var(--radius) - 2px)",sm:"calc(var(--radius) - 4px)"}' +
                '}}' +
            '};}';

        // shadcn/ui stub: exposes the ~25 most common components on
        // `window.__shadcn`. Visual fidelity over behavioral fidelity —
        // composed primitives render as plain Tailwind-styled divs;
        // controlled inputs (Switch/Checkbox/Tabs/Accordion) carry real
        // useState. Stubs for Dialog/Popover/Tooltip/Select fall back
        // to inline rendering of their children so layout is preserved
        // even though the modal/floating behavior is missing.
        var SHADCN_SHIM_JS =
            "(function(R){" +
            "if(!R){return;}" +
            "var h=R.createElement,uS=R.useState,uId=R.useId||function(){return 'id-'+Math.random().toString(36).slice(2,9);};" +
            "function cn(){var a=arguments,p=[],i;for(i=0;i<a.length;i++){if(a[i]&&typeof a[i]==='string')p.push(a[i]);}return p.join(' ');}" +
            "function pass(props){var o={},k;for(k in props){if(k!=='children'&&k!=='className'&&k!=='variant'&&k!=='size'&&k!=='asChild')o[k]=props[k];}return o;}" +
            // ---- primitives ----
            "var Button=function(p){p=p||{};var v=p.variant||'default',s=p.size||'default';" +
                "var vc={'default':'bg-primary text-primary-foreground hover:opacity-90','destructive':'bg-destructive text-destructive-foreground hover:opacity-90','outline':'border border-input bg-background hover:bg-accent hover:text-accent-foreground','secondary':'bg-secondary text-secondary-foreground hover:opacity-80','ghost':'hover:bg-accent hover:text-accent-foreground','link':'text-primary underline-offset-4 hover:underline'};" +
                "var sc={'default':'h-10 px-4 py-2','sm':'h-9 px-3','lg':'h-11 px-8','icon':'h-10 w-10'};" +
                "return h('button',Object.assign({className:cn('inline-flex items-center justify-center gap-2 rounded-md text-sm font-medium transition-colors disabled:pointer-events-none disabled:opacity-50',vc[v]||vc['default'],sc[s]||sc['default'],p.className)},pass(p)),p.children);};" +
            "var Card=function(p){p=p||{};return h('div',Object.assign({className:cn('rounded-lg border bg-card text-card-foreground shadow-sm',p.className)},pass(p)),p.children);};" +
            "var CardHeader=function(p){p=p||{};return h('div',Object.assign({className:cn('flex flex-col space-y-1.5 p-6',p.className)},pass(p)),p.children);};" +
            "var CardTitle=function(p){p=p||{};return h('h3',Object.assign({className:cn('text-2xl font-semibold leading-none tracking-tight',p.className)},pass(p)),p.children);};" +
            "var CardDescription=function(p){p=p||{};return h('p',Object.assign({className:cn('text-sm text-muted-foreground',p.className)},pass(p)),p.children);};" +
            "var CardContent=function(p){p=p||{};return h('div',Object.assign({className:cn('p-6 pt-0',p.className)},pass(p)),p.children);};" +
            "var CardFooter=function(p){p=p||{};return h('div',Object.assign({className:cn('flex items-center p-6 pt-0',p.className)},pass(p)),p.children);};" +
            "var Input=function(p){p=p||{};return h('input',Object.assign({className:cn('flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring disabled:opacity-50',p.className)},pass(p)));};" +
            "var Label=function(p){p=p||{};return h('label',Object.assign({className:cn('text-sm font-medium leading-none peer-disabled:cursor-not-allowed peer-disabled:opacity-70',p.className)},pass(p)),p.children);};" +
            "var Textarea=function(p){p=p||{};return h('textarea',Object.assign({className:cn('flex min-h-[80px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring disabled:opacity-50',p.className)},pass(p)));};" +
            "var Badge=function(p){p=p||{};var v=p.variant||'default',vc={'default':'bg-primary text-primary-foreground','secondary':'bg-secondary text-secondary-foreground','destructive':'bg-destructive text-destructive-foreground','outline':'text-foreground border'};return h('div',Object.assign({className:cn('inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold',vc[v]||vc['default'],p.className)},pass(p)),p.children);};" +
            "var Alert=function(p){p=p||{};var v=p.variant||'default',vc={'default':'bg-background text-foreground','destructive':'border-destructive/50 text-destructive [&>svg]:text-destructive'};return h('div',Object.assign({role:'alert',className:cn('relative w-full rounded-lg border p-4 [&>svg]:absolute [&>svg]:left-4 [&>svg]:top-4 [&>svg+div]:translate-y-[-3px] [&:has(svg)]:pl-11',vc[v]||vc['default'],p.className)},pass(p)),p.children);};" +
            "var AlertTitle=function(p){p=p||{};return h('h5',Object.assign({className:cn('mb-1 font-medium leading-none tracking-tight',p.className)},pass(p)),p.children);};" +
            "var AlertDescription=function(p){p=p||{};return h('div',Object.assign({className:cn('text-sm [&_p]:leading-relaxed',p.className)},pass(p)),p.children);};" +
            "var Avatar=function(p){p=p||{};return h('span',Object.assign({className:cn('relative flex h-10 w-10 shrink-0 overflow-hidden rounded-full bg-muted',p.className)},pass(p)),p.children);};" +
            "var AvatarImage=function(p){p=p||{};return h('img',Object.assign({className:cn('aspect-square h-full w-full',p.className)},pass(p)));};" +
            "var AvatarFallback=function(p){p=p||{};return h('span',Object.assign({className:cn('flex h-full w-full items-center justify-center bg-muted text-sm font-medium',p.className)},pass(p)),p.children);};" +
            "var Separator=function(p){p=p||{};var o=p.orientation||'horizontal';return h('div',Object.assign({className:cn('shrink-0 bg-border',o==='horizontal'?'h-px w-full':'h-full w-px',p.className)},pass(p)));};" +
            "var Progress=function(p){p=p||{};var v=Math.max(0,Math.min(100,p.value||0));return h('div',Object.assign({className:cn('relative h-4 w-full overflow-hidden rounded-full bg-secondary',p.className)},pass(p)),h('div',{className:'h-full bg-primary transition-all',style:{width:v+'%'}}));};" +
            "var Skeleton=function(p){p=p||{};return h('div',Object.assign({className:cn('animate-pulse rounded-md bg-muted',p.className)},pass(p)));};" +
            // ---- controlled primitives ----
            "var Switch=function(p){p=p||{};var ctrl=p.checked!==undefined,sa=uS(!!p.defaultChecked),checked=ctrl?p.checked:sa[0];" +
                "function toggle(){var nv=!checked;if(!ctrl)sa[1](nv);if(p.onCheckedChange)p.onCheckedChange(nv);}" +
                "return h('button',{type:'button',role:'switch','aria-checked':checked,onClick:toggle,className:cn('relative inline-flex h-6 w-11 shrink-0 cursor-pointer items-center rounded-full transition-colors',checked?'bg-primary':'bg-input',p.className)}," +
                    "h('span',{className:cn('pointer-events-none block h-5 w-5 rounded-full bg-background shadow-lg ring-0 transition-transform',checked?'translate-x-5':'translate-x-0.5')}));};" +
            "var Checkbox=function(p){p=p||{};var ctrl=p.checked!==undefined,sa=uS(!!p.defaultChecked),checked=ctrl?p.checked:sa[0];" +
                "function toggle(){var nv=!checked;if(!ctrl)sa[1](nv);if(p.onCheckedChange)p.onCheckedChange(nv);}" +
                "return h('button',{type:'button',role:'checkbox','aria-checked':checked,onClick:toggle,className:cn('h-4 w-4 shrink-0 rounded-sm border border-primary inline-flex items-center justify-center',checked?'bg-primary text-primary-foreground':'bg-background',p.className)}," +
                    "checked?h('span',{className:'text-xs leading-none'},'\\u2714'):null);};" +
            // ---- Tabs (context via React.createContext) ----
            "var TabsCtx=R.createContext({value:null,setValue:function(){}});" +
            "var Tabs=function(p){p=p||{};var ctrl=p.value!==undefined,sa=uS(p.defaultValue),value=ctrl?p.value:sa[0];" +
                "function setValue(nv){if(!ctrl)sa[1](nv);if(p.onValueChange)p.onValueChange(nv);}" +
                "return h(TabsCtx.Provider,{value:{value:value,setValue:setValue}},h('div',{className:cn(p.className)},p.children));};" +
            "var TabsList=function(p){p=p||{};return h('div',Object.assign({className:cn('inline-flex h-10 items-center justify-center rounded-md bg-muted p-1 text-muted-foreground',p.className)},pass(p)),p.children);};" +
            "var TabsTrigger=function(p){p=p||{};var ctx=R.useContext(TabsCtx),active=ctx.value===p.value;" +
                "return h('button',{type:'button',onClick:function(){ctx.setValue(p.value);},className:cn('inline-flex items-center justify-center whitespace-nowrap rounded-sm px-3 py-1.5 text-sm font-medium transition-all',active?'bg-background text-foreground shadow-sm':'',p.className)},p.children);};" +
            "var TabsContent=function(p){p=p||{};var ctx=R.useContext(TabsCtx);if(ctx.value!==p.value)return null;return h('div',Object.assign({className:cn('mt-2',p.className)},pass(p)),p.children);};" +
            // ---- minimal stubs (graceful degradation, no portal/positioning) ----
            "var inlineDiv=function(cls){return function(p){p=p||{};return h('div',Object.assign({className:cn(cls||'',p.className)},pass(p)),p.children);};};" +
            "var Dialog=inlineDiv(),DialogTrigger=inlineDiv(),DialogContent=inlineDiv('rounded-lg border bg-background p-6 shadow-lg'),DialogHeader=inlineDiv('flex flex-col space-y-1.5 text-center sm:text-left'),DialogFooter=inlineDiv('flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-2'),DialogTitle=function(p){p=p||{};return h('h2',Object.assign({className:cn('text-lg font-semibold leading-none tracking-tight',p.className)},pass(p)),p.children);},DialogDescription=function(p){p=p||{};return h('p',Object.assign({className:cn('text-sm text-muted-foreground',p.className)},pass(p)),p.children);};" +
            "var Popover=inlineDiv(),PopoverTrigger=inlineDiv(),PopoverContent=inlineDiv('rounded-md border bg-popover p-4 text-popover-foreground shadow-md');" +
            "var Tooltip=inlineDiv(),TooltipProvider=inlineDiv(),TooltipTrigger=inlineDiv(),TooltipContent=inlineDiv('rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground');" +
            "var Select=inlineDiv(),SelectTrigger=function(p){p=p||{};return h('button',Object.assign({type:'button',className:cn('flex h-10 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm',p.className)},pass(p)),p.children);},SelectValue=function(p){p=p||{};return h('span',Object.assign({className:p.className},pass(p)),p.placeholder||p.children);},SelectContent=inlineDiv('rounded-md border bg-popover p-1'),SelectItem=function(p){p=p||{};return h('div',Object.assign({className:cn('relative flex cursor-default select-none items-center rounded-sm py-1.5 px-2 text-sm hover:bg-accent',p.className)},pass(p)),p.children);};" +
            "var DropdownMenu=inlineDiv(),DropdownMenuTrigger=inlineDiv(),DropdownMenuContent=inlineDiv('rounded-md border bg-popover p-1 shadow-md'),DropdownMenuItem=SelectItem,DropdownMenuSeparator=function(p){p=p||{};return h('div',Object.assign({className:cn('-mx-1 my-1 h-px bg-muted',p.className)},pass(p)));},DropdownMenuLabel=function(p){p=p||{};return h('div',Object.assign({className:cn('px-2 py-1.5 text-sm font-semibold',p.className)},pass(p)),p.children);};" +
            "var Sheet=inlineDiv(),SheetTrigger=inlineDiv(),SheetContent=inlineDiv('rounded-lg border bg-background p-6 shadow-lg'),SheetHeader=DialogHeader,SheetFooter=DialogFooter,SheetTitle=DialogTitle,SheetDescription=DialogDescription;" +
            "var Accordion=inlineDiv(),AccordionItem=inlineDiv('border-b'),AccordionTrigger=function(p){p=p||{};return h('button',Object.assign({type:'button',className:cn('flex flex-1 items-center justify-between py-4 font-medium hover:underline',p.className)},pass(p)),p.children);},AccordionContent=inlineDiv('pb-4 pt-0 text-sm');" +
            // ---- expose ----
            "window.__shadcn=Object.assign(window.__shadcn||{},{" +
                "cn:cn,Button:Button," +
                "Card:Card,CardHeader:CardHeader,CardTitle:CardTitle,CardDescription:CardDescription,CardContent:CardContent,CardFooter:CardFooter," +
                "Input:Input,Label:Label,Textarea:Textarea," +
                "Badge:Badge,Alert:Alert,AlertTitle:AlertTitle,AlertDescription:AlertDescription," +
                "Avatar:Avatar,AvatarImage:AvatarImage,AvatarFallback:AvatarFallback," +
                "Separator:Separator,Progress:Progress,Skeleton:Skeleton," +
                "Switch:Switch,Checkbox:Checkbox," +
                "Tabs:Tabs,TabsList:TabsList,TabsTrigger:TabsTrigger,TabsContent:TabsContent," +
                "Dialog:Dialog,DialogTrigger:DialogTrigger,DialogContent:DialogContent,DialogHeader:DialogHeader,DialogFooter:DialogFooter,DialogTitle:DialogTitle,DialogDescription:DialogDescription," +
                "Popover:Popover,PopoverTrigger:PopoverTrigger,PopoverContent:PopoverContent," +
                "Tooltip:Tooltip,TooltipProvider:TooltipProvider,TooltipTrigger:TooltipTrigger,TooltipContent:TooltipContent," +
                "Select:Select,SelectTrigger:SelectTrigger,SelectValue:SelectValue,SelectContent:SelectContent,SelectItem:SelectItem," +
                "DropdownMenu:DropdownMenu,DropdownMenuTrigger:DropdownMenuTrigger,DropdownMenuContent:DropdownMenuContent,DropdownMenuItem:DropdownMenuItem,DropdownMenuSeparator:DropdownMenuSeparator,DropdownMenuLabel:DropdownMenuLabel," +
                "Sheet:Sheet,SheetTrigger:SheetTrigger,SheetContent:SheetContent,SheetHeader:SheetHeader,SheetFooter:SheetFooter,SheetTitle:SheetTitle,SheetDescription:SheetDescription," +
                "Accordion:Accordion,AccordionItem:AccordionItem,AccordionTrigger:AccordionTrigger,AccordionContent:AccordionContent" +
            "});" +
            "})(window.React);";

        /// Rewrites `import` and `export default` so user code runs under
        /// Babel-standalone (no ES module support) against the UMD globals
        /// the host shell loads — either from the embedded vendor bundle
        /// (sync `<script>` tags) or from esm.sh (async dynamic `import()`
        /// in the bootstrap). Returns { code, libs, externals } where
        ///   - `libs`      = vendored sources to load via `<script>` tags
        ///   - `externals` = unmapped sources to dynamic-import from esm.sh
        ///                   before user code runs. Each entry is
        ///                   { name, url, global, source }.
        /// Imports beginning with `@/` resolve to the inlined shadcn shim.
        function transformJsxArtifact(src) {
            // Sanitize a module specifier to a valid JS identifier so it
            // can be used as a window-global key for the async-loaded copy.
            // 'plotly' → '__ext_plotly', 'd3-cloud' → '__ext_d3_cloud',
            // '@scope/pkg' → '__ext__scope_pkg'.
            function makeExternalGlobal(mod) {
                return '__ext_' + mod.replace(/[^a-zA-Z0-9]/g, '_');
            }

            function mapMod(mod) {
                // shadcn convention: any '@/components/ui/...' or
                // '@/lib/utils' resolves to the inlined window.__shadcn.
                if (mod.indexOf('@/') === 0) return 'window.__shadcn';
                if (JSX_IMPORT_MAP[mod]) return JSX_IMPORT_MAP[mod];
                // Auto-fallback: bootstrap will dynamic-import this module
                // from esm.sh and stash it under window[__ext_<mod>] before
                // user code runs.
                return makeExternalGlobal(mod);
            }
            // For default/namespace imports we must read the global as a
            // property (window.X), not a binding. Otherwise patterns
            // like `import * as d3 from "d3"` become `const d3 = d3;`
            // and TDZ-fail because the RHS resolves to the const being
            // declared. Dotted paths (e.g. window.__shadcn) are
            // already property access, so leave as-is.
            function asProperty(g) {
                return g.indexOf('.') >= 0 ? g : 'window.' + g;
            }

            // First pass: classify every imported module.
            //   - Mapped to a host-shell global (JSX_IMPORT_MAP) → no script
            //     needed (React, ReactDOM, etc. are loaded by the host shell
            //     unconditionally; libs in JSX_LIB_CDN add a <script> tag).
            //   - shadcn (@/...)              → no script needed
            //   - Anything else               → externals[] (esm.sh dynamic import)
            // Order matters: must check JSX_IMPORT_MAP before falling through
            // to externals, otherwise `import {useState} from 'react'` gets
            // routed to esm.sh even though React is already on `window`.
            var libsSeen = {};
            var externalsSeen = {};
            var importScanRe = /^[ \t]*import\s+(?:[^'"]+?\s+from\s+)?['"]([^'"]+)['"];?/gm;
            var scan;
            while ((scan = importScanRe.exec(src)) !== null) {
                var mod = scan[1];
                if (mod.indexOf('@/') === 0) continue;
                if (JSX_LIB_CDN[mod]) {
                    libsSeen[mod] = true;
                } else if (JSX_IMPORT_MAP[mod]) {
                    // Already on window via the host shell — no fetch needed.
                } else if (!externalsSeen[mod]) {
                    externalsSeen[mod] = {
                        name: mod,
                        url: 'https://esm.sh/' + mod,
                        global: makeExternalGlobal(mod),
                        source: 'esm.sh'
                    };
                }
            }
            var libs = Object.keys(libsSeen);
            var externals = Object.keys(externalsSeen).map(function(k){return externalsSeen[k];});

            // Helper: split a "{ a, b as c }" clause into a destructuring body
            // ("a, b: c"). Reused across the named-only and default+named forms.
            function destructureNames(names) {
                return names.split(',').map(function(p) {
                    p = p.trim();
                    var asMatch = p.match(/^(\w+)\s+as\s+(\w+)$/);
                    return asMatch ? asMatch[1] + ': ' + asMatch[2] : p;
                }).join(', ');
            }

            // import X, { a, b as c } from "mod";  — default + named combined
            // (must run before the named-only rewriter so the leading default
            // identifier isn't left on the line). React's UMD bundle has no
            // `.default`, so default-binding falls back to the namespace itself.
            src = src.replace(
                /^[ \t]*import\s+(\w+)\s*,\s*\{\s*([^}]+?)\s*\}\s*from\s*['"]([^'"]+)['"];?[ \t]*$/gm,
                function(_m, defName, names, mod) {
                    var g = mapMod(mod);
                    var p = asProperty(g);
                    return 'const ' + defName + ' = ' + p + '.default || ' + p + '; ' +
                           'const { ' + destructureNames(names) + ' } = ' + p + ';';
                });

            // import X, * as N from "mod";  — default + namespace combined
            src = src.replace(
                /^[ \t]*import\s+(\w+)\s*,\s*\*\s*as\s+(\w+)\s+from\s*['"]([^'"]+)['"];?[ \t]*$/gm,
                function(_m, defName, nsName, mod) {
                    var g = mapMod(mod);
                    var p = asProperty(g);
                    return 'const ' + defName + ' = ' + p + '.default || ' + p + '; ' +
                           'const ' + nsName + ' = ' + p + ';';
                });

            // import { a, b as c } from "mod";
            src = src.replace(
                /^[ \t]*import\s*\{\s*([^}]+?)\s*\}\s*from\s*['"]([^'"]+)['"];?[ \t]*$/gm,
                function(_m, names, mod) {
                    var g = mapMod(mod);
                    return 'const { ' + destructureNames(names) + ' } = ' + asProperty(g) + ';';
                });

            // import * as X from "mod";
            src = src.replace(
                /^[ \t]*import\s*\*\s*as\s+(\w+)\s+from\s*['"]([^'"]+)['"];?[ \t]*$/gm,
                function(_m, name, mod) {
                    var g = mapMod(mod);
                    return 'const ' + name + ' = ' + asProperty(g) + ';';
                });

            // import X from "mod";
            src = src.replace(
                /^[ \t]*import\s+(\w+)\s+from\s*['"]([^'"]+)['"];?[ \t]*$/gm,
                function(_m, name, mod) {
                    var g = mapMod(mod);
                    var p = asProperty(g);
                    return 'const ' + name + ' = ' + p + '.default || ' + p + ';';
                });

            // import "mod";  (side-effect only — drop)
            src = src.replace(/^[ \t]*import\s+['"][^'"]+['"];?[ \t]*$/gm, '');

            // export default function NAMED  →  named function expression on window
            src = src.replace(/^export\s+default\s+function(\s+\w+)?/gm,
                'window.__default_export = function$1');
            // export default class NAMED  →  named class expression on window
            src = src.replace(/^export\s+default\s+class(\s+\w+)?/gm,
                'window.__default_export = class$1');
            // export default IDENTIFIER;
            src = src.replace(/^export\s+default\s+(\w+)\s*;?[ \t]*$/gm,
                'window.__default_export = $1;');
            // export default <expr>  (arrow funcs, JSX literals, etc.)
            src = src.replace(/^export\s+default\s+/gm, 'window.__default_export = ');
            // export const / export function / etc.  →  drop the keyword
            src = src.replace(/^export\s+(?!default)/gm, '');

            return { code: src, libs: libs, externals: externals };
        }

        /// UTF-8 safe base64 encode for stashing original JSX source on a
        /// data attribute (so the Copy button returns the verbatim source,
        /// not the host-shell-wrapped iframe document).
        function utf8ToBase64(str) {
            return btoa(unescape(encodeURIComponent(str)));
        }
        """;

    /// <summary>
    /// Body of the <c>if (lang === 'html')</c> branch inside renderer.code.
    /// Renders the user's HTML inside a sandboxed iframe (Preview) and an
    /// hljs-highlighted source pane (Code). Relies on previewHeader,
    /// the diagram-block CSS, and the wireDiagramBlock JS in chat.html.
    /// </summary>
    private const string HtmlPreviewBranchJs = """
        if (lang === 'html') {
            var L = window._L || {};
            var attrEscaped = code
                .replace(/&/g, '&amp;')
                .replace(/"/g, '&quot;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;');
            var htmlHl;
            try { htmlHl = hljs.highlight(code, { language: 'html' }).value; }
            catch(e) { htmlHl = code.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
            return '<div class="diagram-block" data-kind="html" data-view="preview">' +
                    previewHeader('html', L.htmlPreviewCopyTooltip || 'Copy HTML source') +
                    '<div class="html-frame">' +
                        // sandbox tokens:
                        //   allow-scripts   — needed for any JS at all
                        //   allow-modals    — <input type="file">, alert/confirm/prompt
                        //                     (a missing modal token caused the WebView2
                        //                     process to SEH-crash on first file picker)
                        //   allow-downloads — blob: download anchors for exporters
                        // Crucially we do NOT add allow-same-origin — the iframe stays on
                        // a null origin so it cannot reach parent DOM, cookies, or storage.
                        '<iframe sandbox="allow-scripts allow-modals allow-downloads" srcdoc="' + attrEscaped + '"></iframe>' +
                    '</div>' +
                    '<pre class="code-view"><code class="hljs language-html">' + htmlHl + '</code></pre>' +
                   '</div>';
        }
        """;

    /// <summary>
    /// Body of the <c>if (lang === 'jsx' || lang === 'tsx')</c> branch.
    /// Produces a host shell that loads React, Babel, Tailwind, the
    /// shadcn shim, and any imported CDN libs, then runs the user code
    /// programmatically through Babel.transform with full error capture.
    /// Pairs with ArtifactPreviewHelpersJs for transformJsxArtifact /
    /// utf8ToBase64 / shim constants.
    /// </summary>
    private const string JsxPreviewBranchJs = """
        if (lang === 'jsx' || lang === 'tsx') {
            var L2 = window._L || {};
            var jsxResult = transformJsxArtifact(code);
            var transformedB64 = utf8ToBase64(jsxResult.code);
            var presets = (lang === 'tsx') ? 'react,typescript' : 'react';
            // Order: each lib's deps first, then the lib itself.
            // De-dupe so two libs sharing a dep don't double-load.
            var loadedScripts = {};
            var libScriptList = [];
            jsxResult.libs.forEach(function(lib) {
                (JSX_LIB_DEPS[lib] || []).forEach(function(dep) {
                    var depUrl = JSX_DEP_CDN[dep];
                    if (depUrl && !loadedScripts[depUrl]) {
                        loadedScripts[depUrl] = true;
                        libScriptList.push('<script src="' + depUrl + '" crossorigin></script>');
                    }
                });
                var url = JSX_LIB_CDN[lib];
                if (url && !loadedScripts[url]) {
                    loadedScripts[url] = true;
                    libScriptList.push('<script src="' + url + '" crossorigin></script>');
                }
            });
            var libScripts = libScriptList.join('');

            // Installed first so it can capture script load failures
            // (script.onerror bubbles via capture phase) and any
            // runtime/promise errors, including ones in CDN libs.
            var errorInfraJs =
                '(function(){' +
                    'var buf=[];' +
                    'window.__showPreviewError=function(m){' +
                        'if(!document.body){buf.push(m);return;}' +
                        'var d=document.getElementById("__preview_error");' +
                        'if(!d){d=document.createElement("div");d.id="__preview_error";' +
                            'var r=document.getElementById("root");' +
                            'document.body.insertBefore(d,r||document.body.firstChild);}' +
                        'd.appendChild(document.createTextNode(m+"\\n"));' +
                    '};' +
                    'window.addEventListener("error",function(e){' +
                        // Resource-load errors: report failed scripts/stylesheets only.
                        // Images, media, fonts are best-effort — a broken-image icon is
                        // a clearer signal than a generic "Error" banner.
                        'if(e.target&&(e.target.tagName==="SCRIPT"||e.target.tagName==="LINK"))' +
                            'window.__showPreviewError("Failed to load: "+(e.target.src||e.target.href));' +
                        // Runtime errors carry e.message; resource errors do not. Skip the
                        // empty case so a missing <img> never produces a content-less banner.
                        'else if(e.message)' +
                            'window.__showPreviewError(e.message+(e.filename?" @ "+e.filename+":"+e.lineno:""));' +
                    '},true);' +
                    'window.addEventListener("unhandledrejection",function(e){' +
                        'var r=e.reason;window.__showPreviewError("Unhandled promise rejection: "+(r&&r.message||r));' +
                    '});' +
                    // Per-artifact theme override: parent posts {type:"setTheme",theme:"light"|"dark"}
                    // when the user clicks the sun/moon button. Flip <html data-theme> so the shadcn
                    // CSS vars + Tailwind darkMode selector both update; also patch color-scheme and
                    // body background so default UA surfaces (scrollbar, form controls) follow.
                    'window.addEventListener("message",function(e){' +
                        'if(!e.data||e.data.type!=="setTheme")return;' +
                        'var t=e.data.theme==="dark"?"dark":"light";' +
                        'document.documentElement.setAttribute("data-theme",t);' +
                        'document.documentElement.style.colorScheme=t;' +
                        'if(document.body)document.body.style.background=t==="dark"?"#1e1e1e":"#ffffff";' +
                    '});' +
                    'document.addEventListener("DOMContentLoaded",function(){' +
                        'while(buf.length)window.__showPreviewError(buf.shift());' +
                    '});' +
                '})();';

            // Encode the externals manifest so the iframe bootstrap can read it
            // back as JSON. Empty array → "[]" (no dynamic imports happen).
            var externalsJson = JSON.stringify(jsxResult.externals || []);
            var externalsB64 = utf8ToBase64(externalsJson);

            // Bootstrap is async because each unmapped JSX import becomes a
            // dynamic import() against esm.sh. We must await all of those
            // before Babel.transform / Function() / render, otherwise the
            // user code references window.__ext_<mod> globals that aren't
            // populated yet. Vendored libs (React, recharts, …) are still
            // loaded synchronously via <script> tags above this script, so
            // they're already on `window` when this IIFE starts.
            var bootstrapJs =
                'document.addEventListener("DOMContentLoaded",async function(){' +
                    'var b64=document.documentElement.getAttribute("data-jsx-source");' +
                    'var extB64=document.documentElement.getAttribute("data-jsx-externals")||"";' +
                    'var presets=(document.documentElement.getAttribute("data-jsx-presets")||"react").split(",");' +
                    'var src,externals;' +
                    'try{src=decodeURIComponent(escape(atob(b64)));}' +
                    'catch(e){window.__showPreviewError("Failed to decode source: "+e.message);return;}' +
                    'try{externals=extB64?JSON.parse(decodeURIComponent(escape(atob(extB64)))):[];}' +
                    'catch(e){window.__showPreviewError("Failed to decode externals: "+e.message);return;}' +
                    'if(typeof Babel==="undefined"){window.__showPreviewError("Babel did not load from the bundled runtime.");return;}' +
                    'if(typeof React==="undefined"||typeof ReactDOM==="undefined"){window.__showPreviewError("React or ReactDOM did not load from the bundled runtime.");return;}' +
                    // Pre-load every esm.sh module the import scan flagged. Failures stop
                    // the whole render so the user sees a precise message naming the lib
                    // and source — invaluable for air-gap diagnosis (IT can whitelist the
                    // exact host, or the user knows which import to replace).
                    'for(var i=0;i<externals.length;i++){' +
                        'var ext=externals[i];' +
                        'try{window[ext.global]=await import(ext.url);}' +
                        'catch(e){window.__showPreviewError("Failed to load \\""+ext.name+"\\" from "+ext.source+": "+(e&&e.message||e));return;}' +
                    '}' +
                    'var jsCode;try{jsCode=Babel.transform(src,{presets:presets}).code;}' +
                    'catch(e){var loc=e.loc?" (line "+e.loc.line+", col "+e.loc.column+")":"";' +
                        'window.__showPreviewError("JSX/Babel parse: "+(e.message||e)+loc);return;}' +
                    'var fn;try{fn=new Function(jsCode+"\\n;return (window.__default_export||(typeof App!==\\"undefined\\"?App:null)||(typeof Component!==\\"undefined\\"?Component:null));");}' +
                    'catch(e){window.__showPreviewError("Compile: "+(e.message||e));return;}' +
                    'var c;try{c=fn();}' +
                    'catch(e){window.__showPreviewError("Script error: "+(e.message||e)+(e.stack?"\\n\\n"+e.stack:""));return;}' +
                    // If no exported component was found but user code already
                    // rendered into #root via its own ReactDOM.render call,
                    // treat that as success — no error, no second render.
                    // Skipping the second render avoids createRoot warning
                    // about "container already passed to render" and prevents
                    // the user-rendered tree from being torn down.
                    'var rootEl=document.getElementById("root");' +
                    'if(!c){if(rootEl&&rootEl.childNodes.length>0)return;' +
                        'window.__showPreviewError("No exported component found. Define `export default ...` or a top-level function `App`/`Component`.");return;}' +
                    'try{ReactDOM.createRoot(rootEl).render(React.createElement(c));}' +
                    'catch(e){window.__showPreviewError("Render: "+(e.message||e)+(e.stack?"\\n\\n"+e.stack:""));}' +
                '});';

            // Forward parent chat.html theme so the iframe's default
            // scrollbars (and any system-default surfaces) match. Set
            // at render time only; if the user later flips the theme,
            // existing iframes keep their original color-scheme until
            // re-rendered.
            var parentTheme = (document.documentElement.getAttribute('data-theme') === 'dark') ? 'dark' : 'light';
            var hostHtml =
                '<!DOCTYPE html><html data-theme="' + parentTheme + '" data-jsx-source="' + transformedB64 + '" data-jsx-externals="' + externalsB64 + '" data-jsx-presets="' + presets + '">' +
                '<head><meta charset="UTF-8"/>' +
                '<meta name="viewport" content="width=device-width,initial-scale=1.0"/>' +
                '<style>*,*::before,*::after{box-sizing:border-box}' +
                'html{color-scheme:' + parentTheme + '}' +
                'html,body{margin:0;background:' + (parentTheme === 'dark' ? '#1e1e1e' : '#ffffff') + ';font-family:system-ui,-apple-system,Segoe UI,sans-serif}' +
                '#root{min-height:100vh}' +
                '#__preview_error{padding:10px 14px;background:#2b1d1d;color:#ffb4b4;font:12px/1.5 ui-monospace,Consolas,monospace;white-space:pre-wrap;border-bottom:1px solid #5a2c2c}' +
                SHADCN_CSS_VARS_CSS +
                '</style>' +
                '<script>' + errorInfraJs + '</script>' +
                '<script src="https://assiststudio.vendor/react-18.3.1.development.js" crossorigin></script>' +
                '<script src="https://assiststudio.vendor/react-dom-18.3.1.development.js" crossorigin></script>' +
                '<script>' + JSX_PRE_LIB_SHIM_JS + '</script>' +
                '<script src="https://assiststudio.vendor/babel-standalone-7.29.0.min.js" crossorigin></script>' +
                '<script src="https://assiststudio.vendor/tailwindcss-3.4.17.js"></script>' +
                '<script>' + SHADCN_TAILWIND_CONFIG_JS + '</script>' +
                libScripts +
                '<script>' + JSX_POST_LIB_SHIM_JS + '</script>' +
                '<script>' + SHADCN_SHIM_JS + '</script>' +
                '</head><body><div id="root"></div>' +
                '<script>' + bootstrapJs + '</script>' +
                '</body></html>';
            var attrEscaped2 = hostHtml
                .replace(/&/g, '&amp;')
                .replace(/"/g, '&quot;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;');
            var sourceB64 = utf8ToBase64(code);
            var jsxHl;
            try { jsxHl = hljs.highlight(code, { language: lang }).value; }
            catch(e) {
                try { jsxHl = hljs.highlight(code, { language: 'javascript' }).value; }
                catch(e2) { jsxHl = code.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
            }
            // Source-aware indicator so air-gap users / IT can identify the
            // exact host to whitelist (or which import to vendor) without
            // digging through devtools. Empty string when no externals.
            var externalNote = '';
            if (jsxResult.externals && jsxResult.externals.length) {
                externalNote = (L2.jsxPreviewExternalsLabel || 'Loaded externally') + ': ' +
                    jsxResult.externals.map(function(e){return e.name + ' (' + e.source + ')';}).join(', ');
            }
            return '<div class="diagram-block" data-kind="jsx" data-view="preview" data-theme="' + parentTheme + '" data-source-b64="' + sourceB64 + '">' +
                    previewHeader(lang, L2.jsxPreviewCopyTooltip || 'Copy source', parentTheme, externalNote) +
                    '<div class="html-frame">' +
                        // Same sandbox token set as the HTML branch above — see comment there.
                        '<iframe sandbox="allow-scripts allow-modals allow-downloads" srcdoc="' + attrEscaped2 + '"></iframe>' +
                    '</div>' +
                    '<pre class="code-view"><code class="hljs language-' + lang + '">' + jsxHl + '</code></pre>' +
                   '</div>';
        }
        """;

    /// <summary>
    /// Maps a JSX-bundle filename to the Content-Type header the WebView2
    /// expects on the synthesized response. JS is the only type we serve
    /// today, but this leaves room for a vendored CSS file (e.g. tailwind
    /// preflight) without another conditional in the call site.
    /// </summary>
    /// <param name="fileName">Vendor filename including extension.</param>
    /// <returns>MIME type string, defaulting to <c>application/javascript</c>.</returns>
    private static string ContentTypeFor(string fileName)
    {
        if (fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            return "text/css; charset=utf-8";
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return "application/json; charset=utf-8";
        if (fileName.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase))
            return "font/woff2";
        return "application/javascript; charset=utf-8";
    }

    /// <summary>
    /// Intercepts <c>https://assiststudio.vendor/&lt;file&gt;</c> requests
    /// and serves the matching embedded resource directly from the assembly,
    /// bypassing the disk entirely. Called from
    /// <see cref="OnWebResourceRequested"/> before the CDN allow-list check
    /// so vendored URLs never escape to the network.
    ///
    /// Returns <c>true</c> if the URI was a vendor URL and the response was
    /// set (either 200 with the resource or 404 if the file is missing);
    /// <c>false</c> if the URI is not a vendor URL and the caller should
    /// continue with normal handling.
    /// </summary>
    /// <param name="parsedUri">The parsed request URI.</param>
    /// <param name="args">WebResourceRequested event args to populate.</param>
    /// <param name="env">CoreWebView2 environment (for response synthesis).</param>
    internal static bool TryServeVendorResource(
        Uri parsedUri,
        CoreWebView2WebResourceRequestedEventArgs args,
        CoreWebView2Environment env)
    {
        if (!string.Equals(parsedUri.Host, VendorHostName, StringComparison.OrdinalIgnoreCase))
            return false;

        var fileName = parsedUri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(fileName))
        {
            args.Response = env.CreateWebResourceResponse(
                Content: null, StatusCode: 404, ReasonPhrase: "Not Found",
                Headers: "Content-Type: text/plain");
            return true;
        }

        if (!VendorResourceMap.Value.TryGetValue(fileName, out var resourceName))
        {
            args.Response = env.CreateWebResourceResponse(
                Content: null, StatusCode: 404, ReasonPhrase: "Not Found",
                Headers: "Content-Type: text/plain");
            return true;
        }

        // GetManifestResourceStream returns a fresh UnmanagedMemoryStream each
        // call backed by the assembly's mapped image. WinUI 3's CoreWebView2
        // projection expects an IRandomAccessStream, so wrap it via the
        // System.IO.WindowsRuntimeStreamExtensions adapter — the underlying
        // bytes are still served zero-copy from the assembly image.
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            args.Response = env.CreateWebResourceResponse(
                Content: null, StatusCode: 404, ReasonPhrase: "Not Found",
                Headers: "Content-Type: text/plain");
            return true;
        }

        // Sandbox iframes have a null origin and load these scripts with the
        // `crossorigin` attribute, so CORS headers are required even though
        // the content never leaves the process.
        var headers =
            "Content-Type: " + ContentTypeFor(fileName) + "\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Cache-Control: public, max-age=31536000, immutable";
        args.Response = env.CreateWebResourceResponse(
            Content: stream.AsRandomAccessStream(),
            StatusCode: 200, ReasonPhrase: "OK", Headers: headers);
        return true;
    }
}
