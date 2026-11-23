# AI Usage and External Sources

The challenge description allows the use of external resources such as AI tools, provided their use is marked and
cited. This document describes how AI assistance was used in this solution.

## Tools

- **AI coding assistant:** GitHub Copilot
- **API documentation:** [Frankfurter API](https://frankfurter.dev)
- **Libraries:** Newtonsoft.Json (provided by the template), MSTest (unit tests)

## How AI was used

| Area | AI involvement | My role |
| --- | --- | --- |
| Part 1 – Currency converter (API client, models, MVVM view model, WPF view) | Used to improvise the implementation and refine code | Defined the requirements and design, reviewed every change, requested simplifications, ran and manually tested the application (including inspecting the API traffic in Fiddler) |
| Part 2 – Query caching (cache, eviction strategies, settings, caching decorator) | Used to improvise the implementation | Reviewed the design, reduced the number of types/files, verified thread safety and eviction behaviour against the real API |
| Unit tests | **Used extensively** – the test code was largely AI-generated | Defined the scenarios to cover and verified that the tests are meaningful (see below) |

## Verification

All AI-generated code was reviewed and verified rather than accepted as is:

- **Build:** solution builds with 0 warnings and 0 errors.
- **Manual testing:** application run against the live Frankfurter API; requests inspected with Fiddler.
- **Unit tests:** 104 tests covering the API client, view model (validation, debouncing, cancellation of outdated
  requests, error handling), cache, eviction strategies, configuration and thread safety.
- **Test quality check:** key logic was deliberately broken to confirm the corresponding tests fail – removing the
  cache lock, removing the outdated-response check and disabling the cache lookup each caused the relevant tests
  to fail.
- **Thread safety:** the cache was stress-tested with 16–32 concurrent threads; the same test fails when the lock is removed.