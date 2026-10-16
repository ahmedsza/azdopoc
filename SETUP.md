# Setup Guide: GitHub, Azure Repos, and Azure Pipelines

This guide sets up Yard / Property Management from a GitHub-hosted source repository, copies the source into Azure Repos, and uses Azure Pipelines to build and deploy the database and website.

The deployment supplied by this repository is **SQL Server plus IIS on a Windows self-hosted agent machine**. Azure DevOps orchestrates delivery; it does not provision an Azure VM, App Service, Azure SQL database, DNS, or certificates. The Windows machine can be a private demo PC or a separately provisioned VM.

> The application has no authentication or authorization. Use fictional data on localhost or a tightly restricted demo network. Do not expose the current application publicly or treat these pipelines as a production-ready hosting solution. SQL Server Developer edition is for development and testing, not production.

## Contents

1. [Architecture and workflow](#1-architecture-and-workflow)
2. [Prerequisites and configuration inventory](#2-prerequisites-and-configuration-inventory)
3. [Verify the application locally](#3-verify-the-application-locally)
4. [Publish to GitHub and import into Azure Repos](#4-publish-to-github-and-import-into-azure-repos)
5. [Keep Azure Repos synchronized with GitHub](#5-keep-azure-repos-synchronized-with-github)
6. [Choose branches and enable build triggers](#6-choose-branches-and-enable-build-triggers)
7. [Prepare the Windows SQL and IIS host](#7-prepare-the-windows-sql-and-iis-host)
8. [Register the self-hosted Azure Pipelines agent](#8-register-the-self-hosted-azure-pipelines-agent)
9. [Configure secrets, approvals, and resource permissions](#9-configure-secrets-approvals-and-resource-permissions)
10. [Configure the project wiki and release notes](#10-configure-the-project-wiki-and-release-notes)
11. [Create and run the pipelines](#11-create-and-run-the-pipelines)
12. [Verify and operate the deployment](#12-verify-and-operate-the-deployment)
13. [Troubleshooting](#13-troubleshooting)
14. [Completion checklist](#14-completion-checklist)

## 1. Architecture and Workflow

```text
GitHub: source repository and development history
    |
    | Initial Azure Repos import; subsequent controlled Git synchronization
    v
Azure Repos Git: pipeline checkout and release source
    |
    +--> azure-pipelines-web --> web artifact (web.zip)
    |                              |
    |                              v
    |                      azure-pipelines-web-deploy
    |                              |
    |                              +--> IIS website on Windows host
    |                              +--> release-notes artifact and project wiki
    |
    +--> azure-pipelines-database --> database artifact (DACPAC and tools)
    |                                   |
    |                                   v
    |                           azure-pipelines-database-deploy
    |                                   |
    |                                   +--> preview artifacts
    |                                   +--> approval --> SQL Server
    |
    +--> azure-pipelines.yml: optional combined build and isolated SQL validation
```

- GitHub hosts the source; pipelines in this guide select **Azure Repos Git**, not GitHub, as their repository provider.
- Importing a GitHub repository is a one-time copy, not a continuous synchronization service.
- Azure Pipelines builds immutable delivery artifacts. Deployment pipelines consume those artifacts without rebuilding them.
- The SQL Database Project is the only schema owner. Do not run EF migrations or create the schema from application startup.
- Database and website deployment are independent. There is no automatic dependency that waits for the database before deploying the website. Coordinate their release order explicitly.
- The application connects directly to SQL Server from ASP.NET Core. Browsers do not connect to SQL Server.

For a first installation, validate the combined CI pipeline, deploy the database, and then deploy the website using the split pipelines.

## 2. Prerequisites and Configuration Inventory

### Accounts and Access

- A GitHub repository and permission to push source changes.
- An Azure DevOps **Services** organization and project with Azure Repos, Pipelines, and Wiki enabled.
- Permission to import/create repositories, create pipelines, manage branch policies, authorize resources, and configure environments and variable groups.
- An organization administrator who can install the release-notes extension and configure agent pools if your account cannot.
- Microsoft-hosted parallel-job capacity for the Ubuntu build jobs. New organizations may need a parallelism grant or paid capacity.
- A Windows deployment machine with administrative setup access, IIS support, SQL connectivity, and a persistent agent service.

Azure DevOps Services does not require an Azure subscription merely to store code and run pipelines. Provisioning Azure-hosted infrastructure separately can require a subscription and incur charges.

### Tools

| Tool | Developer machine | Windows deployment host |
| --- | --- | --- |
| Git | Required for source and synchronization | Required for wiki checkout and publication |
| .NET SDK 10 | Required | Required; database deployment SDK-install tasks are disabled |
| PowerShell 7.2+ (`pwsh`) | Required for database helper | Required for SQL deployment and wiki tasks |
| Windows PowerShell 5.1 | Windows default | Required by existing IIS deployment tasks |
| SQL Server 2019+ | LocalDB or reachable SQL instance | Reachable full SQL instance; LocalDB is unsuitable for this service-hosted demo |
| Docker with Linux containers | Optional for SQL container | Optional if using containerized SQL on a supported host |
| .NET 10 ASP.NET Core Hosting Bundle | Not required for `dotnet run` | Required for IIS hosting |
| `sqlcmd` | Optional, for manual checks and seed data | Optional, for manual SQL checks |

[global.json](global.json) requests SDK `10.0.100` with `latestFeature` roll-forward and no prerelease SDKs. [.config/dotnet-tools.json](.config/dotnet-tools.json) pins SqlPackage to `170.5.96`; restore that local tool rather than substituting an arbitrary global installation.

Download tools from their official sources: [.NET](https://dotnet.microsoft.com/download/dotnet/10.0), [PowerShell](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows), [Git](https://git-scm.com/downloads), and [SQL command-line utilities](https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-utility).

### Names to Decide Before Setup

| Setting | Existing value | Where to configure it |
| --- | --- | --- |
| Release branch | Inconsistent: web `master`, database `main` | Deployment pipeline resource branch filters |
| Azure DevOps project/repository | Choose your own | Azure Repos and Git remote URLs |
| Self-hosted pool | `SelfHosted` | Both split deployment YAML files and agent registration |
| Combined-pipeline SQL pool | `SqlDeploymentAgents` | Root pipeline parameter; override with `SelfHosted` if appropriate |
| Variable group | `PropertyManagement-Sql` | Pipelines > Library |
| Secret variable | `SqlDeploymentConnectionString` | Variable group; mark secret |
| Deployment environment | `property-management-sql` | Pipelines > Environments |
| Web build pipeline name | `azure-pipelines-web` | Must match web deploy `resources.pipelines.source` |
| Database build pipeline name | `azure-pipelines-database` | Must match database deploy `resources.pipelines.source` |
| Wiki repository resource | `coj/coj.wiki` | Web deploy `resources.repositories.name` |
| Wiki branch | `wikiMaster` | Web deploy resource `ref` and publisher `$wikiBranch` |
| IIS site and app pool | `jhbdemo` | Web deploy variables |
| IIS physical directory | `D:\jhbdemo` | Web deploy `sitePath`; choose an existing drive |
| IIS HTTP port | `8080` | Web deploy `sitePort` |
| Demo database | `PropertyManagementDemo` | Connection string; required by optional demo seed |

## 3. Verify the Application Locally

Run commands from the repository root in PowerShell 7. Do not commit connection strings or passwords.

### Restore and Build

```powershell
git --version
pwsh --version
dotnet --list-sdks
dotnet tool restore
dotnet restore PropertyManagement.slnx
dotnet build PropertyManagement.slnx -c Release --no-restore -warnaserror
```

The build should produce both the web application and `database/PropertyManagement.Database/bin/Release/PropertyManagement.Database.dacpac`. The SQL project uses `Microsoft.Build.Sql` and targets SQL Server 2019 (`Sql150`); newer compatible SQL Server engines can host it.

### Choose a Local SQL Target

**Option A: Windows LocalDB.** Install SQL Server Express LocalDB, confirm the `MSSQLLocalDB` instance exists, and start it if needed:

```powershell
sqllocaldb info
sqllocaldb start MSSQLLocalDB
$env:SQL_DEPLOY_CONNECTION_STRING = 'Server=(localdb)\MSSQLLocalDB;Database=PropertyManagementDemo;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
```

**Option B: SQL Server or the supplied SQL container.** Follow section 7 for the container. Set `SQL_DEPLOY_CONNECTION_STRING` privately in the current terminal to a connection string with an explicit server and database. Use a secure input mechanism or your secret manager; do not paste real passwords into committed files or shared command transcripts.

`SQL_DEPLOY_CONNECTION_STRING` configures the deployment helper only. The web application reads `ConnectionStrings:PropertyManagement`. For a non-LocalDB target, configure it separately for this shell:

```powershell
$env:ConnectionStrings__PropertyManagement = $env:SQL_DEPLOY_CONNECTION_STRING
```

### Preview and Publish the Schema

```powershell
$dacpac = 'database/PropertyManagement.Database/bin/Release/PropertyManagement.Database.dacpac'
./scripts/Deploy-Database.ps1 -Action Preview -Dacpac $dacpac
```

Review `deployment-report.xml`, `deployment.sql`, and `plan.json` in `artifacts/database-preview`. Then publish the approved artifact:

```powershell
./scripts/Deploy-Database.ps1 -Action Publish -Dacpac $dacpac -ApprovedPlan artifacts/database-preview/plan.json
```

The helper rejects system databases and verifies the approved target, DACPAC, profile, report, and script. A missing database can be created if the login has permission. The safety settings do not replace plan review or backups.

### Optional Demo Data and Application Startup

For LocalDB:

```powershell
sqlcmd -S '(localdb)\MSSQLLocalDB' -d PropertyManagementDemo -E -i database/PropertyManagement.Database/Seed/DemoData.sql -b
dotnet run --project src/PropertyManagement.Web/PropertyManagement.Web.csproj -c Release --no-build --launch-profile local
```

Open `http://localhost:5180`. To use another port, append `-- --urls http://localhost:5181` to the run command.

For SQL authentication, use `sqlcmd -U <login>` with its interactive password prompt, or a privately configured `SQLCMDPASSWORD`; avoid a literal password in `-P`. With a self-signed demo SQL certificate, the ODBC-based `sqlcmd` may also need `-C`.

The [seed script](database/PropertyManagement.Database/Seed/DemoData.sql) only runs against `PropertyManagementDemo`, inserts fictional records into an empty portfolio, and skips existing portfolios. It is never executed automatically by a deployment pipeline.

### Tests

Unit tests require no SQL Server:

```powershell
dotnet test tests/PropertyManagement.Tests/PropertyManagement.Tests.csproj -c Release --filter "TestCategory=Unit" -warnaserror
```

For the full SQL and HTTP test suite, build the DACPAC first and use a disposable development/test SQL instance:

```powershell
# Omit this assignment to use the tests' default LocalDB connection.
$env:TEST_SQL_CONNECTION_STRING = $env:SQL_DEPLOY_CONNECTION_STRING
dotnet test tests/PropertyManagement.Tests/PropertyManagement.Tests.csproj -c Release -warnaserror
```

Tests replace the catalog with a unique `PropertyManagement_Tests_<guid>` database and delete that generated database afterward. The login needs database creation/deletion permission. Do not point integration tests at a production server.

## 4. Publish to GitHub and Import Into Azure Repos

### Prepare GitHub

If the repository already exists on GitHub, keep its existing remote and history. Otherwise create an empty GitHub repository and push this source using your normal Git tooling. Do not reinitialize or force-push an existing shared repository.

Before publishing, inspect tracked files for passwords, tokens, private data, local backups, and deployed configuration. [.gitignore](.gitignore) excludes build output and `.env`, but **does not exclude every possible backup or secret file**. In particular, inspect `setupsqlcontainer/backup` before adding files. If a secret was previously committed, removing it from the latest revision does not remove it from history; revoke/rotate it and follow your organization's history-remediation procedure.

Use `main` as the canonical branch for a new installation. If the existing GitHub repo uses `master`, decide whether to keep it or migrate it before configuring policies and pipelines. Do not maintain two accidental release branches.

### Import the Repository

1. Open your Azure DevOps project.
2. Go to **Repos > Files**, open the repository selector, and choose **Import repository**.
3. Select Git and enter the GitHub clone URL, such as `https://github.com/<github-owner>/<repository>.git`.
4. For a private GitHub repo, provide the authentication requested by the import UI using a least-privilege credential with repository read access. Enter it directly in that UI, not in this guide, YAML, a clone URL, or chat. Follow organizational SSO requirements.
5. Choose an Azure Repos repository name and start the import.
6. Verify the expected branches, commit history, root YAML files, and source files are present.
7. In repository settings, set the default branch to the chosen release branch.

See [Import a Git repository](https://learn.microsoft.com/azure/devops/repos/git/import-git-repository?view=azure-devops).

### Alternative: Push Into an Empty Azure Repos Repository

If import is unavailable, create an empty Azure Repos Git repository with no initial README, then add a second remote to an existing GitHub clone. Authenticate using Git Credential Manager or another organization-approved method.

```powershell
git remote -v
git remote add azure https://dev.azure.com/<organization>/<project>/_git/<repository>
git fetch origin
git push azure refs/remotes/origin/main:refs/heads/main
```

Push other required branches/tags explicitly after review. This example assumes `origin` is GitHub and its release branch is `main`. Do not use `git push --mirror` or `--force` against a populated Azure Repos repository; these can overwrite or delete refs.

## 5. Keep Azure Repos Synchronized With GitHub

Choose and document who owns synchronization. No GitHub-to-Azure-Repos synchronization job is supplied here, and no GitHub Actions workflow is required for deployment.

### Before Azure Repos Branch Policies Are Enabled

For a one-way copy of the GitHub release branch into an unprotected Azure Repos release branch:

```powershell
git fetch origin
git fetch azure
git log --oneline azure/main..origin/main
git push azure refs/remotes/origin/main:refs/heads/main
```

A normal push refuses a non-fast-forward update. Investigate divergence instead of overriding that safeguard. This explicit remote-ref push avoids accidentally publishing unrelated uncommitted local work.

### After Azure Repos Branch Policies Are Enabled

Do not grant routine policy-bypass rights just to maintain a copy. Push GitHub's reviewed source to an Azure Repos synchronization branch and open an Azure Repos PR into `main`:

```powershell
git fetch origin
git push azure refs/remotes/origin/main:refs/heads/sync/github-main-<unique-id>
```

Use a unique branch name for each synchronization. Run the required Azure Repos build validation, review the change, and merge under the configured policies. A merge or squash may create a different Azure Repos commit ID from GitHub; record the upstream GitHub commit/PR in the synchronization PR for traceability. If exact commit identity is a hard requirement, agree a protected promotion workflow and merge strategy with your repository administrators first.

Keep source/YAML edits in GitHub and synchronize them into Azure Repos. Direct edits in both repositories create divergence. This applies to the pipeline configuration changes described below as well. Commit and synchronize this guide when you are ready; its creation does not itself publish anything.

## 6. Choose Branches and Enable Build Triggers

### What the Checked-In YAML Actually Does

| File | Push-trigger behavior | Other behavior |
| --- | --- | --- |
| [azure-pipelines-web.yml](azure-pipelines-web.yml) | `trigger: none` | Manual or branch-policy builds; unit tests and coverage; produces `web/web.zip` |
| [azure-pipelines-database.yml](azure-pipelines-database.yml) | `trigger: none` | Manual or branch-policy builds; produces `database` artifact |
| [azure-pipelines-web-deploy.yml](azure-pipelines-web-deploy.yml) | `trigger: none` | Completion trigger consumes `azure-pipelines-web` builds from `master` |
| [azure-pipelines-database-deploy.yml](azure-pipelines-database-deploy.yml) | `trigger: none` | Completion trigger consumes `azure-pipelines-database` builds from `main` |
| [azure-pipelines.yml](azure-pipelines.yml) | `trigger: none` | Manual or branch-policy combined validation; optional SQL deployment is guarded to `refs/heads/main` |

**None of these files currently enables ordinary push CI.** Creating the pipeline in Azure DevOps does not change that. The instructions here describe configuration edits you must make; this guide does not modify the YAML for you.

### Recommended New Setup: Use `main` Throughout

1. Change the web deployment pipeline's completion-trigger branch from `master` to `main`.
2. Enable push CI on the web/database build pipelines if automatic builds are wanted. Replace their top-level `trigger: none` with:

   ```yaml
   trigger:
     branches:
       include:
         - main
   ```

3. Enable the same trigger in the root validation pipeline if you want combined SQL/HTTP validation on every `main` push. This runs in addition to the split builds and consumes more agent time.
4. Keep both deployment pipelines' top-level `trigger: none` and their pipeline-resource completion triggers. A code-push trigger on deployment YAML could cause duplicate or unintended deployments.
5. Set each pipeline's **Default branch for manual and scheduled builds** to `refs/heads/main`, including deployment pipelines. Azure DevOps uses this setting when evaluating pipeline-completion trigger definitions.
6. Save the edits in GitHub and synchronize them to Azure Repos before testing the trigger chain.

To retain manual-only builds, leave the build YAML unchanged and queue the build pipelines manually from the correct branch. Successful builds can still trigger the deployment pipelines through their resource triggers. Set up protected deployment resources before the first successful build.

If keeping `master`, update the database completion filter, branch policies, environment branch checks, defaults, and the root pipeline's explicit `refs/heads/main` conditions consistently.

### Azure Repos Pull-Request Validation

Azure Repos Git PR validation is configured through branch policies, not YAML `pr:` triggers:

1. Go to **Repos > Branches > main > Branch policies**.
2. Add a required, automatic **Build validation** policy for the combined validation pipeline once it exists.
3. Configure reviewers, resolution of comments, and other organizational policies as needed.
4. Keep normal PR validation at `deployDatabase=false` and deny untrusted PRs access to deployment secrets, agents, and environments.

GitHub PR checks do not automatically become Azure Repos branch policies. The combined pipeline provides broader validation than the split build pipelines: database and HTTP integration tests are not run by the database-only build, and web-only tests are introductory arithmetic examples, not broad business-logic coverage.

## 7. Prepare the Windows SQL and IIS Host

The split deployment pipelines use Windows paths, IIS tasks, and local IIS management. Install the agent **on the IIS machine**. Registering an environment does not select a remote IIS machine or install software on it.

### SQL Server

Use an existing SQL Server 2019+ instance reachable by the agent and application, or create a development SQL container. A hosted Ubuntu agent cannot access a developer PC's LocalDB.

For a Windows desktop demo with Docker Desktop installed, running, and switched to Linux containers:

```powershell
./setupsqlcontainer/Setup-SqlContainer.ps1
docker ps --filter name=propertymanagement-sql
docker logs --tail 50 propertymanagement-sql
Test-NetConnection localhost -Port 1433
```

The script prompts securely for the `sa` password and starts SQL Server **2022 Developer** with:

| Resource | Value |
| --- | --- |
| Container | `propertymanagement-sql` |
| Image | `mcr.microsoft.com/mssql/server:2022-latest` |
| Host/container SQL port | `1433:1433` |
| Persistent Docker volume | `propertymanagement-sql-data` |
| Backup directory mount | `setupsqlcontainer/backup` to `/var/opt/mssql/backup` |
| Restart policy | `unless-stopped` |

The helper is not idempotent: if the named container already exists, inspect it and use `docker start propertymanagement-sql` rather than rerunning setup blindly. Reusing a database volume does not reset its existing SQL login passwords. A running container or open TCP port alone does not prove SQL login readiness; wait for SQL startup and verify a query.

The script publishes SQL port 1433 on all host interfaces. Restrict access with host/network firewall rules. Do not expose it to the internet. For a localhost-only demo, review changing the script's port mapping to `127.0.0.1:1433:1433` before creating the container.

Docker Desktop is not the deployment choice for Windows Server. Use a supported SQL Server installation or a separate supported Linux container host there. If SQL is on another host, replace `localhost` in connection strings with the reachable SQL hostname.

Example connection-string shape for a private local demo, entered directly into the Azure DevOps secret variable:

```text
Server=localhost,1433;Database=PropertyManagementDemo;User ID=<SQL_LOGIN>;Password=<SQL_PASSWORD>;Encrypt=True;TrustServerCertificate=True;
```

Replace placeholders with real values privately. Prefer separate schema-deployment and application logins for shared environments. The existing web pipeline deliberately reuses the deployment secret, so separating identities requires a reviewed pipeline/configuration change. Do not use `sa` as a shared application identity. For shared systems use a trusted SQL certificate and certificate validation rather than `TrustServerCertificate=True`.

### IIS and ASP.NET Core

Have an administrator perform these setup actions on the deployment machine:

1. Enable IIS, its management tools, and the Windows components needed for local IIS management. On Windows Server, install the Web Server role and management tools; on Windows desktop, use **Turn Windows features on or off**.
2. Install the .NET 10 ASP.NET Core Hosting Bundle **after IIS**. If it was installed first, repair/reinstall it after enabling IIS. Restart IIS/WAS or reboot as required by the installer.
3. Install the .NET 10 SDK, PowerShell 7.2+, and Git separately. The Hosting Bundle alone is not the SDK required for SQL deployment tools.
4. Ensure Windows PowerShell and the IIS management tooling are available to the agent service identity.
5. Ensure `D:` exists, or change `sitePath` in the web deploy YAML to a suitable directory such as `C:\Sites\jhbdemo`.
6. Reserve a free HTTP port, default `8080`. The pipeline creates/updates the site and an Integrated, No Managed Code app pool under `ApplicationPoolIdentity`.
7. Configure `ASPNETCORE_ENVIRONMENT=Production` for the IIS worker process, or ensure no machine/app-pool setting changes it to Development. Recycle the pool after environment changes. The local launch profile is not used by IIS.
8. Restart the Azure Pipelines agent service after installing tools or changing its machine environment/PATH.

See [Host ASP.NET Core on IIS](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0).

### Hostnames and Network Access

[appsettings.json](src/PropertyManagement.Web/appsettings.json) permits only `localhost`, `127.0.0.1`, and `[::1]`. Localhost deployment verification works with those defaults. To browse from another machine:

- Configure DNS or use the actual reachable Windows hostname; `jhbdemo` is an IIS site name, not automatic DNS registration.
- Set `AllowedHosts` to the specific approved hostnames, for example through the IIS app-pool environment, and recycle the pool. Make this durable rather than editing deployed JSON after every release, because deployment recopies that file.
- Permit inbound TCP 8080 only from the intended private clients, if required. Keep SQL access restricted independently.
- Configure real HTTPS bindings/certificates and application authentication before any wider use. The supplied YAML only creates an HTTP binding.

## 8. Register the Self-Hosted Azure Pipelines Agent

1. In **Organization settings > Agent pools**, create or select `SelfHosted` and make it available to the project.
2. Open **New agent**, select Windows x64, and follow the current download/configuration instructions. Extract it to a dedicated directory such as `C:\azagent`, not the application directory or repository clone.
3. Run the agent configuration interactively on the deployment machine, using your organization URL, for example `https://dev.azure.com/<organization>`, pool `SelfHosted`, and a unique agent name.
4. Use an organization-approved authentication method. If registration requires a PAT, enter it directly in the agent configuration prompt with only the needed agent-registration scope. Do not save it in YAML or a command transcript. Runtime wiki publication uses `System.AccessToken`, not this registration credential.
5. Configure the agent to run as a Windows service under a dedicated identity. The identity must be able to manage the local IIS site/app pool and write the deployment directory. The supplied demo IIS tasks commonly require local administrative rights; use a dedicated host, restrict pool access, and review privileges before shared use.
6. Confirm the agent is online and sees `dotnet`, `pwsh`, and `git`. Verify tools as the service account, not just as your desktop user.
7. Keep this pool dedicated to trusted deployment jobs. Do not run untrusted PR code on a privileged agent that can alter IIS or access persistent SQL credentials.

If the pool contains agents on multiple machines, the job can land on any eligible agent. For this local-IIS workflow, use a dedicated pool containing the intended host or add explicit agent demands in a reviewed YAML change. Sharing the environment name does not pin jobs to a host. Ensure every eligible SQL deployment agent has consistent target connectivity/tooling.

Allow outbound HTTPS access to Azure DevOps, artifact storage, NuGet, the .NET distribution endpoints, and the extension's Azure DevOps API calls. Hosted SQL validation also needs Microsoft Container Registry. Follow the organization's proxy and TLS policy; do not disable TLS validation to work around connectivity failures.

See [Windows self-hosted agents](https://learn.microsoft.com/azure/devops/pipelines/agents/windows-agent?view=azure-devops).

## 9. Configure Secrets, Approvals, and Resource Permissions

### Variable Group

1. Go to **Pipelines > Library > + Variable group**.
2. Name it `PropertyManagement-Sql`.
3. Add `SqlDeploymentConnectionString`, enter the complete target connection string, and mark it **secret** using the lock control.
4. Save and authorize only the intended database/web deployment pipelines. Authorize the combined pipeline only if using its optional shared SQL deployment.
5. Restrict who can edit the group and its pipeline permissions. Do not enable open access for all pipelines.

For integrated SQL authentication, SQL sees the agent service identity during schema deployment. IIS normally uses the app pool identity, which is a different principal; the same integrated connection string does not automatically grant both access. Provision and test both identities explicitly.

### Deployment Environment

Create `property-management-sql` under **Pipelines > Environments** before enabling deployment. Use an empty environment for tracking if the UI asks for a resource type; the YAML already chooses the agent pool. Configure:

- **Approval** by an appropriate reviewer, preferably not the requester.
- **Branch control** allowing the selected protected release branch, normally `refs/heads/main`.
- **Exclusive lock** so jobs using this environment serialize. The YAML's `lockBehavior: sequential` only controls an existing lock check; it does not create one.
- Restricted environment pipeline permissions and administrator membership.

Both split deployment pipelines share this environment. Their Deploy stages serialize, but this does not guarantee database-before-web ordering and does not serialize the database Preview stage. Avoid unrelated schema changes between preview and publish.

Environment approval protects the Deploy stage, **not** the preceding database Preview job, which already connects to SQL using the secret. Protect the variable group and privileged agent pool as well; YAML completion branch filters are not authorization boundaries. Configure protected-resource checks/permissions appropriate to your organization, and do not manually select untrusted artifact runs.

### SQL Permissions and Backups

The schema-deployment identity needs DacFx metadata visibility and permission for the reviewed DDL; creating a missing database additionally needs server-level permission. Integration-test database creation/deletion rights are separate and should exist only on a disposable test server.

Take and verify an appropriate backup/recovery path before changing a shared database. A Docker volume and a mounted backup directory do not constitute a backup job. No backup scheduler or restore procedure is provisioned by this repository.

## 10. Configure the Project Wiki and Release Notes

The existing website deployment expects wiki checkout to succeed before changing IIS. Wiki publication is not optional in that YAML.

### Install the Extension and Provision the Wiki

1. Install Richard Fennell's **Generate Release Notes (Crossplatform)** extension from the Visual Studio Marketplace into the Azure DevOps organization. The YAML references `XplatGenerateReleaseNotes@4`.
2. Open **Overview > Wiki** and create/provision the **project wiki**. Create an initial page so its repository and branch exist.
3. Obtain the wiki clone URL and branch from the wiki UI. A project wiki commonly uses `<project>.wiki` and `wikiMaster`; verify rather than assuming.
4. Edit the web deploy repository resource from `name: coj/coj.wiki` to `<your-project>/<your-project>.wiki` (or the actual provisioned wiki repository name).
5. If the wiki branch differs, update both `ref: refs/heads/wikiMaster` and the publisher's `$wikiBranch = 'wikiMaster'`.
6. Synchronize the updated YAML from GitHub to Azure Repos.

Use the provisioned project wiki for this workflow. A code-published wiki can have a different repository/branch layout and requires corresponding reviewed changes.

### Build Service Permissions

Identify the runtime identity from **Organization settings / Project settings > Pipelines > Settings**:

| Job authorization scope | Typical identity |
| --- | --- |
| Current project scope enabled | `<project> Build Service (<organization>)` |
| Collection scope | `Project Collection Build Service (<organization>)` |

Do not use the project's GUID as the account name. The identity may become visible only after a first pipeline run. Grant it:

- Read access to source repositories, builds, PR metadata, and work items used by release notes, subject to normal project security.
- **Read** and **Contribute** on the wiki through **Overview > Wiki > wiki menu > Wiki security**.
- Access to any pipeline resource/artifact explicitly requested by the deployment pipeline.

Authorize the `projectWiki` repository resource for the web deploy pipeline through the run's **This pipeline needs permission to access a resource > View > Permit** prompt, or the resource's pipeline permissions UI. Repository security permissions and pipeline resource authorization are separate requirements.

Keep **Protect access to repositories in YAML pipelines** enabled. The explicit `checkout: projectWiki` authorizes this repository for the job without opening access to unrelated repositories. The publisher receives `System.AccessToken` through its task environment, uses bearer authentication, and does not persist checkout credentials. Do not add PAT fallbacks or weaken repository protection to address authorization errors.

After a successful smoke test, the pipeline publishes `release-notes/release-notes.md` and adds a child page under **Web Deployment Release Notes**. It compares the consumed `webBuild` with the previous successful `Deploy` stage; the first deployment establishes the baseline. GitHub-only PR/work-item associations are not automatically imported as Azure Repos PRs or Azure Boards links, so some release-note sections can legitimately be empty.

## 11. Create and Run the Pipelines

### Create Definitions From Azure Repos YAML

For each definition, choose **Pipelines > New pipeline > Azure Repos Git > your repository > Existing Azure Pipelines YAML file**, select the intended branch and file, then save. Avoid an initial deployment run until resource protections are configured.

| Pipeline definition name | YAML path | Purpose |
| --- | --- | --- |
| `property-management-ci` | `/azure-pipelines.yml` | Full build plus isolated SQL/HTTP integration validation |
| `azure-pipelines-database` | `/azure-pipelines-database.yml` | Package schema and deployment tools |
| `azure-pipelines-database-deploy` | `/azure-pipelines-database-deploy.yml` | Preview, approve, and publish database |
| `azure-pipelines-web` | `/azure-pipelines-web.yml` | Build, unit tests/coverage, and `web.zip` |
| `azure-pipelines-web-deploy` | `/azure-pipelines-web-deploy.yml` | Deploy IIS and publish release notes |

The combined pipeline name is arbitrary because the split deployments do not reference it. The two split build names must match the `source` values in their deployment YAML; update those references if renaming definitions. Keep these pipelines in the same Azure DevOps project for this setup.

For each definition, check the saved YAML path and default branch. Authorize resource access only for the intended pipeline. If a manual deployment asks for pipeline resources, use the **Resources** selector to choose the exact successful upstream build and verify its branch and commit. Do not assume a manual deployment always selects the desired artifact version.

### First Run: Full Validation

1. Queue `property-management-ci` on `main` with `deployDatabase=false`.
2. Confirm Build produces `database` and `web` artifacts.
3. Confirm ValidateSql starts its isolated SQL Server 2019 container, deploys the downloaded DACPAC, runs the tests, publishes test results and `sql-ci-reports`, and removes the container.
4. Inspect logs/results before configuring the pipeline as a required branch-policy build.

This job generates a masked per-run SQL password and needs no persistent SQL variable group. Its `web` artifact is a published directory, **not** the `web.zip` contract expected by the split web deploy pipeline; use `azure-pipelines-web` as that deployment's producer.

### First Run: Database Delivery

1. Queue `azure-pipelines-database` from the release branch.
2. Inspect its `database` artifact: DACPAC, `artifact.json`, safe profile, deployment helper, tool manifest, and SDK configuration.
3. After success, confirm `azure-pipelines-database-deploy` starts via completion trigger. If not, verify the filter, pipeline source name, and default branch, or queue it manually with the correct resource version.
4. Its Preview stage runs on `SelfHosted`, restores the pinned tool, verifies the DACPAC SHA-256, and publishes `sql-target-plan`.
5. Before approving Deploy, inspect `deployment-report.xml`, `deployment.sql`, and `plan.json` in that artifact. Confirm the server/database and expected changes.
6. Approve the environment check. Publish re-verifies the artifact and plan, deploys without rebuilding, and publishes `sql-published-plan`.
7. Seed fictional demo data manually if required. Deployment never seeds it.

If the approved plan has changed, investigate, generate a fresh preview, and obtain approval again. Do not bypass plan checking to make the release proceed.

### First Run: Website Delivery

1. Confirm the schema has been deployed and the SQL connection secret is correct.
2. Queue `azure-pipelines-web` on the release branch. Verify its Tests and Code Coverage tabs and its `web` artifact containing `web.zip`.
3. Confirm the completion trigger starts `azure-pipelines-web-deploy`, or queue it manually with the intended `webBuild` resource.
4. Permit the wiki resource if requested, review the selected artifact, and approve the protected environment.
5. The deployment checks out the wiki, extracts the ZIP, creates/updates IIS, stops the app pool, copies files, writes the database connection into deployed `appsettings.json`, and starts the pool.
6. Verify `http://localhost:8080/` on the host, then check the release-notes artifact and wiki page.

The deployed connection string is plaintext on disk. The pipeline grants the app pool read access, but you must also restrict inherited directory permissions to the required administrators, deployment identity, and app pool identity. Never upload the deployed configuration as an artifact or commit it to Git.

### Alternative: Combined Pipeline SQL Deployment

The root pipeline can optionally deploy SQL after full validation. Use this **instead of**, not concurrently with, split SQL delivery for the same release:

- Run from trusted `main` with `deployDatabase=true`.
- Set `deploymentPool=SelfHosted` to reuse the configured host, or provision its default `SqlDeploymentAgents` pool.
- Keep `deploymentEnvironment=property-management-sql` and `deploymentVariableGroup=PropertyManagement-Sql`, unless you deliberately configured different protected resources.
- Review `sql-target-plan` after PlanDeployment, approve DeployDatabase, and inspect `sql-published-plan`.

This option does not deploy IIS. The guarded stages will not deploy from a PR or a branch other than the explicitly allowed branch.

## 12. Verify and Operate the Deployment

### Acceptance Checks

- The selected upstream build branch and commit match the intended release.
- SQL preview/publish artifacts identify the intended server/database and DACPAC.
- The database contains `Properties`, `Tenants`, `Leases`, `RentPayments`, and the modeled stored procedures.
- IIS site/app pool exist, the pool is started, and the site uses the intended path and binding.
- `http://localhost:8080/` renders successfully; try properties, tenants, leases, and the rent ledger using fictional data.
- Test any approved remote hostname separately, including `AllowedHosts`, DNS, and firewall behavior.
- Tests/coverage artifacts, deployment history, release notes, and wiki updates are available to the expected readers.

**Do not use a green web deployment run as the sole acceptance signal.** In the current YAML, the HTTP smoke test emits a warning after all attempts fail rather than failing the task. Release-note tasks are skipped unless it records success. Inspect the smoke-test output and verify the site yourself; making smoke-test failure block deployment requires a separate YAML change.

### Subsequent Releases

Merge source in GitHub, synchronize into Azure Repos under the chosen branch policy, validate, and promote reviewed artifacts. Coordinate compatible database-before-web changes explicitly. Keep permissions on production-like resources limited to trusted maintainers.

The web deployment stops the app pool, so expect downtime. It copies files over the destination rather than performing a clean/atomic replacement; obsolete files can remain. Keep backups and review package/file changes when planning recovery. It also attempts to restart the pool even if an earlier deployment task fails, which does not guarantee a valid application was restored.

### Recovery and Retention

- Retain the successful build artifacts and source commit needed for recovery; configure Azure Pipelines retention accordingly.
- Re-deploying a retained earlier web build is possible by explicitly selecting its resource version, but first verify database compatibility and any stale destination files.
- An old DACPAC is not an automatic database rollback. Use a reviewed forward fix, expand/contract change, or tested database restore plan as appropriate.
- Back up SQL data independently. Neither pipeline artifacts nor Docker volumes replace tested backups.
- If a secret leaks, rotate it, update the variable group, redeploy the application configuration, and remove/remediate exposed copies under organizational policy.

## 13. Troubleshooting

| Symptom | Check / action |
| --- | --- |
| GitHub changes never reach Azure Pipelines | Import is one-time. Synchronize to Azure Repos and verify the Azure Repos release branch contains the commit. |
| A push does not start a build | All supplied YAML initially uses `trigger: none`. Enable the intended build triggers or run manually; also verify the committed YAML branch. |
| Successful build does not start deployment | Check exact build definition `source`, completion filter (`master` versus `main`), pipeline default branch, pipeline state, and resource authorization. |
| PR does not run validation | Configure Azure Repos Build validation branch policy; YAML `pr:` is not the Azure Repos mechanism. |
| Hosted build waits for an agent | Check Microsoft-hosted parallelism entitlement, available concurrent jobs, and organization restrictions. |
| Deployment waits for an agent | Verify pool name, project/pipeline pool permissions, online Windows agent, and tool demands. |
| `pwsh`, SDK, or Git missing in agent job | Install machine-wide/on the service-account path and restart the agent service. SQL deploy's SDK installer tasks are disabled. |
| Wrong host receives IIS deployment | Environment does not route to a machine. Restrict pool membership or add reviewed agent demands. |
| Resource authorization prompt | Authorize only the intended variable group, pool, environment, wiki repo, or upstream pipeline through its resource permissions. |
| Unknown `XplatGenerateReleaseNotes` task | Install/enable the release-notes extension in the correct Azure DevOps organization. |
| Wiki checkout or push returns TF401019/403 | Verify wiki/project/branch, runtime Build Service identity, wiki Read/Contribute, and explicit pipeline repository authorization. Do not disable repository protection. |
| Wiki release-note sections are empty | First-run baseline, missing metadata permissions, or changes lacking Azure Repos PR/Azure Boards associations can explain empty sections. |
| SQL connection refused or timeout | Check SQL startup, host/port, firewall, service-account network access, and whether `localhost` points at the intended machine. |
| SQL login or integrated authentication fails | Validate SQL login/database access; integrated authentication uses the agent or IIS process identity, not necessarily your desktop account. |
| TLS certificate validation fails | Install/trust the proper SQL certificate and hostname. Use `TrustServerCertificate=True` only for the explicitly accepted local demo case. |
| Container name/port already in use | Inspect the existing container/listener. Start the known container or resolve the conflict; do not delete its data volume as a shortcut. |
| Container password appears unchanged | Existing SQL data volumes retain the configured login password; changing the setup input does not reset it. |
| Approved deployment changed | Target state, artifact, profile, or generated comparison changed. Review a newly generated plan and approve again. |
| IIS HTTP 500.30/500.31 or module errors | Check the .NET 10 Hosting Bundle, IIS installation order, Windows Application event log, runtime availability, connection configuration, and application permissions. |
| HTTP 400 from a remote hostname | Add the approved hostname to durable `AllowedHosts` configuration and recycle the app pool. |
| HTTP 500 after app starts | Verify schema was deployed, SQL is reachable from IIS, runtime identity is authorized, and deployed connection string is configured. |
| Green deploy but site unavailable/no release notes | Inspect the current warning-only smoke-test task and test the site manually; it may have skipped release-note generation. |
| NuGet restore fails behind a proxy | Check approved feeds, proxy, certificates, and outbound access. See [README.md](README.md) for an optional process-scoped feed override; do not commit private credentials or disable TLS. |

## 14. Completion Checklist

- [ ] GitHub source is published without secrets, database backups, or deployed configuration.
- [ ] Azure Repos contains the intended history/release branch and a synchronization owner/process is agreed.
- [ ] Branch names, pipeline default branches, resource filters, policies, and environment checks agree.
- [ ] Build triggers are deliberately enabled or the manual-build workflow is documented.
- [ ] The Windows host has IIS, the .NET 10 Hosting Bundle/SDK, PowerShell 7, Git, SQL connectivity, and an online service agent.
- [ ] Deployment pool access is restricted; jobs run only on the intended host.
- [ ] The secret variable group is configured and authorized only for trusted delivery pipelines.
- [ ] Environment approval, branch control, and exclusive lock checks are configured outside YAML.
- [ ] The project wiki exists, its resource/branch are updated, the extension is installed, and wiki permissions/resource authorization are complete.
- [ ] Full isolated validation and split web unit-test/coverage builds succeed.
- [ ] The target database plan was reviewed and its exact DACPAC published successfully.
- [ ] IIS responds successfully with the intended database, and wiki/release-note publication works.
- [ ] Network restrictions, file permissions, backups, artifact retention, and recovery responsibilities are recorded.
- [ ] Any shared/public use is blocked until authentication, authorization, HTTPS, and operational safeguards are implemented.