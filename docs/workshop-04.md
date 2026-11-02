# Workshop 4: AWS application deployment

This repository starts with the original workshop archive in the first commit. The next commit records the deployment fixes and helper scripts for workshop 4. Workshop 5 Owner endpoints are a separate task and are not claimed as completed by this deployment.

## Deployed application

- Frontend: https://d2ebas3eqwfhw7.cloudfront.net
- API base URL: https://0xnay410zf.execute-api.us-west-2.amazonaws.com/develop
- Region: `us-west-2`

Terraform provisions the shared infrastructure (26 resources in the initial deployment), including S3, CloudFront, Cognito, SQS and the report scheduler. AWS Syndicate deploys the .NET Lambda functions, API Gateway and DynamoDB tables. Angular build artifacts are uploaded to the private frontend S3 bucket served by CloudFront.

## Changes in this submission

- Added CORS configuration to three API Gateway routes that failed during deployment.
- Unified location path parameter names in API Gateway to avoid conflicting `{id}` and `{locationId}` sibling resources. Lambda's router extracts its own parameter names from the request path.
- Added UTF-8 wrappers for Syndicate on Windows, including paths containing Cyrillic characters.
- Added Terraform helpers for initialization, validation, planning and application of a saved plan.
- Added deployment recovery and API update wrappers, including `--replace-output` for updates to the same deployment bundle.
- Added a read-only menu diagnostic and optional sample-data loader. Sample prices, descriptions and nutritional values are synthetic. The loader inserts 3 locations and 4 dishes using conditional writes; it does not create restaurant tables or overwrite matching IDs.
- Added Windows checksums to the Terraform dependency lock file without upgrading the pinned AWS provider.

## Setup on another machine

Prerequisites: AWS CLI authenticated to the target account, Terraform >= 1.10, .NET 10 SDK, Python 3.11 and a Node.js version supported by `frontend/restaurant-frontend-app/package.json`.

Run from the repository root, replacing the account ID with the account you intend to deploy to:

```powershell
.\scripts\prepare-workshop4.ps1 -AccountId 054776129200
```

The script generates ignored local configuration in `backend/restaurant-backend-app/.syndicate-config-local/` and Terraform backend/variable overrides. The original Syndicate configuration files remain templates. AWS credentials are read through the normal AWS credential chain and are not stored by these scripts.

Follow the state-bucket creation steps in the root README, using the same account ID and region. Then:

```powershell
.\scripts\init-workshop4.ps1
.\scripts\init-workshop4.ps1 -Action validate
.\scripts\init-workshop4.ps1 -Action plan
# Review the saved plan before applying it.
.\scripts\init-workshop4.ps1 -Action apply
```

From `backend/restaurant-backend-app`:

```powershell
py -3.11 -m venv .venv
.\.venv\Scripts\python.exe -m pip install aws-syndicate
dotnet build .\restaurant-backend-app.sln --configuration Release
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$env:SDCT_CONF = (Resolve-Path '.syndicate-config-local').Path
.\.venv\Scripts\syndicate.exe create-deploy-target-bucket
.\build.cmd
.\deploy.cmd
```

For later API-only corrections after building a new bundle, use `update-api.cmd`. The deployment wrapper is for the initial deployment/recovery workflow; it is not a replacement for deliberate code updates to existing Lambda functions.

From `frontend/restaurant-frontend-app`, set `BACKEND_URL` to your own API's deployed URL:

```powershell
$env:BACKEND_URL = 'https://0xnay410zf.execute-api.us-west-2.amazonaws.com/develop'
npm.cmd ci
npm.cmd run build
aws s3 sync .\dist\restaurant-frontend-app\browser s3://restaurant-frontend-054776129200/ --region us-west-2
```

Get the actual frontend address from Terraform outputs or the CloudFront console. Diagnostic/sample-data scripts are specific to the documented deployment: inspect their account, API and image URLs before reusing them for another account.

## Recorded validation

- `terraform fmt -check -recursive` and `terraform validate` passed.
- Initial infrastructure apply: 26 added, 0 changed, 0 destroyed.
- .NET Release build succeeded with 98 warnings.
- Syndicate backend deployment and subsequent API Gateway update completed successfully.
- Angular production build succeeded: initial bundle 789.32 kB; the 500 kB warning threshold was exceeded, while the 1 MB error limit was not.
- The postbuild configuration-placeholder check passed.
- The read-only diagnostic returned HTTP 200 with CORS headers for `/dishes?sortBy=PopularityDescending` and `/locations`. Both initially returned `[]`, matching the empty DynamoDB tables.
- `scripts/check-workshop4-api.py` verifies CORS entries and variable-path sibling consistency across 49 API routes. The original metadata fails these checks; the corrected metadata passes.

Dependency installation reported 57 vulnerabilities. They have not been remediated by this deployment commit; unit-test execution and a dependency security review are not claimed as completed.

## Local artifacts

Do not commit credentials, `.venv`, `node_modules`, build output, Terraform state/plans, `.terraform`, diagnostic logs or generated local deployment configuration. These paths are covered by repository ignore rules.
