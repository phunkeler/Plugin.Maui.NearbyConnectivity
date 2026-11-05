# Azure DevOps: read-only sign-in for CI

This is optional, and it needs admins. The workflow does not sign in by default. Without a sign-in, the Chain row in
CI says `Boards not read`, every other row works, and the Chain row works locally after `az login`.

With it, the `harness scope` workflow signs in to Microsoft Entra with workload identity federation and gets a
short-lived token for each run, so the Chain row reads Azure Boards in CI too. No client secret or personal access
token is stored anywhere, and the harness only reads: one `az rest` GET per work item.

## Who does what

| Step | Who | Where |
|---|---|---|
| 1. Check the repository | A repository admin on GitHub | Repository settings |
| 2. App registration with a federated credential | Someone who can create app registrations in the Entra tenant | Entra admin center |
| 3. Add the app to the Azure DevOps organization | Organization owner or project collection administrator | Organization settings |
| 4. Repository secrets and variables | A repository admin on GitHub | Repository settings |
| 5. Add the sign-in step to the workflow | A developer | `harness-scope.yml` |

## 1. Check the repository

Do this first. Both answers decide what the later steps need.

**The Actions policy must allow `azure/login`.** A blocked action stops the whole workflow from starting, and
`continue-on-error` does not help. The comment then stops updating, so do this before step 5.

```
gh api repos/<org>/<repo>/actions/permissions
gh api repos/<org>/<repo>/actions/permissions/selected-actions
```

If the repository allows selected actions only, add `azure/login@*` in Settings, Actions, General. Both commands need
repository admin rights.

**Read the subject GitHub sends.** Entra needs an exact match.

```
gh api repos/<org>/<repo>/actions/oidc/customization/sub
```

Add `:pull_request` to the end of `sub_claim_prefix` to get the subject. Older repositories send the name-only form,
`repo:<org>/<repo>:pull_request`. Newer, renamed or transferred repositories can send the immutable form,
`repo:<org>@<owner-id>/<repo>@<repo-id>:pull_request`. If you cannot read the setting, a first sign-in that fails with
`AADSTS700213` prints the exact subject in the workflow log.

## 2. App registration

1. Entra admin center, App registrations, New registration. Name it `github-harness-<repo>`. No redirect URI.
2. Note the **Application (client) ID** and the **Directory (tenant) ID**.
3. Certificates & secrets, Federated credentials, Add credential, scenario **Other issuer**. Issuer
   `https://token.actions.githubusercontent.com`, the subject from step 1, audience `api://AzureADTokenExchange`, name
   `harness-pull-request`. Do not add a client secret.
4. Give it no Azure role. Boards does not use one.

## 3. Add the app to the Azure DevOps organization

Organization settings, Users, Add users. Use the **Object ID** from Entra's Enterprise applications page, not the one
from App registrations. Choose the **Stakeholder** access level, which is free. Add the app to a purpose-specific group
inside the project's **Readers** group, and to no other group. Stakeholder access alone can change work items, so
Readers is what keeps it read-only. See Microsoft's guide "Use service principals and managed identities in Azure
DevOps".

Check early that the app can read a Test Case in your project. Stakeholder access is narrower in private projects than
in public ones. If the Chain row says `not found in Boards` for a Test Case that exists, ask the Azure DevOps
administrator which access level the app needs.

## 4. Repository secrets and variables (GitHub)

Repository settings, Secrets and variables, Actions.

| Name | Kind | Value |
|---|---|---|
| `AZURE_CLIENT_ID` | Secret | The app registration's Application (client) ID |
| `AZURE_TENANT_ID` | Secret | The Directory (tenant) ID |
| `ADO_ORG` | Variable | The organization URL, `https://dev.azure.com/<org>` |
| `ADO_PROJECT` | Variable | The project name |

The two IDs identify the app. They are not credentials, and they are secrets here only to keep them out of logs.

The workflow already passes `ADO_ORG` and `ADO_PROJECT` to the harness from these variables. Set them here rather
than only in `harness.json`: the environment wins, so a `harness.json` on any branch cannot point the robot's token
at another organization. The harness also refuses any host but `dev.azure.com` and `*.visualstudio.com`.

`az` is preinstalled on `ubuntu-latest`.

## 5. Add the sign-in step to the workflow

In `.github/workflows/harness-scope.yml`, add `id-token: write` to the job's `permissions`, and add this step after
`Build harness`. A failed sign-in stays a reason on the Chain row.

```yaml
      - name: Sign in to Azure Boards
        if: steps.base.outputs.present == 'true'
        continue-on-error: true
        uses: azure/login@a641126d1b8aa4d1fa005f4f92df94a3a4c4c906 # v3.1.0
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          allow-no-subscriptions: true
```

## Who can use the identity

The `pull_request` subject trusts this repository's pull request workflows, not one workflow file. Anyone who can push
a branch can edit the workflow in a pull request and use the identity. Pull requests from forks get no OIDC token by
default. That is acceptable for an identity that can only read one Boards project, if everyone with push access may
read that project. An identity that can write needs its own app and a stricter subject.

## If it fails

A failed sign-in leaves the job green, and the Chain row carries the reason.

- `no Azure sign-in here`: the sign-in step is missing or failed. Read the step's log.
- `AADSTS700213` or `AADSTS70021: No matching federated identity record found`: the subject does not match, or the
  change has not reached Entra yet. Compare the subject in the log with the credential, and wait a few minutes.
- `startup_failure` on the run: the Actions policy blocks `azure/login`. See step 1.
- `TF400813: The user ... is not authorized`: step 3 is missing.
- `not found in Boards` for a work item that exists: the app is not in the project's Readers group, its access level
  cannot read that item, or `boards.project` names another project.
