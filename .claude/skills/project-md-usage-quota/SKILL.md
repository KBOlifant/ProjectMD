---
name: project-md-usage-quota
description: Use when changing the monthly scan allowance, the rate limit on creating scans, the sidebar usage meter, or how clearing history affects the count.
---

# Usage Quota and Rate Limit

Backend: `Services/UsageService.cs`, `ScanRecorder.cs`, `ScanRateLimiting.cs`, `Controllers/MeController.cs`. Frontend: `components/shell/usage-meter.tsx`, `app/functions/data/usage*.ts`.

## Rules the server enforces

- **Monthly allowance:** `Usage:MonthlyLimit` scans per UTC calendar month (default in `appsettings.json` is 10). `used` counts scans created this month; `resetsAt` is midnight UTC on the 1st of next month.
- **Cleared scans still count.** Clearing history is a soft delete (`DeletedAt`); `UsageService` deliberately ignores the query filter so users cannot reset their count by clearing. Do not change that.
- **No double spend:** `ScanRecorder` checks the quota and inserts in one transaction, serialised per user (a row lock on the user's own row), so two simultaneous requests cannot both pass. It returns false and saves nothing when the allowance is used up, and the controller answers 429 with `reason: "quota"`.
- **Rate limit:** `POST /api/scans` allows `RateLimiting:ScanCreate:PermitsPerMinute` (default 5) per user per minute; over it, 429 with `reason: "rate"` and `Retry-After`.
- **Development relaxes all of this.** `appsettings.Development.json` (not yet committed) sets the monthly limit to 1,000,000, the AI token caps to 100,000,000 and the rate limit to 1000 per minute. Test quota and rate behaviour with a low value set explicitly, not in a default dev run.
- **Daily AI budget:** see `../project-md-ai-budget-safety/SKILL.md`.

## Frontend

- `UsageMeter` shows `used / limit scans`, a dash while loading, the warning colour at the limit, and nothing for read-only roles.
- The limit shown in static mode is `USAGE_LIMIT` (10), which is only a placeholder for the server number in api mode.
- After a scan completes or fails, and after history is cleared, call `refreshUsage()`.
- A refused scan shows "You've used this month's scans." for `quota`, or "Too many requests. Try again shortly." for other 429s.

## Rules

- The browser check is a hint. The server decides.
- Changing the limit needs no code, only configuration, but update any text that quotes it.
- Keep the number in the meter, the Generate stats and the server in agreement; the meter reads the server.

## Verification

- Backend: `UsageServiceTests`, `ScanRecorderConcurrencyTests`, `ScansApiTests`.
- Set a small `Usage:MonthlyLimit`, scan up to the limit, clear history, and confirm the next scan is still refused.
