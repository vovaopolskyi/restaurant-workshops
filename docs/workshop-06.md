# Workshop 6: Owner group pre-order screen

Implemented the Owner Review UI, mock API workflow, real API client, 15-second background synchronization and Remove Guest bonus.

- Participant cards distinguish the owner's cart, submitted carts and guests still choosing.
- Consolidation includes submitted carts only and merges identical dishes; Grand Total is derived from consolidated items, not the group's raw total.
- Submit and removal use confirmation dialogs and prevent duplicate requests. Submitted/Closed groups are read-only.
- Background requests are silent, preserve the last successful data on failure and cannot overlap. Timers stop when the group closes, during mutations and when the page is destroyed.
- Reservation cards link to `/reservations/:reservationId/group-order`.
- New components use signal inputs/state, computed values, shared style tokens, explicit image dimensions and responsive layouts.

## Demo and production

Local demo: `http://localhost:4200/reservations/demo-reservation/group-order?demo`.

Reviewed AWS preview: `https://d2ebas3eqwfhw7.cloudfront.net/preview/workshop6/reservations/demo-reservation/group-order?demo`.

The preview is a development build with in-memory data under a separate S3 prefix. Its demo route does not replace a real user's stored login. Production builds require real authentication; their mock interceptor and preview guard exception are disabled.

The real API is configured as `https://0xnay410zf.execute-api.us-west-2.amazonaws.com/develop`. Real verification needs a Customer login, a group reservation and submitted guest carts. The read-only invitation lookup was corrected to remain available when the group is full or Submitted. Joining and cart mutation guards continue to reject invalid states.

## Tests and verification

The supplied practice, level-2, level-3 and bonus tests were copied unchanged into `src/app`. Level 2 replaces the static level-1 page tests as required by the workshop. The original tests remain in `homework-tests/` for reference. Added regression tests cover initial card presentation and reservation navigation.

Application/spec TypeScript checks and updated SCSS compilation passed locally. The backend suite passed all 270 tests after the invitation-read correction. Browser homework tests and the production build must pass through `workshop6.cmd` before publication; this agent environment blocks esbuild child-process creation with `spawn EPERM`, so those checks run in the user's regular PowerShell. Reports are saved under ignored `diagnostics/workshop6/`.

`finish6.cmd` runs the four frontend gates, pushes the feature branch, updates the backend, uploads the production frontend and refreshes CloudFront caches. It stops on a failed gate. Demo-preview files are preserved because the production upload does not delete objects.

## AI assistance

AI assisted with the client, components, styles, synchronization, tests and deployment scripts. The user visually reviewed the AWS preview and requested symbol-encoding, card-border and softer-color corrections. Provided tests, type checks, backend regressions and the browser preview are used to assess the result. Authenticated end-to-end production acceptance is a separate check; mocked tests alone do not prove live workflows.
