# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Plugin.Maui.Onboarding is a .NET MAUI library implementing an app-tour /
walkthrough control: a dark scrim with a spotlight hole cut into it, moving
between controls with a tooltip card explaining each one (a "coach mark"
first-run tour). Targets `net10.0-android` and `net10.0-ios` only (no
Windows/Mac Catalyst/Tizen). There is no test project — verification is
build + running the example app.

## Commands

Build the library:
```bash
dotnet build src/Plugin.Maui.Onboarding/Plugin.Maui.Onboarding.csproj -f net10.0-android
dotnet build src/Plugin.Maui.Onboarding/Plugin.Maui.Onboarding.csproj -f net10.0-ios
```

Run the example app on a connected/booted Android device or emulator (finds
the serial via `adb devices`):
```bash
scripts/run-android-device.sh <device-serial>
scripts/run-android-device.sh --watch <device-serial>   # dotnet watch: rebuild+redeploy on change, streams logs
```

Pack the NuGet package (mirrors `.github/workflows/publish.yml`, which
publishes on GitHub release via NuGet Trusted Publishing/OIDC — no manual
push needed):
```bash
dotnet pack src/Plugin.Maui.Onboarding/Plugin.Maui.Onboarding.csproj -c Release -o ./nupkg
```

## Architecture

`OnboardingCoordinator` is the single public entry point consumers drive
directly; every other type in the library is an implementation detail it
owns. The library requires a Shell-based host app — the coordinator pushes
its overlay via `Shell.Current.Navigation`.

**Tour model** (immutable, built once): `OnboardingTour` (record: `Key` +
ordered `OnboardingStep`s) is assembled via `OnboardingTourBuilder`, which
stamps `IsLastStep` on the final step at `.Build()`. Each `OnboardingStep`
names a `TargetKey`, spotlight shape/corner radius/padding, and an optional
`RequiredRoute` to navigate to first.

**Marking targets**: `OnboardingTargetBehavior` (attached via XAML to any
`VisualElement`) registers/unregisters itself by key into the static,
weakly-referenced `Internals.OnboardingTargetRegistry` on attach/detach.
Re-registers on every attach since Shell can recreate pages on tab
re-entry.

**Starting/advancing a tour**: bounds are resolved per "segment" — a
contiguous run of steps sharing a page, starting at step 0 and at every step
with a non-null `RequiredRoute` — rather than for the whole tour up front.
`OnboardingCoordinator.ResolveSegmentAsync` resolves one segment's steps'
bounds (navigating via `Shell.Current.GoToAsync` first if the segment's
first step declares a `RequiredRoute`) entirely before the overlay is
shown/updated for it: this is deliberate and non-obvious, since once the
modal covers the page, native bounds lookups against some controls behind it
(observed with `CollectionView`'s RecyclerView) can stall indefinitely
rather than merely delay, so waiting it out post-push isn't a fix.
`BeginAsync` resolves the first segment before ever pushing the overlay;
`AdvanceAsync` notices when it's about to cross into an unresolved segment
(`_stepIndex >= _resolvedThroughExclusive`) and, when it does, pops the
overlay, resolves the new segment, then re-pushes it —
`OnboardingOverlayHost.ShowAsync` resets the overlay's geometry on every
re-push, which incidentally also stops the step-to-step move animation from
lerping across the page transition. `OnboardingTargetLocator.ResolveBoundsAsync`
polls every 75ms (10s timeout) and requires **two consecutive matching
bounds reads** before trusting a position, because virtualizing containers
can report a valid non-zero size from an early measure pass before their
on-screen position has settled. A step whose target never resolves gets a
`null` geometry and `AdvanceAsync` silently skips it rather than showing a
bogus spotlight.

**Absolute bounds**: `Internals.OnboardingBoundsExtensions.GetAbsoluteBounds`
is the one genuinely platform-specific piece (`#if ANDROID`/`#if IOS`) —
MAUI only exposes parent-relative layout position, not absolute screen
position. Both a target's bounds and the overlay's own root bounds are
read through this same method so subtracting one from the other cancels
out constant offsets (status bar, safe-area insets) automatically.

**The overlay**: `Internals.OnboardingOverlayHost` owns a single, lazily
created, reused-for-the-app's-lifetime `Views.OnboardingHostPage`
(transparent modal, pushed/popped via `PushModalAsync`/`PopModalAsync`,
*not* Mopups). `OnboardingOverlayView` (inside that page) holds a
`GraphicsView` painted by `Internals.SpotlightDrawable` plus a per-step
`Views.OnboardingTooltipCard`. `SpotlightDrawable` cuts the hole using a
single even-odd-wound path containing both the full-canvas rect and the
spotlight shape — not Porter-Duff destination-out compositing, since that
isn't reliable across Microsoft.Maui.Graphics backends — and separately
hand-draws a soft layered-stroke glow around the hole's edge plus a
direction-aware arrow (path data transcribed from an SVG, transformed by
hand-rolled 90°-rotation + scale + translate, not a canvas transform or
static asset) pointing from the tooltip back at the spotlight.
`Internals.OnboardingGeometryMath` is pure, view-free geometry: lerping
between two steps' geometry for the move animation, and auto-placing the
tooltip (prefers below/above the spotlight, falls back to left/right,
clamped to the screen) plus its connecting arrow.

**Advancing/completing**: `OnboardingCoordinator.AdvanceAsync` steps
through the pre-resolved geometries; reaching the end (or `SkipAsync`)
calls `CompleteAsync`, which marks the tour completed via
`Internals.OnboardingCompletionStore` (a `Preferences`-backed JSON blob,
keyed by tour `Key` — no host-app persistence dependency) and pops the
modal. `StartTourIfNotCompletedAsync` checks that store first;
`ReplayTourAsync` resets it and restarts unconditionally. Next/Skip taps
from the tooltip card are fire-and-forget event handlers
(`OnNextRequested`/`OnSkipRequested`) routed through `RunAndLogAsync` so an
exception doesn't silently vanish mid-tour — it at least reaches
Debug/logcat output.

**Styling**: `ScrimColor`/`TooltipBackgroundColor`/`TooltipBorderColor` on
`OnboardingCoordinator` forward down through `OnboardingOverlayHost` to
`OnboardingOverlayView`, settable at any time (including mid-tour) because
each is stored at every layer and re-applied whenever the lazily-created
page/card actually exists. Leaving `ScrimColor` unset falls back to
`onboarding_scrim_light`/`onboarding_scrim_dark` app resources if the host
app defines them (checked against `Application.Current.RequestedTheme`),
and beneath that to `SpotlightDrawable`'s compiled-in default.

## Project structure

```
src/Plugin.Maui.Onboarding/
  OnboardingCoordinator.cs        the entry point — starts/advances/skips a tour
  OnboardingTour.cs, OnboardingTourBuilder.cs, OnboardingStep.cs
  OnboardingTargetBehavior.cs     attach to any VisualElement to make it spotlightable
  OnboardingSpotlightShape.cs, OnboardingTooltipPlacement.cs, OnboardingArrowDirection.cs
  Internals/                      target registry/locator, geometry math, the scrim drawable
  Views/                          the overlay page/view and tooltip card (XAML)
example/Plugin.Maui.Onboarding.Example/   a MAUI app scaffold for trying the library
```
