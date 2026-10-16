# Walkthrough: From User Story to Tested Release and PR Merge

This walkthrough follows one small Yard feature through Azure Boards, a feature branch, code and tests, Azure Pipelines, deployment to a test environment, acceptance testing, and an Azure Repos pull-request merge. It ends with release verification and work-item closure.

This is an executable team exercise, not a record of work already performed. All work-item IDs, branch names, build numbers, and URLs below are examples. Creating this document does not create Azure Boards items, change application code, provision test environments, run deployments, enable paid services, or merge a PR.

Complete [SETUP.md](SETUP.md) first. It explains GitHub import/synchronization, the Windows agent, SQL Server, IIS, secrets, approvals, and wiki permissions. The application has no authentication: use fictional data and a restricted demo/test network only.

## 1. The Journey

| Step | Owner | Evidence produced |
| --- | --- | --- |
| Define story and acceptance criteria | Product owner | Azure Boards User Story |
| Break down the work and design tests | Developer and tester | Child Tasks and linked Test Cases |
| Create feature branch | Developer | Branch linked to the story |
| Implement and test locally | Developer | Task-linked commits and test results |
| Build the candidate | Azure Pipelines | Successful validation run and versioned web artifact |
| Deploy and test before merge | Release owner and tester | Isolated test deployment and acceptance-test run |
| Review and merge | Human reviewer | Approved Azure Repos PR and required policy results |
| Verify the merged release | Release owner and product owner | Release build/deployment, final test evidence, closed story |

Pre-merge deployment is **test-only**. Do not weaken the shared release environment's branch checks to deploy a feature branch. The existing deployment pipeline must not be redirected at an untrusted branch using shared credentials.

## 2. Agree the Working Model

### Repository and Branch Ownership

Use Azure Repos for Boards development links, pipeline checkout, build validation, and the final integration PR. Keep GitHub as the source-hosting/publication location agreed in [SETUP.md](SETUP.md).

For this exercise, choose **one integration authority**: the Azure Repos PR is where this story is accepted into `main`. Publish the same feature commits to GitHub if required, but do not independently merge the feature into GitHub `main` first. After the Azure Repos merge, publish the accepted result back to GitHub under the agreed synchronization policy. This is a deliberate promotion workflow, not uncontrolled edits to both default branches.

If your team requires GitHub-first merging, use the Azure Repos PR as a subsequent promotion PR instead and record the upstream GitHub PR. Do not mix these two models during the exercise. Never force-push shared default branches to resolve divergence.

### Setup Checks

- Use an Azure DevOps project with the **Agile** process for the exact `User Story` terminology below. Scrum uses Product Backlog Item; CMMI uses Requirement; Basic uses Issue. State names also differ by process.
- Confirm `main` exists and is the agreed release branch. The checked-in web deploy completion filter currently uses `master`, while database deploy uses `main`; normalize as described in the setup guide.
- All five existing pipeline YAML files currently have `trigger: none`. Queue builds manually or deliberately enable push triggers; Azure Repos PR validation must be configured as a branch policy.
- Require human reviewers, linked work items, comment resolution, and build validation on Azure Repos `main` according to team policy. Deny routine bypass rights.
- Use `property-management-ci` for the combined validation pipeline, `azure-pipelines-web` for ZIP packaging, and the existing split deployment definitions for trusted release delivery.
- Prepare a **separate test target and test deployment definition** using section 8 before attempting a pre-merge deployment. It is not provided automatically by the repository.
- For test-plan/suite authoring, provide **Basic + Test Plans** access or an eligible Visual Studio subscription, plus the appropriate area-path and test-management permissions. Basic access alone does not provide all Test Plans authoring features; Stakeholder access is insufficient.

References: [Create test plans and access requirements](https://learn.microsoft.com/en-us/azure/devops/test/create-a-test-plan?view=azure-devops) and [Azure Repos branch policies](https://learn.microsoft.com/en-us/azure/devops/repos/git/branch-policies?view=azure-devops).

## 3. Create the User Story in Azure Boards

Open **Boards > Backlogs**, select the correct team/backlog, and create a User Story. Use the following content, replacing the example ID `120` with the assigned ID.

| Field | Example value |
| --- | --- |
| Title | Filter properties by maximum monthly asking rent |
| Description | As a property manager, I want to filter properties by maximum monthly asking rent so that I can identify properties within a prospective tenant's budget. |
| Assigned To | Developer responsible for the feature |
| Area Path / Iteration Path | Team area and current sprint |
| Priority / Story Points | Agreed during refinement; example Priority 2, 3 points |
| Tags | `walkthrough; properties; rent-filter` |

Paste these acceptance criteria into the story:

1. **AC1 - Inclusive amount:** Given a maximum rent of `9000.00`, the property list contains only properties whose **property asking rent** is less than or equal to that value. A property at exactly `9000.00` is included.
2. **AC2 - Optional filter:** A blank maximum rent applies no rent restriction. Existing search and occupancy behavior remains unchanged.
3. **AC3 - Combined filters:** Search, occupancy, and maximum rent combine with AND semantics; name ordering remains unchanged.
4. **AC4 - Validation:** Negative values, nonnumeric values, values with more than two decimal places, and decimal overflow show an accessible validation message. Invalid input must not silently display an unrestricted portfolio or cause an HTTP 500. Zero is valid.
5. **AC5 - Stable request contract:** The GET parameter is `maxRent`. Its numeric representation is culture-invariant, for example `9000.50`; the page continues to display money using the configured culture/currency. Keyboard and browser refresh retain valid filter values.
6. **AC6 - Clear and empty states:** Clear filters removes all filter parameters. A valid filter with no matches displays the existing empty state.
7. **AC7 - Read-only behavior:** Filtering does not alter properties, leases, or rent payments and does not require a database schema change.

Record the product owner's agreement that this filters `Property.MonthlyRent`, **not** a tenant's contracted lease rent. This prevents a subtle implementation ambiguity.

**Checkpoint:** the story has an owner, sprint, agreed acceptance criteria, and a concrete definition of done before coding begins.

## 4. Add Child Tasks

Open the story and create these child Tasks through its work-item links or the sprint taskboard. Set the story as **Parent**, assign an owner, and estimate Remaining Work in your team's units.

| Example ID | Task | Suggested owner | Completion evidence |
| --- | --- | --- | --- |
| 121 | Define acceptance cases and deterministic fixtures | Tester/developer | Test Cases linked to AC1-AC7 |
| 122 | Implement validated maximum-rent query behavior | Developer | Controller change and automated validation tests |
| 123 | Add accessible maximum-rent input and validation display | Developer | Razor view change; keyboard/mobile checks |
| 124 | Add SQL/HTTP regression tests | Developer | Passing feature and regression tests in pipeline |
| 125 | Build, deploy to test, and execute acceptance tests | Tester/release owner | Build, deployment, and Test Plans run links |
| 126 | Review, merge, verify release, and update documentation | Reviewer/developer | Approved PR, release evidence, story closure |

Move the active task to **Active** as work starts. Finish tasks when their evidence exists, not merely because a commit message references them. Keep the story Active until its agreed completion criteria are met.

If optional security/review capabilities are being introduced for the first time, create separate enablement tasks with administrator owners, cost approval, and verification criteria. They are not automatically part of a small application feature's implementation estimate.

## 5. Define Test Cases Before Implementation

### Create the Plan and Requirement-Based Suite

1. Open **Test Plans > Test Plans > New Test Plan** and create `Yard - Sprint 1 Acceptance` with the same area/iteration as the story.
2. Add a **requirement-based suite**, query for User Story `120`, and select it. This establishes requirement coverage rather than merely grouping tests by a similar name.
3. Add the Test Cases below to that suite. Verify the story's **Tested By** links and each case's corresponding **Tests** relationship.
4. Set tester assignments and configurations, for example desktop Edge and a narrow/mobile viewport. Capture the application URL and fixture preconditions in each case.
5. In each Test Case, use the **Steps** grid with explicit Action and Expected Result entries. Include AC references in its description or tags.

Do not confuse a Test Case definition with an execution result: each candidate needs its own test run/results tied to the actual deployed build.

### Controlled Test Data

Use a dedicated acceptance database containing only fictional fixtures you control:

| Property name | City | Asking rent | Occupancy today |
| --- | --- | ---: | --- |
| Budget Cottage | Johannesburg | 6500.00 | Vacant |
| Boundary Flat | Johannesburg | 9000.00 | Occupied |
| Premium House | Johannesburg | 12000.00 | Vacant |
| Coastal Flat | Cape Town | 9000.50 | Vacant |

Create the occupied fixture with a fictional tenant and a lease spanning today; record the actual dates. Keep fixture IDs/names unique and clean up only your own test data. The built-in demo seed is not this exact fixture set and should not be assumed to produce these outcomes.

### Test Matrix

| Example ID | AC | Action / input | Expected result |
| --- | --- | --- | --- |
| 201 | AC1 | On Properties, enter `9000.00` and Filter | Budget Cottage and Boundary Flat only; boundary included |
| 202 | AC2 | Leave maximum rent blank; no other filters | All four fixtures in normal name order |
| 203 | AC3 | Search `Johannesburg`, select Vacant, enter `9000.00` | Budget Cottage only |
| 204 | AC4 | Submit `-1`, `abc`, `9000.001`, then an overflowing number through direct GET requests | Each shows validation feedback, no HTTP 500, and no unrestricted results |
| 205 | AC4 | Submit `0` | Valid request, existing empty state for these fixtures |
| 206 | AC5 | Enter `9000.50`, apply, refresh | Three fixtures within budget; valid value retained; displayed currency remains configured culture/currency |
| 207 | AC6 | Set all filters, then activate Clear filters | GET returns to the unfiltered Properties route and all fixtures |
| 208 | AC6 | Submit maximum `100.00` | Existing no-properties-found state, not an error |
| 209 | AC5 | Use keyboard only and narrow viewport | Input has an accessible label; error is discoverable; controls do not overlap |
| 210 | AC2, AC7 | Compare search/occupancy without rent; inspect records after filtering | Existing behavior unchanged; no data changes |

For Test Case `201`, enter these steps rather than just its title:

| Step | Action | Expected result |
| --- | --- | --- |
| 1 | Open the candidate's `/Properties` page | Page loads; candidate build/URL matches the test run |
| 2 | Clear existing filters and enter `9000.00` in maximum rent | Value is accepted and visible |
| 3 | Activate Filter | URL contains the valid `maxRent` parameter |
| 4 | Inspect every result | Only Budget Cottage and Boundary Flat appear; rent at boundary is included |
| 5 | Refresh the page | Same results and valid input value remain |

HTML number inputs can prevent invalid text from being typed. Test invalid values using crafted URLs such as `/Properties?maxRent=abc`; server-side validation must not depend on browser controls.

**Checkpoint:** every acceptance criterion has a linked Test Case, deterministic data, and an expected outcome.

## 6. Create the Feature Branch From the Story

### Azure Boards UI

1. Open User Story `120`.
2. In **Development**, select **Create a branch**.
3. Select the Azure Repos repository, base branch `main`, and branch name `feature/120-max-rent-filter`.
4. Create it and verify the story now shows the branch link.

If the UI action is unavailable, create the branch in **Repos > Branches**, then add an existing branch link in the work item's Development section. Do not create two different branches with the same intended purpose.

### Open the Branch Locally

Run from your existing clone. `origin` is GitHub and `azure` is Azure Repos, as in the setup guide. Verify remotes before using these commands:

```powershell
git status --short
git remote -v
git fetch azure
git switch --track azure/feature/120-max-rent-filter
git branch --show-current
git log -1 --oneline
```

If the local branch already exists, use `git switch feature/120-max-rent-filter` and inspect its tracking/up-to-date state instead of recreating it. Keep unrelated local edits out of the feature commits; do not discard them to make the walkthrough commands run.

Record the base commit in the story. If publishing a matching GitHub feature branch is part of your policy, push the same commits there after local validation. Do not open a competing merge path into GitHub `main`.

## 7. Implement, Test, and Link the Code

### Start at the Existing Implementation

| Existing file | Work to perform |
| --- | --- |
| [PropertiesController.cs](src/PropertyManagement.Web/Controllers/PropertiesController.cs) | Extend Index's validated GET input and compose the additional EF predicate |
| [Index.cshtml](src/PropertyManagement.Web/Views/Properties/Index.cshtml) | Extend the existing GET filter form, retained value, and accessible validation feedback |
| [DatabaseIntegrationTests.cs](tests/PropertyManagement.Tests/DatabaseIntegrationTests.cs) | Reuse the isolated SQL and WebApplicationFactory patterns for feature/regression tests |

The current action already composes name/address/city search and occupancy filters before including leases and ordering by name. Place the rent predicate in that query composition, not as an in-memory filter after fetching all properties.

### Implement in Small Steps

1. Under Task `124`, add a focused failing test for the inclusive boundary using seeded fixtures and an HTTP GET to `/Properties?maxRent=9000.00`. Add invalid direct-GET cases and verify the list does not broaden on validation failure.
2. Under Task `122`, define the optional GET input contract. Handle blank input separately from malformed input; enforce invariant decimal parsing, nonnegative amounts, and at most two fractional digits. A nullable decimal alone does not implement all of AC4/AC5.
3. Retain existing search/status values. For invalid rent, preserve the attempted value, display an accessible error, and return no property results for that invalid query. Agree this validation behavior with the tester.
4. After successful validation, compose the additional predicate before query execution:

   ```csharp
   if (maximumRent is decimal amount)
   {
       query = query.Where(property => property.MonthlyRent <= amount);
   }
   ```

5. Under Task `123`, add a labeled numeric input named `maxRent`, `min="0"`, and `step="0.01"` to the existing GET form. Serialize its valid value invariantly. Add server-side validation feedback; browser attributes are convenience, not enforcement.
6. Keep the existing clear-filters link and empty state. Check the filter bar layout on desktop/narrow screens and preserve the existing icon/design conventions.
7. Finish the combined-filter, clear/reset, decimal-format, and no-mutation tests. Use existing fixture/helper conventions; do not turn the introductory arithmetic tests into business-logic coverage.

This is a query/UI feature. No SQL model change, migration, stored-procedure edit, or production seed execution is needed. If you choose a different story that changes schema, include SQL project edits and reviewed DACPAC deployment before application testing.

### Validate Locally

Use a disposable test SQL target configured as described in the setup guide. Run from the repo root:

```powershell
dotnet tool restore
dotnet restore PropertyManagement.slnx
dotnet build PropertyManagement.slnx -c Release --no-restore -warnaserror
dotnet test tests/PropertyManagement.Tests/PropertyManagement.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PropertyRentFilter" --logger "trx;LogFileName=rent-filter.trx" --results-directory artifacts/rent-filter-tests
dotnet test tests/PropertyManagement.Tests/PropertyManagement.Tests.csproj -c Release --no-restore --logger "trx;LogFileName=regression.trx" --results-directory artifacts/regression-tests
```

`PropertyRentFilter` is a **proposed naming convention**, not a test currently supplied by this walkthrough. Name your added tests accordingly or substitute their actual names. Confirm the focused run executes the intended tests; a zero-test run is not a pass. The full run exercises existing SQL/HTTP regressions and generates/deletes isolated test databases.

Run the application locally, execute the relevant acceptance cases, and attach summarized results to the task. Do not publish test connection strings or credentials in logs/screenshots.

### Commit With Task Links

Review and stage only intended files. For example:

```powershell
git diff --check
git diff
git add src/PropertyManagement.Web/Controllers/PropertiesController.cs tests/PropertyManagement.Tests/DatabaseIntegrationTests.cs
git commit -m "#122 #124 Add validated maximum rent filtering and regression tests"
git add src/PropertyManagement.Web/Views/Properties/Index.cshtml
git commit -m "#123 Add maximum rent filter control and accessible validation"
git push azure feature/120-max-rent-filter
git push origin feature/120-max-rent-filter
```

The second push is required only by the agreed GitHub publication policy. Review both commits for secrets and unrelated edits first. Split commits only when each staged change is coherent; do not force this example's split if your validation/UI implementation is tightly coupled.

For commits stored in Azure Repos, `#122` references the Azure Boards work item. `AB#122` is the convention for configured **GitHub-to-Azure-Boards integration**, not a replacement for checking Azure Repos links. Enable that GitHub integration separately if you want GitHub-native development links; storing a branch on GitHub does not create them by itself.

Open Tasks `122`-`124` and verify their Development links show the Azure Repos commits. Add missing existing commit links through the UI if needed. Avoid completion keywords until the work is actually verified.

## 8. Build and Deploy the Candidate Before Merge

### Build the Exact Feature Revision

1. Manually queue `property-management-ci` from `feature/120-max-rent-filter` with `deployDatabase=false`. It must build the solution and pass isolated SQL/HTTP integration tests.
2. Queue `azure-pipelines-web` from the **same feature commit**. It runs the existing Unit-category tests/coverage and publishes `web/web.zip`; it does not replace full feature/regression validation.
3. Record each pipeline run URL, source branch, full source commit, and artifact-producing build ID on Task `125`.
4. If you push a fix between the two builds, rebuild so the evidence refers to the same revision. Do not deploy a ZIP built before the final fix.

The combined pipeline's `web` artifact is a directory, not the ZIP contract required by the split web deploy. Use the split web build as the ZIP producer. A feature-branch build should not trigger the trusted release deployment once its resource filter is correctly restricted to `main`.

### Prepare a Separate Test Deployment Definition

Have a trusted maintainer create a reviewed test-only copy/adaptation of [azure-pipelines-web-deploy.yml](azure-pipelines-web-deploy.yml). This is a setup task, **not an existing pipeline supplied here**. Save its trusted YAML on the approved configuration branch and configure a separate definition such as `azure-pipelines-web-deploy-test`.

Apply all of these changes to the test copy:

| Setting | Test-only value / behavior |
| --- | --- |
| Top-level push and PR triggers | Disabled; run manually |
| `webBuild` completion trigger | Disabled using `trigger: none` on that pipeline resource |
| Deployment environment | New protected `property-management-test` environment |
| Agent pool | Dedicated restricted test host/pool, for example `SelfHostedTest` |
| Variable group | New `PropertyManagement-Test`, with its own secret `SqlDeploymentConnectionString` |
| SQL target | Separate fictional-data acceptance database, not the shared release database |
| Site/app pool | `jhbdemo-test` |
| Physical path / port | Dedicated path such as `D:\jhbdemo-test`, free port such as `8081` |
| Smoke test | Fail the job when the site is unavailable, rather than preserving the original warning-only behavior |
| Release notes/wiki | Either disable all related checkout/tasks in this test copy, or use a separate test notes location and appropriate permissions |

Change all environment references, including any hardcoded labels in notes if retaining them. Authorize only the test definition to the test pool, group, and environment. Do not give the feature build or untrusted PR validation access to those resources. Keep release resource checks intact.

The deployment YAML should come from trusted configuration; the selected **artifact** can come from the reviewed feature candidate. For branch-control checks, account for both the deployment definition's source and its linked build resource. If allowing this candidate requires a branch exception, allow only the reviewed feature branch on **test-only** resources, with approval; never broadly allow every feature branch on release resources.

On a shared machine, a separate IIS site is not a strong security boundary against a privileged deployment agent. Prefer a disposable/dedicated test host for code not yet merged.

### Prepare the Test Database and Deploy

1. Use the schema built from the same candidate revision to initialize the separate acceptance database. Run the existing [Deploy-Database.ps1](scripts/Deploy-Database.ps1) Preview/Publish flow using test-only credentials and a reviewed plan, or use an equivalently protected test SQL delivery definition.
2. Create the fictional fixture records from section 5. Do not use integration-test cleanup helpers against a shared database.
3. Queue `azure-pipelines-web-deploy-test` from its trusted configuration branch.
4. In **Run pipeline > Resources**, select the exact successful feature `webBuild` run recorded above. Verify its commit, not just a similar build number.
5. Review approvals and deploy. Confirm the test site starts, uses the test SQL target, and returns successful HTTP responses.
6. Add the test deployment run URL, application URL, environment, and consumed build ID to Task `125` and the story.

**Checkpoint:** the feature artifact is deployed and reachable only on the isolated test target. No release environment has consumed the unmerged feature.

## 9. Execute Acceptance Tests and Record Bugs

1. In the requirement-based Test Plans suite, select the assigned cases/configurations and start a manual run using the web runner.
2. Record the candidate build as the build under test using the available run/plan settings. Also add the exact build/deployment URLs and commit in run comments or attachments so there is no ambiguity.
3. Execute each Action/Expected Result pair against the test site and mark each step and case Passed, Failed, or Blocked honestly. Attach relevant screenshots and observed values without credentials or personal data.
4. If a case fails, create a Bug from the test runner so the steps/results and available diagnostic context are carried across. Link the Bug to Story `120`, record the build/commit, and distinguish expected from actual behavior.
5. Fix under a linked task/bug on the same feature branch, push, rerun validation/build, deploy the new artifact, and create a fresh acceptance run. Do not reuse old passing results as proof of a new revision.
6. Attach the final passing run to Task `125`; record who accepted AC1-AC7 and when. Keep earlier failures for auditability.

Published MSTest TRX results appear in the pipeline's Tests tab. They do **not automatically** establish that each manual Azure Test Plans case was executed or link an automated method to its Test Case. If the team requires automated-test association, configure it explicitly and verify the association/result reporting with supported tooling; otherwise keep manual acceptance and automated pipeline evidence clearly separate.

**Exit condition:** no unresolved acceptance failures; any accepted residual risk has an owner and recorded decision. Mark implementation/test tasks complete as appropriate, but do not close the story before review/release obligations are satisfied.

## 10. Create and Review the Azure Repos PR

Create a PR in **Repos > Pull requests > New pull request** from `feature/120-max-rent-filter` into `main`. Alternatively, use the branch link in the story's Development section. Link Story `120`, its implementation tasks, and resolved bugs in the PR's **Work items** section. Verify the PR appears back on the story.

Use a title such as `Add maximum monthly asking-rent filter (#120)` and this description template:

```markdown
## Story
User Story #120: Filter properties by maximum monthly asking rent.
Tasks: #121, #122, #123, #124, #125, #126.

## Changes
- Validate optional invariant maximum-rent GET input.
- Filter property asking rent inclusively and combine with existing filters.
- Retain values and show accessible validation feedback.
- No database schema change.

## Evidence
- Combined validation run: <URL>, commit <SHA>
- ZIP-producing web build: <URL>, commit <SHA>
- Test deployment: <URL>, consumed build <ID>
- Acceptance test run: <URL>, AC1-AC7 passed
- Security/review results: <URLs or explicitly not enabled>

## Risk and recovery
- Read-only filtering; no schema or data migration.
- Revert through a reviewed PR/redeploy a compatible retained package.
```

Opening the PR earlier is fine if the team prefers ongoing discussion, but the merge decision comes after the candidate deployment/test evidence requested in this exercise.

### Required Validation and Human Review

1. Confirm the `main` **Build validation** policy automatically queues the combined validation pipeline with shared SQL deployment disabled. This validates the PR merge result, not merely the earlier branch build.
2. Set build expiration/revalidation policy so target-branch changes require current evidence. If other changes land on `main`, rerun the required policies and assess whether acceptance must be repeated on the refreshed candidate.
3. Have a reviewer check parsing, query composition, boundary conditions, retained form values, invalid requests, accessibility, regression coverage, and lack of schema changes.
4. Address comments with linked commits, rebuild, redeploy/retest any changed candidate, and resolve discussion only after the concern is actually handled.
5. Verify policy results, human approval, linked work items, and current acceptance evidence refer to the final iteration.

The optional Advanced Security and Copilot sections below fit here. Neither is enabled by adding this document, and neither replaces human ownership or acceptance testing.

## 11. Optional: GitHub Advanced Security for Azure DevOps

This scans the **Azure Repos** copy. Security features on the GitHub-hosted copy are separate settings/entitlements and do not automatically protect Azure Repos.

### Enable With Administrator and Cost Approval

1. Check the current [Advanced Security configuration guide](https://learn.microsoft.com/en-us/azure/devops/repos/security/configure-github-advanced-security-features?view=azure-devops). Azure DevOps Services exposes bundled Advanced Security and/or standalone **Secret Protection** and **Code Security** experiences depending on your organization's offering.
2. Have an administrator with the required Advanced Security management permission review billing/active-committer estimates and authorize the chargeable service. Do not enable organization-wide billing as an incidental feature-development step.
3. In **Project settings > Repos > Repositories**, select the repository and enable the approved product(s). Keep secret push protection enabled.
4. Secret/repository scanning starts with Secret Protection or the bundled equivalent; code and dependency scanning need their own setup. Enabling the product alone is not proof that a code/dependency scan ran.
5. Permit the required scanning tasks through organizational task policy. Use nonprivileged hosted build agents; do not scan untrusted code on the IIS deployment agent.

### Scan the Candidate and PR

For a quick baseline, use the documented default setups. For this walkthrough's branch/PR evidence, use **advanced pipeline setup** so the candidate and PR merge result are scanned as well as `main`. Scheduled default-branch CodeQL analysis alone is not per-PR validation.

Add the following sequence to a reviewed build-validation job or a separate security-validation pipeline, not to the privileged deployment job. This illustrative job sequence uses the existing solution and pinned SDK requirements; integrate it into real YAML under a separately reviewed change:

```yaml
pool:
  vmImage: ubuntu-latest

steps:
  - checkout: self

  - task: UseDotNet@2
    inputs:
      packageType: sdk
      version: 10.0.x

  - task: AdvancedSecurity-Codeql-Init@1
    inputs:
      languages: csharp
      buildtype: manual

  - script: dotnet restore PropertyManagement.slnx
    displayName: Restore dependencies

  - script: dotnet build PropertyManagement.slnx -c Release --no-restore --no-incremental -m:1 /p:UseSharedCompilation=false -warnaserror
    displayName: Build under CodeQL tracing

  - task: AdvancedSecurity-Dependency-Scanning@1

  - task: AdvancedSecurity-Codeql-Analyze@1
    inputs:
      WaitForProcessing: true
```

Initialize CodeQL **before** compilation and analyze **after** it in the same job. Use clean hosted workspaces, avoid compiler/build caches, and restore dependencies before dependency scanning. This scans C#; add supported languages separately if the feature changes JavaScript. It is not a security analysis of SQL deployment scripts or an authorization implementation.

1. Scan the default/target branch first to establish the baseline, then run the security build for the candidate and PR through a build-validation policy.
2. Open **Repos > Advanced Security** and inspect the relevant branch/pipeline results. Review PR annotations when available, not just a successful task exit.
3. For a blocking security gate, configure the documented status-check branch policy, for example **AdvancedSecurity/NewHighAndCritical**. Choose **AllHighAndCritical** only if the team intends existing high/critical findings to block too. These checks appear after applicable scanning runs; follow current setup prerequisites and wait-for-processing guidance.
4. Link actionable findings to Bugs/Tasks, fix on the feature branch, and rerun the scanner. A resolved annotation alone does not close the underlying alert. Accept/dismiss risk only with authorized rationale.
5. Record scan run/alert links and policy outcomes in the PR. A green pipeline by itself does not mean there were no security findings or that a merge-blocking security policy exists.

For a safe demonstration, show enablement, scan execution, and alert triage. Do not introduce live secrets or intentionally vulnerable production code. If a real secret is discovered, revoke/rotate it; deleting its visible text or dismissing an alert is insufficient.

References: [Code scanning](https://learn.microsoft.com/en-us/azure/devops/repos/security/github-advanced-security-code-scanning?view=azure-devops) and [Dependency scanning](https://learn.microsoft.com/en-us/azure/devops/repos/security/github-advanced-security-dependency-scanning?view=azure-devops).

## 12. Optional: Copilot Code Review in Azure Repos

Use the requested [Copilot code-review guide](https://learn.microsoft.com/en-us/azure/devops/repos/git/copilot-code-reviews?view=azure-devops) as the source of truth. The guidance checked for this walkthrough describes a **limited preview**; availability, requirements, and billing can change.

Copilot PR review, GitHub Advanced Security, and Copilot Autofix are different capabilities. Enabling one does not enable the others.

### Enablement and Billing

1. Confirm preview availability and obtain administrator approval for a linked Azure subscription and usage billing. Current guidance bills completed reviews through Azure Cost Management based on token usage; do not assume an editor Copilot license covers Azure Repos reviews.
2. A Project Collection Administrator enables organization access under **Organization settings > Repos > Repositories > GitHub Copilot code review**.
3. A Project Administrator/repository administrator enables the intended project/repository within the organization's controls. Prefer a small initial scope instead of enabling every repository.
4. Opt in through user **Preview features** where required, unless enabled for everyone by an administrator. Verify **GitHub Copilot** appears in the PR reviewer list.
5. Configure the supported review compute pool. Current guidance supports the default Azure Pipelines option or a suitable **Managed DevOps Pool with the latest Ubuntu image**; **self-hosted pools and Windows images are not supported**. Do not select this repository's `SelfHosted` Windows IIS pool.
6. Choose a default review effort and monitor costs. Higher effort can consume more tokens; budget alerts notify but do not stop reviews or cap charges.

### Request and Act on the Review

1. Ensure the PR is **Active**, conflict-free, and within the current documented size/rate limits. A draft PR is not the supported review state described in the current guide.
2. In the PR's Reviewers list, select **Request** beside **GitHub Copilot**, choosing the default or an explicit effort level.
3. Read each finding. Reproduce/assess it before changing code; link real defects to existing tasks or new bugs, implement the fix, and rerun the relevant tests.
4. Request a fresh review after new commits where supported. Current guidance does **not** automatically re-review every push and limits duplicate reviews of the same PR version. Replying to comments does not cause Copilot to follow up.
5. Record the review outcome in the PR and have the human reviewer make the approval decision.

For automatic initial requests, enable **Automatically request Copilot code review** in the target branch's policies after feature enablement. Feature availability and automatic-request policy are separate settings.

Copilot posts a **Comment** review; it does not approve or request changes, satisfy required human reviewers, or inherently block a merge. Comment-resolution policies can still apply to its discussions. Security gates and normal branch policies remain separate.

**Optional-step verification:** a review completes on the intended PR iteration, findings are assessed, fixes have fresh evidence, a human reviewer approves, and the team has checked usage/costs. If unavailable, record the step as skipped with the reason and proceed with human review and required tests.

## 13. Complete the PR Merge

Only complete the PR when all mandatory checks and the latest acceptance evidence are satisfactory:

1. Verify the target is Azure Repos `main`, the final diff is scoped to the story, linked work items are correct, and no secrets/unrelated files are included.
2. Ensure required build/security/status policies pass, human approvals are current, and blocking comments are resolved. Do not bypass policies to finish the demonstration.
3. Select the team's approved merge strategy. A normal merge retains task-linked commits; squash creates a new commit, so preserve story/task references in the squash message and rely on explicit PR work-item links too.
4. Review completion options carefully. Do not automatically complete the story if the definition of done still requires post-merge deployment verification. Complete only work items whose obligations are fulfilled.
5. Complete the PR and record the PR URL, completion time, and resulting `main` commit on Story `120`.
6. Delete the completed feature branch only under the team's policy, after retaining traceability and any needed build artifacts. Avoid immediately deleting the GitHub branch if synchronization/review still depends on it.

**Checkpoint:** the feature is integrated through an approved Azure Repos PR, not directly pushed to the protected default branch.

## 14. Verify the Merged Release and Close the Story

The pre-merge deployment proves the candidate works in test; the merged revision can have a different commit and include target-branch changes. Validate and deploy the accepted revision explicitly.

1. Confirm Azure Repos `main` contains the completed PR. Run the combined validation and split web build on that revision, automatically if push triggers were enabled or manually otherwise.
2. Check the trusted release deployment consumes the exact intended successful `main` web build, not the earlier feature package. Follow environment approvals and the setup guide's database-before-web ordering when a release includes schema changes.
3. For this story there is no schema change. Do not run an unnecessary database deployment just to complete the demonstration; verify the existing target schema is compatible.
4. Verify the application directly and rerun critical boundary/combined-filter cases against the released demo target. The original web deploy smoke test is warning-only on HTTP failure, so a green run alone is insufficient.
5. Check deployment history, the release-notes artifact, and the project wiki. Confirm the consumed build and story/commit links; missing associations should be investigated rather than inferred from a similar title.
6. Attach final build/deployment/test links to Task `126` and Story `120`. Complete the remaining tasks, obtain product-owner acceptance, and move the story to **Closed** or the equivalent terminal state.
7. Publish the accepted source back to GitHub using the agreed synchronization process. Prefer a reviewed GitHub synchronization PR if `main` is protected. Account for merge/squash history differences; do not force one remote to overwrite the other.

For a team publishing Azure Repos' accepted result to a GitHub review branch, an example is:

```powershell
git fetch azure
git push origin refs/remotes/azure/main:refs/heads/sync/azure-story-120
```

Open a GitHub PR from that synchronization branch into GitHub `main`, review its full diff/history, and follow that repository's checks. This publication PR is not a second independent feature implementation. If the team requires identical default-branch commit IDs, establish a permitted fast-forward synchronization policy rather than assuming a squash/merge will preserve IDs.

### Final Traceability Record

Add a concise comment to the story using the real identifiers:

```text
Story: 120 - Maximum monthly asking-rent filter
Tasks: 121-126 complete
Test Cases: 201-210 linked through requirement-based suite
Feature branch: feature/120-max-rent-filter
Final candidate commit: <SHA>
Candidate validation and web build: <URLs>
Test deployment and acceptance run: <URLs>
Security scan / Copilot review: <URLs, or not enabled and reason>
Azure Repos PR and merged commit: <URL / SHA>
Release build, deployment, verification: <URLs>
Wiki release notes: <URL>
GitHub publication PR/commit: <URL / SHA>
Accepted by: <name>, <date>
```

## 15. Walkthrough Exit Checklist

- [ ] Story and AC1-AC7 are agreed in Azure Boards.
- [ ] Child Tasks have owners, state, and completion evidence.
- [ ] Linked Test Cases have explicit steps, deterministic fixtures, and executed results.
- [ ] Feature branch, commits, and Azure Repos PR link back to the work items.
- [ ] Focused feature tests and the full regression suite execute and pass.
- [ ] Candidate ZIP/build identity matches the isolated test deployment and acceptance run.
- [ ] Required PR policies validate the current merge result; a human approves.
- [ ] Optional security/review steps are evidenced or explicitly marked skipped, without silently enabling paid services.
- [ ] PR merges without bypass; merged-release validation and verification succeed.
- [ ] Story closes with traceable evidence, and GitHub publication follows the agreed synchronization policy.