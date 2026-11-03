# Workshop 8: group pre-order end-to-end automation

**Direction 1, levels 1–3. Approach: Playwright Test with JavaScript ES modules, Page Object Model, and API fixtures.** The target is the owner's integrated, deployed Angular/.NET application from the preceding workshops. These are application tests; they do not deploy AWS resources or test a third-party booking site.

## Architecture and rationale

Page objects own accessible UI locators and user actions. Scenario tests describe user-visible outcomes. Shared fixtures and API clients prepare isolated reservations, invitation sessions and carts, and verify backend state. API checks cover authorization and invalid state without duplicating the full UI flow for every role/error combination.

Playwright provides browser isolation, automatic waiting, parallel execution, tagging, HTML/JUnit reporting and failure evidence in one runner. This keeps the framework small while covering the requested UI/API test pyramid. Tests tagged `@ui` exercise the deployed interface; `@api` identifies API authorization and validation checks. No HTTP mocks substitute for the live environment in this suite.

```text
aqa/restaurant-aqa-framework/
  pages/                 # Login, Owner group order and waiter page objects
  support/               # Environment loader, API client and scenario lifecycle
  tests/
    participants.spec.js # L1 participants and totals
    submit.spec.js       # L2 consolidated submission
    remove.spec.js       # L3 guest removal
    access.spec.js       # Authorization and missing-resource checks
  playwright.config.js   # Browser, retries, reporters and evidence
  .env.example           # Empty configuration template
```

## Prerequisites and configuration

Use Node.js 22 or newer, npm, and your own deployed frontend and API containing the workshop 5/6 behavior. Use a dedicated test environment and three distinct accounts: reservation Owner, another customer, and a waiter assigned to the selected location. Each worker's test table must belong to that location, support at least six guests, and be available for dynamically selected future time slots. That location must expose at least two available dishes for cart/aggregation scenarios.

From this directory:

```powershell
Copy-Item .env.example .env
npm ci
npx playwright install chromium
```

Complete `.env` locally. It is ignored by Git; never commit the completed file, credentials or actual application addresses into the framework.

| Variable | Required value |
| --- | --- |
| `BASE_URL` | Your deployed frontend base URL |
| `API_BASE_URL` | Your deployed API base URL including its stage |
| `OWNER_EMAIL`, `OWNER_PASSWORD` | Dedicated reservation Owner account |
| `OTHER_EMAIL`, `OTHER_PASSWORD` | Different customer account for access-denial checks |
| `WAITER_EMAIL`, `WAITER_PASSWORD` | Waiter account assigned to the test location/table |
| `TEST_TABLE_ID` | Dedicated table identifier; capacity at least six |
| `TEST_TABLE_IDS` | Comma-separated dedicated tables, one per parallel worker; use `--workers=1` if only one table is configured |
| `TEST_LOCATION_ID` | Location containing that table and assigned waiter |
| `RUN_LIVE_E2E` | Set to `true` to explicitly permit live test-data creation and cleanup |
| `BROWSER_CHANNEL` | Optional local browser channel, for example `chrome`; leave empty to use Playwright Chromium |

The package lock is part of the reproducible installation: if it has not been generated yet, run `npm install --ignore-scripts` once and commit `package-lock.json` before using `npm ci` or the workflow. The Playwright version is pinned in `package.json`; install the matching browser binaries through the package's CLI.

The regular suite needs application accounts only. Two controlled-state tests additionally require an authenticated AWS CLI identity when explicitly enabled with `ENABLE_ADMIN_FIXTURES=true`, `AWS_ACCOUNT_ID`, `AWS_REGION`, and `AWS_GUEST_SESSIONS_TABLE`. They verify the AWS account and session membership, modify only the current scenario's session, and restore its original document in `finally`. Without opt-in, those two tests are visibly skipped; a run with skips does not prove that coverage passed. No AWS keys belong in `.env` or Git.

## Commands

For a fresh workshop environment, [prepare-workshop8.ps1](../../scripts/prepare-workshop8.ps1) creates three dedicated accounts (including a real Waiter), an isolated location, eight test tables and the Waiters assignment. It accepts `-ExpectedAccountId`, `-BaseUrl`, `-ApiBaseUrl`, and optional `-Region`. It refuses to overwrite an existing `.env` or bootstrap manifest, suppresses invitation emails, generates private passwords, and records created IDs in ignored `diagnostics/workshop8/bootstrap.json`. It requires your existing authenticated AWS CLI identity and permission to manage those fixture records/users.

[verify-workshop8.ps1](../../scripts/verify-workshop8.ps1) accepts `-ExpectedAccountId`, `-FrontendBucket`, `-CloudFrontDomain`, and optional `-Region`. It checks the account/origin, runs frontend type/regression checks, builds and publishes the Owner authentication correction, refreshes CloudFront, then executes E2E. It preserves preview files and does not push Git commits. This publication is necessary because the previous interceptor omitted Owner authentication on protected `/guest/groups/.../sessions` routes.

If an earlier bootstrap created `@example.invalid` accounts, run [repair-workshop8.ps1](../../scripts/repair-workshop8.ps1) with `-ExpectedAccountId` before testing again. The application's legacy email validator rejects that TLD. The repair replaces only recorded workshop accounts with `@example.com` accounts, suppresses email delivery, updates the private configuration and Waiter assignment, and disables the old accounts. Existing table/location fixtures stay in place. New bootstraps already use the compatible reserved example domain.

Before creating any reservation, global setup verifies sign-in and roles for all three accounts. An account configuration error therefore stops the run once with a clear message instead of failing every scenario during setup.

```powershell
npm run check          # Static JavaScript checks
npm run test:list      # Discover scenarios without creating application data
npm test               # All levels
npm run test:l1        # @L1
npm run test:l2        # @L2
npm run test:l3        # @L3
npm run test:api       # @api
npm run test:ui        # @ui
npm test -- --grep "@L2.*@api|@api.*@L2"
npm test -- --headed   # Watch browser actions locally
npm run report        # Open the HTML report
```

Test discovery/static checks do not require real account secrets. Live runs require all configured application accounts and both URLs. Do not treat discovery success as evidence that browser scenarios passed.

## Coverage matrix

The scenario files are the source of truth for executable coverage; use `npm run test:list` to inspect the exact tests and tags.

| Level | Coverage location | Intended behavior |
| --- | --- | --- |
| L1 | [participants.spec.js](tests/participants.spec.js) | Owner participant view; individual/cart totals; empty and draft carts; changed quantities |
| L1–L3 | [access.spec.js](tests/access.spec.js) | Owner-only access, another customer, waiter, anonymous/guest access and missing resources |
| L2 | [submit.spec.js](tests/submit.spec.js) | Submitted carts only, duplicate dish aggregation, status transitions, waiter-visible order and invalid submission states |
| L3 | [remove.spec.js](tests/remove.spec.js) | Guest removal, refreshed snapshot/total, wrong-group or missing session, Owner protection and invalid group state |

The deployed API prevents a guest from submitting an empty cart. [admin-states.spec.js](tests/admin-states.spec.js) uses the opt-in AWS fixture provider to arrange that state and a genuinely session-free group, then verifies the real API response. A missing-order case uses normal API preparation without creating its order. Normal order creation requires a nonempty order: the suite seeds one dish in the existing order and explicitly includes that quantity in final Waiter assertions, while the consolidation response contains submitted guest quantities only.

## Chosen behavior for underspecified cases

- A valid login does not grant Owner access: another customer or waiter receives `403`. Missing/invalid authentication is rejected by the API/authentication boundary; the suite distinguishes that from Owner authorization.
- Missing reservation, group or session returns `404` according to the applicable endpoint. A session in a different group returns `400` when deletion is attempted through the Owner's group.
- Consolidated submission of a group already submitted is rejected with `400`; it does not add quantities again.
- Owner sessions are protected from removal (`400`). A new group normally contains an empty Owner session, so removing the last guest leaves that Owner participant and a zero total if its cart is empty. A group containing genuinely no sessions returns `participants: []` and `total: 0`; this state is different from "no guests joined".
- Draft carts are visible in the Owner snapshot but excluded from submission. Submitted carts with identical dish IDs are consolidated by summed quantity.
- API snapshot `total` includes draft and submitted carts. The UI's Grand Total is the submitted-only consolidated total; individual cards retain draft subtotals.
- Tests validate error outcomes without depending on incidental error wording unless that text is part of the visible UI contract.

## Test data and isolation

API fixtures create unique reservation/session data per test and clean up after success or failure. Dates/times are generated relative to the current time instead of fixed calendar dates. Each browser test uses an independent context; CI retries must obtain fresh data rather than reuse an already submitted reservation. A dedicated test table/location and isolated time slots keep parallel runs independent.

Cleanup uses the reservation cancellation API. Cancellation does not physically remove every reservation/group/session record, and the current backend overlap checks can continue to reserve cancelled time slots. The fixture therefore uses unique data and dynamically chosen slots on later dates, with bounded collision retries. Repeated runs can exhaust the dedicated table's candidate slots; use a fresh dedicated table or an explicitly authorized cleanup provider when that happens. Cleanup failures are attached to the test result rather than hidden. Never use real bookings or production customer accounts for this suite.

## Reports and failure evidence

- `playwright-report/`: HTML report, opened by `npm run report`.
- `test-results/results.xml`: JUnit result for CI consumers.
- `test-results/`: retained traces/videos/screenshots for failed scenarios.

The runner retains failure traces and videos and captures failure screenshots. The configured run does not stop on the first failed test (`maxFailures: 0`), so later independent scenarios can still execute. Reports describe actual passed/failed/skipped outcomes; optional unsupported fixtures must remain visible as skipped.

## GitHub Actions: cloud execution

Workflow: [workshop-08-e2e.yml](../../.github/workflows/workshop-08-e2e.yml).

Add these **repository variables** in GitHub Actions settings:

```text
BASE_URL
API_BASE_URL
TEST_TABLE_ID
TEST_TABLE_IDS
TEST_LOCATION_ID
RUN_LIVE_E2E=true
```

Add **repository secrets** for `OWNER_EMAIL`, `OWNER_PASSWORD`, `OTHER_EMAIL`, `OTHER_PASSWORD`, `WAITER_EMAIL`, and `WAITER_PASSWORD`. Actual values are supplied through GitHub settings, not YAML. The live job uses the `aqa-e2e` environment; configure environment protection/reviewers if required for the shared test deployment.

The workflow runs static checks and test discovery on relevant pushes and pull requests. Pull requests, including forks, never receive application credentials and never run the mutating live suite. Trusted pushes to `main` or `feature/workshop-08-e2e` run live tests only when repository variable `RUN_LIVE_E2E` is `true`. Manual runs additionally require checking the `run_live` input and selecting `all`, `L1`, `L2` or `L3`. GitHub must have the workflow on its default branch for the normal manual-dispatch UI to be available.

Live runs use GitHub-hosted Ubuntu, install Chromium/system dependencies, run the requested scenarios, and upload reports/evidence even when tests fail. A final step restores failure status after artifacts are uploaded; `continue-on-error` on the test step does not turn a failed suite into a green job. The live job is serialized across workflow runs to avoid overlapping changes to the dedicated deployment.

After a run, open its **Artifacts**, download the report bundle, extract it and open the HTML report. Artifacts are retained for seven days and can contain test data from traces and screenshots, so use dedicated accounts.

## Validation status and optional work

This README describes the intended executable workflow. It does not assert that live scenarios or the GitHub Actions workflow have run successfully: record the actual command, result and report when they have been executed against your configured deployment.

The optional ReportPortal integration is not configured or claimed as complete. The implemented reporting path uses Playwright HTML/JUnit plus GitHub Actions artifacts. A CI definition alone is not evidence of a successful cloud run. CI intentionally skips `@admin` fixtures: running those in CI additionally requires a separately scoped AWS identity; ordinary application credentials cannot access DynamoDB directly.

References: [Playwright CI setup](https://playwright.dev/docs/ci-intro), [GitHub workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows).
