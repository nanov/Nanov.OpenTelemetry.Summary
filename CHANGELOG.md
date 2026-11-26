# Changelog

## [v1.0.0] - 2026-11-26
### BREAKING CHANGES
- due to [`ec6b21e`](https://github.com/nanov/Nanov.OpenTelemetry.Summary/commit/ec6b21e4ce15c5ba3d3d8d548c7bf2490fcb66b3) - striped per-series buffers, bound handles, .NET 10 and 11 targets *(PR [#5](https://github.com/nanov/Nanov.OpenTelemetry.Summary/pull/5) by [@nanov](https://github.com/nanov))*:

  {name}.max is now the window's exact maximum; a {name}.dropped counter appears once values are dropped; BufferCapacity applies per tag set; the public WeightedSample type and the internal swap-buffer and IReservoir types are removed.


### New Features
- [`eb80f63`](https://github.com/nanov/Nanov.OpenTelemetry.Summary/commit/eb80f6362ab0396a83658926c06194e8a8ba99ee) - add Summary HTTP instrumentation bridge packages *(PR [#2](https://github.com/nanov/Nanov.OpenTelemetry.Summary/pull/2) by [@nanov](https://github.com/nanov))*
- [`ec6b21e`](https://github.com/nanov/Nanov.OpenTelemetry.Summary/commit/ec6b21e4ce15c5ba3d3d8d548c7bf2490fcb66b3) - striped per-series buffers, bound handles, .NET 10 and 11 targets *(PR [#5](https://github.com/nanov/Nanov.OpenTelemetry.Summary/pull/5) by [@nanov](https://github.com/nanov))*

### Chores
- [`5136f64`](https://github.com/nanov/Nanov.OpenTelemetry.Summary/commit/5136f6422a8539fcdce673abd7070adec1083dda) - release v1.0.0 *(commit by [@github-actions[bot]](https://github.com/apps/github-actions))*


## [v0.1.0] - 2026-05-30
### New Features
- [`404e699`](https://github.com/nanov/Nanov.OpenTelemetry.Summary/commit/404e69972ff8236c8bcc3b6261a7933262fabe57) - add Summary.IsEnabled and drop OpenTelemetry SDK dependency *(PR [#1](https://github.com/nanov/Nanov.OpenTelemetry.Summary/pull/1) by [@nanov](https://github.com/nanov))*

### Bug Fixes
- [`72798db`](https://github.com/nanov/Nanov.OpenTelemetry.Summary/commit/72798db3cc2a725420347e036710516fcdfcfee7) - emit count and sum as cumulative per-tag counters *(PR [#3](https://github.com/nanov/Nanov.OpenTelemetry.Summary/pull/3) by [@nanov](https://github.com/nanov))*

### Chores
- [`9ffd8de`](https://github.com/nanov/Nanov.OpenTelemetry.Summary/commit/9ffd8de1b933927bd5d6f66c3efbf07eb5434b63) - readme pills *(commit by [@nanov](https://github.com/nanov))*
- [`78477ef`](https://github.com/nanov/Nanov.OpenTelemetry.Summary/commit/78477ef82aafdfa30c1ba6e28b71e8f5a8606958) - release v0.1.0 *(commit by [@github-actions[bot]](https://github.com/apps/github-actions))*


## [0.0.2] - 2026-04-15

Initial release.
[v0.1.0]: https://github.com/nanov/Nanov.OpenTelemetry.Summary/compare/v0.0.2...v0.1.0
[v1.0.0]: https://github.com/nanov/Nanov.OpenTelemetry.Summary/compare/v0.1.0...v1.0.0
