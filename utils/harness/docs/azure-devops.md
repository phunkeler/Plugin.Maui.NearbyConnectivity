# Azure DevOps: read-only sign-in for CI (stage 2)

Day one does not need this. Without it, the Chain row in CI says `Boards not read`, and it works locally after
`az login`.

With it, the `harness scope` workflow signs in to Microsoft Entra with workload identity federation, gets a one-hour
token per run, and the Chain row reads Azure Boards in CI too. No personal access token is stored anywhere, and the
harness only reads: one `az rest` GET per work item.

## Who does what

| Step | Who | Where |
|---|---|---|
| 1. App registration with a federated credential | Someone who can create app registrations in the Entra tenant | Entra admin center |
| 2. Add the app to the Azure DevOps organization | Organization owner or project collection administrator | Organization settings |
| 3. Repository variables | A repository admin on GitHub | Repository settings |
| 4. Two workflow steps | Whoever owns `.github/workflows/harness-scope.yml` | A pull request |

## 1. App registration

1. Entra admin center, App registrations, New registration. Name it `github-harness-<repo>`. No redirect URI.
2. Note the **Application (client) ID** and the **Directory (tenant) ID**.
3. Certificates & secrets, Federated credentials, Add credential, scenario **GitHub Actions deploying Azure resources**.
   Organization `<github org>`, Repository `<repo>`, Entity type **Pull request**. Name `harness-pull-request`.
   No client secret is created. Nothing expires.

## 2. Add the app to the Azure DevOps organization

Organization settings, Users, Add users. Search the app registration by name, access level **Basic** or
**Stakeholder**, and add it to the project's **Readers** group. Readers can read work items and their links, which is
all the harness does. See Microsoft's guide "Use service principals and managed identities in Azure DevOps".

## 3. Repository variables (GitHub)

Repository settings, Secrets and variables, Actions, **Variables** (none of these is secret):

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` | The app registration's Application (client) ID |
| `AZURE_TENANT_ID` | The Directory (tenant) ID |
| `ADO_ORG` | The organization URL, `https://dev.azure.com/<org>` |
| `ADO_PROJECT` | The project name |

The workflow already passes `ADO_ORG` and `ADO_PROJECT` to the harness from these variables. Set them here rather
than only in `harness.json`: the environment wins, so a `harness.json` on any branch cannot point the robot's token
at another organization. The harness also refuses any host but `dev.azure.com` and `*.visualstudio.com`.

## 4. Workflow steps

In `harness-scope.yml`, add `id-token: write` to the job's permissions, and this step before "Change scope report":

```yaml
      - uses: azure/login@a641126d1b8aa4d1fa005f4f92df94a3a4c4c906 # v3.1.0
        if: vars.AZURE_CLIENT_ID != ''
        with: { client-id: "${{ vars.AZURE_CLIENT_ID }}", tenant-id: "${{ vars.AZURE_TENANT_ID }}", allow-no-subscriptions: true }
```

`az` is preinstalled on `ubuntu-latest`. Until the variables exist the step is skipped and the Chain row says
`Boards not read`, so the order of the steps above does not matter.

## If it fails

The report itself never fails; the Chain row carries the reason.

- `AADSTS70021: No matching federated identity record found`: the subject in the federated credential does not match.
  The workflow runs on pull requests, so the credential needs the **Pull request** entity.
- `TF400813: The user ... is not authorized`: step 2 is missing.
- `not found in Boards` for a work item that exists: the app is not in the project's Readers group, or `boards.project`
  names another project.
