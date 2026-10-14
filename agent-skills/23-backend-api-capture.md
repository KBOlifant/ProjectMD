---
name: backend-api-capture
description: Record the API calls a page makes while it loads (endpoints, methods, statuses, response shapes, GraphQL operations, third parties) without storing any secret or real data.
---

# Backend API capture

**Owns:** `backend/ProjectMD.Api/Services/Extraction/Api/*` (new folder), the API hook inside `PageRequestProxy.cs`, one small hook in `PlaywrightPageFetcher.cs`, the mapping in `HtmlReducer.cs` that copies `RawPage.Api` and `RawPage.Design` into `PageDigest`, the content-hash change, tests under `ProjectMD.Api.Tests/Extraction/Api*`, README section.
**Does not own:** design capture ([24](24-backend-design-capture.md)), prompts or Markdown ([25](25-backend-structured-output.md)). Agents 24 and 25 run at the same time: before editing a shared file, re-read it, make the smallest change, and expect to retry if it changed.
**Types already exist** in `Services/Extraction/CaptureTypes.cs` (`ApiEndpoint`, `ThirdPartyHost`, `ApiCatalog`) and `RawPage` / `PageDigest` already have optional `Api` and `Design` members. Do not rename them.

## What to capture
- Every request the page makes that is an `xhr` or `fetch` (Playwright resource types) plus the document's own navigation is NOT an endpoint. `PageRequestProxy` already performs all requests through the SSRF-guarded client, so record there: method, host, path, status, content type, response size, resource type.
- **Group** identical calls. Normalise the path into a template: replace numeric segments, UUIDs, long hex or base64-looking segments, and segments with 8+ mixed digits/letters with `{id}` (keep readable words such as `users`, `v1`, `search`). Key = method + host + template.
- **First party vs third party:** first party = same registrable domain as the scanned page (simple last-two-labels rule plus a short list of multi-part suffixes such as `co.uk`, `com.au`, `co.za`). Third-party hosts are summarised in `ThirdPartyHost` (request count + category from a small embedded host->category table: analytics, ads, fonts, cdn, payments, auth, chat, monitoring, maps, video; unknown = null). Third-party calls are NOT listed as endpoints.
- **GraphQL:** a POST to a path containing `graphql` (or a JSON body with a `query` field) is one endpoint per operation: parse the body only enough to get the operation name and kind (`query`, `mutation`, `subscription`); never keep variables or the query text. Unnamed operations become `anonymous query`.
- **Response shape:** only for JSON responses up to 64 KB (read at most that much). Produce a compact type description, depth max 3, arrays described by their first element, at most 20 keys per object (then `...`): `{id:number,name:string,items:[{sku:string,price:number}]}`. Types only, never values. Skip the shape if the body is not valid JSON.
- **Query parameters:** names only, sorted, max 10.
- **Limits:** keep at most 40 first-party endpoints (highest count first, then alphabetical); set `Truncated`. Budget the extra work: total capture overhead must not add more than a few seconds and must never fail the scan (any capture error -> `Api = null` and carry on).

## Privacy and safety (hard rules)
- Never store: request or response bodies, header values, cookies, query-string VALUES, GraphQL variables, URL fragments, user info in URLs, or any field value from a JSON response. Shapes contain names and type names only. If a field NAME looks sensitive (`password`, `token`, `secret`, `authorization`, `ssn`, `card`, `cvv`, `email`, `phone`, `address`), keep the name but nothing else (names are not secrets).
- Redact path segments that look like tokens (long random strings) into `{id}` before anything is stored or logged.
- Never log shapes, paths or hosts above Debug level; do not add the captured data to exception messages.
- Capture must not weaken SSRF protection: do not add any new network path; reuse the existing proxy.

## Content hash
Include a canonical, order-independent form of the endpoint list (method, template, first-party flag, graphql operation) in `PageDigest.ContentHash` input so the cache invalidates when the API surface changes. Keep it independent of counts and statuses (they fluctuate). Include the design system canonical form too (see 24) when `Design` is present: coordinate by building the hash input from `Api` and `Design` in one place in `HtmlReducer`.

## Tests (fixtures, no real network; browser tests stay opt-in)
Path normalisation (ids, uuids, hex, words kept), first/third-party split incl. `co.uk`, grouping and counts, GraphQL operation parsing (named, anonymous, batched array body), shape inference (nested, arrays, nulls, cap on keys and depth, non-JSON), query param names only, truncation at 40, sensitive-looking names, that no value from a response or query string appears anywhere in the produced `ApiCatalog` (property test over a fixture with canary strings), hash stability under count changes and change when an endpoint is added. A local Kestrel test site (see existing `LocalSite`) that serves a page calling `/api/users/42`, `/api/users/43` and a GraphQL endpoint verifies the end-to-end capture through `PageRequestProxy` without a real browser if possible, otherwise as an opt-in browser test.

## Rules
Plain-English comment above each class/method. Do NOT connect to the real database, do NOT read or print `backend/.env` or any key, do not call any external service, no secrets in tracked files, do not run git, do not start the server from your shell. A stuck old server process may lock `backend/ProjectMD.Api/bin`; if `dotnet build` in place fails with a lock, build and test in a scratch copy under the scratchpad directory (exclude bin, obj and .env) and say so. `dotnet build ProjectMD.sln` 0 warnings; `dotnet test ProjectMD.sln` passes.
