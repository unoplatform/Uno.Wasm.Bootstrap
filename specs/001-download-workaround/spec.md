# Feature Specification: Smooth Progress Bar During WebAssembly Application Loading

**Feature Branch**: `copilot/sub-pr-971`
**Created**: 2026-02-13
**Updated**: 2026-02-13
**Status**: Implemented
**Related Issues**: [dotnet/runtime#93941](https://github.com/dotnet/runtime/issues/93941), PR #1025

## Overview

When users load a WebAssembly application built with Uno.Wasm.Bootstrap, they observe a progress bar at the bottom of the screen indicating loading status. Currently, this progress bar exhibits erratic behavior—jumping backward, freezing at certain percentages, or showing inconsistent progress—which creates confusion and uncertainty about whether the application is actually loading or has stalled.

The underlying cause is that the runtime discovers assembly dependencies incrementally during the loading process, causing the total count of items to download to change dynamically. This makes it impossible to show an accurate linear progress indicator, resulting in poor user experience during the critical first-load phase.

Users need a progress bar that consistently moves forward, never jumps backward, and accurately reflects that loading is progressing even when the exact completion percentage is uncertain.

## User Scenarios & Testing

### P1: Smooth Progress on Fast Network Connections

**User Journey**: A developer or end-user accesses a Uno WebAssembly application on a fast network connection (100+ Mbps). The application has numerous assembly dependencies that are discovered incrementally.

**Priority Justification**: This is the most common scenario for development and enterprise environments. Users expect responsive, professional loading behavior.

**Independent Test Approach**: Deploy application with 20+ assemblies to test server. Load application on gigabit connection and record progress bar values every 100ms.

**Acceptance Scenarios**:

```gherkin
Given a Uno WebAssembly application with multiple assembly dependencies
When the user loads the application on a fast network connection
Then the progress bar should advance smoothly from 0% toward 100%
And the progress bar value should never decrease
And the progress bar should not remain frozen for more than 1 second at any value below 95%
And the progress bar should reach 100% when loading completes
```

**Edge Cases**:
- Application with 50+ assemblies (large dependency tree)
- Application loading from local development server (extremely fast)
- Browser with aggressive parallel download limits

### P2: Consistent Progress on Slow Network Connections

**User Journey**: An end-user accesses a Uno WebAssembly application from a mobile device on a slow network connection (2-5 Mbps). Assembly downloads take several seconds each.

**Priority Justification**: Mobile and low-bandwidth users are common in production scenarios. Poor progress indication on slow connections may cause users to abandon the application.

**Independent Test Approach**: Deploy application to test server. Use browser network throttling (Fast 3G profile) and record progress bar behavior during full load cycle.

**Acceptance Scenarios**:

```gherkin
Given a Uno WebAssembly application with multiple assembly dependencies
When the user loads the application on a slow network connection
Then the progress bar should advance gradually and continuously
And the progress bar should reflect ongoing download activity
And the progress bar should never show backward movement
And the user should see visible progress updates at least every 2 seconds
```

**Edge Cases**:
- Intermittent network interruptions with retry behavior
- Network timeout followed by successful retry
- Downloading large satellite assemblies (> 1MB)

### P3+: Accurate Progress with Varying Dependency Counts

**User Journey**: Different Uno WebAssembly applications have vastly different numbers of assemblies—from simple apps with 10 assemblies to complex apps with 100+ assemblies.

**Priority Justification**: The progress system should scale appropriately across application sizes without manual tuning.

**Independent Test Approach**: Test three applications: minimal (10 assemblies), standard (30 assemblies), and complex (80+ assemblies). Compare progress behavior across all three.

**Acceptance Scenarios**:

```gherkin
Given a Uno WebAssembly application with {10, 30, 80} assemblies
When the user loads the application
Then the progress bar should advance at an appropriate pace for the application size
And smaller applications should not rush to 100% within the first second
And larger applications should not stall at low percentages for extended periods
And the progress bar should complete within 5% of the actual loading completion
```

**Edge Cases**:
- Application with zero additional dependencies beyond initial manifest
- Application where all dependencies are discovered immediately (no incremental discovery)
- Application with unusual asset types (ICU data, PDB files, resources)

## Requirements

### Functional Requirements

**FR-1**: The progress bar value SHALL never decrease during the loading process, regardless of how the runtime reports resource counts.

**FR-2**: The progress bar SHALL advance smoothly without remaining visibly frozen (same value for >1 second) at any value below 95% while loading is active.

**FR-3**: The progress system SHALL analyze initial application configuration to estimate the total number of resources more accurately than simple asset counting.

**FR-4**: The progress system SHALL adjust its estimation dynamically as the runtime reports increasing resource counts during incremental dependency discovery.

**FR-5**: The progress system SHALL detect when downloads are completing faster than new dependencies are being discovered and advance progress proactively to prevent stalling.

**FR-6**: The progress system SHALL reserve the final 5% of progress (95-100%) for post-download initialization to ensure the bar doesn't reach 100% prematurely.

**FR-7**: The progress system SHALL complete progress indication (reach 100%) when the WebAssembly application initialization is fully complete.

**FR-8**: When debug logging is enabled (`MonoConfig.debugLevel > 0`), the progress system SHALL output detailed progress calculations including estimated totals, velocity metrics, and target adjustments to assist developers in diagnosing loading issues. The system SHALL respect the full `debugLevel` semantics: positive values enable logging, zero disables debugging, negative values enable debugging without console output.

### Key Entities

**Progress State**: Tracks current progress value, target progress value, estimated final resource count, and progress history over time.

**Resource Metrics**: Records resources loaded count, resources total count (as reported by runtime), and timestamps for velocity calculation.

**Asset Metadata**: Information from initial configuration including asset count, asset types (assembly, resource, PDB, ICU), and their characteristics affecting dependency loading.

## Success Criteria

**SC-1**: Users SHALL observe the progress bar advancing monotonically (never decreasing) in 100% of application loading scenarios.

**SC-2**: The progress bar SHALL avoid visible stalls (no value change for >1 second below 95%) in at least 95% of loading scenarios on connections faster than 5 Mbps.

**SC-3**: The progress bar final value SHALL reach within 5% of actual completion before jumping to 100% in at least 90% of loading scenarios.

**SC-4**: Users loading applications on throttled networks (2-5 Mbps) SHALL observe visible progress updates at minimum every 3 seconds.

**SC-5**: The progress system SHALL handle applications ranging from 10 to 100+ assemblies without requiring configuration changes or exhibiting significantly degraded behavior at either extreme.

**SC-6**: Developers enabling debug logging SHALL receive actionable diagnostic information including velocity metrics, estimation accuracy, and target adjustments with timestamps.

## Implementation

### Algorithm Design

The implementation uses a multi-layered approach combining algorithmic smoothing in TypeScript and visual smoothing via CSS transitions:

**Layer 1: Smart Initial Estimation**
- Analyzes MonoConfig assets by type (assembly, resource, PDB, other)
- Applies 1.5x dependency multiplier to assemblies (empirically determined based on typical dependency patterns)
- Sets conservative initial target (20% of estimated final, capped at 50%)
- Provides detailed debug logging for estimation accuracy validation

**Layer 2: Velocity-Based Smoothing**
- Maintains sliding window of progress history (1.5 seconds)
- Calculates download velocity (resources/second) from recent history
- Proactively advances target when completion ratio approaches 90% of target
- Caps velocity-based advancement at 10% per update to prevent overcorrection

**Layer 3: Stall Detection**
- Monitors time since last progress update (1 second threshold)
- Gently advances target by up to 2% when stalled
- Ensures continuous visual feedback during pauses

**Layer 4: Dynamic Target Adjustment**
- Uses convergence rate (50%) when new dependencies discovered
- Converges to 95% to reserve final 5% for initialization
- Updates estimated final total when runtime reports exceed estimates

**Layer 5: Strictly Non-Decreasing Progress**
- Tracks last reported value to prevent backward movement
- Scales progress as `(loaded/total) * currentTarget`
- Guarantees monotonic progress regardless of runtime behavior

**Layer 6: Visual Smoothing (CSS)**
- 300ms ease-out transitions on progress bar width
- Smooths discrete JavaScript value updates
- Cross-browser support (Chrome, Firefox, Safari, Edge)
- Separate transitions for light and dark themes

### Implementation Files

**Modified Files**:
1. `src/Uno.Wasm.Bootstrap/ts/Uno/WebAssembly/Bootstrapper.ts`
   - Added 7 configuration constants
   - Added 4 progress tracking fields
   - Enhanced `initializeProgressEstimation()` with asset type analysis
   - Completely rewrote `reportDownloadResourceProgress()` with velocity-based algorithm
   - Fixed debug logging to respect `debugLevel` semantics (only log when `> 0`)

2. `src/Uno.Wasm.Bootstrap/WasmCSS/uno-bootstrap.css`
   - Added 300ms ease-out transitions to `.uno-loader progress::-webkit-progress-value`
   - Added 300ms ease-out transitions to `.uno-loader progress::-moz-progress-bar`
   - Applied to both light and dark theme variants

### Configuration Constants

```typescript
MINIMUM_INITIAL_TARGET = 30           // Minimum starting percentage
INITIAL_TARGET_PERCENTAGE = 0.3       // Legacy constant (deprecated)
CONVERGENCE_RATE = 0.5               // Advance 50% of remaining on total changes
VELOCITY_WINDOW_MS = 500             // 500ms window for velocity calculation
MIN_VELOCITY_SAMPLES = 2             // Minimum samples before using velocity
VELOCITY_EXTRAPOLATION_CAP = 0.1     // Max 10% advance per velocity update
STALL_THRESHOLD_MS = 1000            // 1 second before considering stalled
FINAL_RESERVE_PERCENTAGE = 0.95      // Reserve 5% for completion
ASSEMBLY_DEPENDENCY_MULTIPLIER = 1.5 // Assemblies trigger more loads
```

### Debug Logging

When `MonoConfig.debugLevel > 0`, the system outputs:
- Initial asset analysis: counts by type, estimated final, initial target
- Target advancement: when total increases, new target value
- Velocity adjustments: advancement amount and calculated load rate
- Per-update progress: current value, loaded/total, current target

Debug logging respects the full `debugLevel` semantics:
- `debugLevel > 0`: Debugging enabled with console logging
- `debugLevel == 0`: Debugging disabled, no logs
- `debugLevel < 0`: Debugging enabled without console logging

### Testing Approach

For manual testing and validation, the `Uno.Wasm.Sample.RayTracer` project can be temporarily modified to include additional NuGet packages with many transitive dependencies:
- Newtonsoft.Json
- System.Linq.Async
- Microsoft.Extensions.Logging
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Configuration
- Microsoft.Extensions.Http
- System.Reactive
- System.Collections.Immutable

This creates a realistic scenario with numerous assemblies to validate smooth progress bar behavior during incremental dependency discovery.

## Out of Scope

- Modifying the .NET runtime's progress callback semantics (external dependency)
- Providing byte-level download progress (not available from runtime)
- Customizable progress bar styling or positioning (separate feature)
- Progress persistence across browser sessions
- Displaying estimated time remaining (unreliable with incremental discovery)

---

**Sources**:
- [Diving Into Spec-Driven Development With GitHub Spec Kit - Microsoft for Developers](https://developer.microsoft.com/blog/spec-driven-development-spec-kit)
- [GitHub Spec Kit Repository](https://github.com/github/spec-kit)
