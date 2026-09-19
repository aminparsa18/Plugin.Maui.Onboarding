# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-09-19

### Fixed

- Onboarding overlay invisible on iOS: the modal is now presented natively
  instead of relying on the prior presentation path. (#7)

## [0.2.0] - 2026-08-29

### Added

- Multi-page tour support: steps are resolved per "segment" (a contiguous
  run of steps sharing a page) rather than for the whole tour up front, with
  navigation via `RequiredRoute` before a segment's bounds are resolved.
  Steps within a segment resolve concurrently instead of serially. (#3)
- Per-step custom content on the tooltip card, alongside the default
  title/description layout. (#5)

### Fixed

- `OnboardingTargetLocator` timeout/cancellation handling and bounds
  `IsClose` tolerances. (#2)
- Coordinator lifecycle and completion-state issues: tours that fail or are
  cancelled mid-segment-resolution now end cleanly instead of leaving
  `IsTourActive` stuck `true` with no Skip button to recover through. (#4)

## [0.1.0] - 2026-08-28

### Added

- Initial release: `OnboardingCoordinator`-driven app tour / walkthrough
  control for .NET MAUI (`net10.0-android`, `net10.0-ios`) — scrim with a
  spotlight hole, tooltip card, and target-registration via
  `OnboardingTargetBehavior`.
- Demo GIF and README reference; NuGet publish workflow (Trusted
  Publishing/OIDC via GitHub release).

### Fixed

- Tooltip placement fallback, spotlight shape-swap timing, corner-radius
  clamping, and resolution concurrency.
