# Workshop 5: Owner group pre-order API

Implemented all three levels from `homework-backend.md`:

| Method | Endpoint | Result |
| --- | --- | --- |
| GET | `/guest/groups/{groupReservationId}/sessions` | Participant carts, submission flags and individual/group decimal totals |
| GET | `/reservations/{reservationId}/guest-session-id` | Owner session ID |
| POST | `/group-reservations/{groupReservationId}/submit` | Consolidated submitted-session quantities; body contains `reservationId` |
| DELETE | `/guest/groups/{groupReservationId}/sessions/{guestSessionId}` | Updated snapshot excluding the removed participant |

All operations require a Cognito identity and verify reservation ownership server-side. Responses use camelCase. Missing resources return 404, invalid relationships/states and missing orders return 400, unauthenticated requests return 401, and other users receive 403.

Submission merges dishes by ID from submitted sessions only and preserves existing order quantities. DynamoDB transactions update the order to `SubmittedByCustomer` and group to `Submitted` together. Conditions protect the original order cart and submitted sessions against concurrent edits/deletes. Repeat submissions are rejected. Conditional deletion requires an Open group. Owner-session initialization uses a deterministic identity and conditional insertion to preserve existing carts.

Snapshots hydrate sessions from the base table to avoid stale submission flags. Group membership discovery still uses an eventually consistent GSI, so newly joined sessions may take time to appear. Deleted identities are explicitly removed from the returned snapshot. Transactions support at most 98 submitted sessions plus the two status updates.

New groups initialize an owner session; existing groups can initialize one through the existing reservation-update flow. A historical group without an owner session returns 404 from the lookup endpoint; GET does not create data. Owner cart editing through guest-token endpoints is outside these four Owner operations.

## Verification

- Full backend suite: 270 passed, 0 failed, 0 skipped.
- API metadata validation: 52 routes passed CORS and path-parameter checks.
- Tests cover authentication, ownership, required errors, JSON contracts, decimal totals, aggregation, draft exclusion, stale-index deletion and persistence failures.

## Deploy

Configure the repository using `scripts/prepare-workshop4.ps1` first. From `backend/restaurant-backend-app`, run `workshop5.cmd` to test, sync deployment state, build/upload a new bundle, and update the IAM policy, API Lambda and API Gateway. The script uses a local virtual environment, with a fallback to the sibling workshop4 working environment on the original machine.

Local tests passed. AWS deployment and authenticated live acceptance tests remain to be performed; successful publication of source code alone does not verify the deployed API.
