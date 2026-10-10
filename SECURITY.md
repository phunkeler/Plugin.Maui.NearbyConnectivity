# Security

## Supported versions

Until 1.0 ships, only the latest preview receives fixes.

| Version | Supported |
| --- | --- |
| Latest `1.0.0-preview.x` | Yes |
| Older previews | No. Upgrade. |

## Reporting a vulnerability

Do not open a public issue. Report privately through GitHub Security Advisories:

<https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/security/advisories/new>

Include the package version, platform, and steps or code that reproduce the problem.

| Step | Target |
| --- | --- |
| Acknowledgement | Within 3 business days |
| Assessment and severity | Within 10 business days |
| Fix or mitigation for a confirmed report | Within 90 days, sooner for anything exploitable in radio range |

You will be credited in the advisory unless you ask not to be.

## What counts

In scope: anything that lets a device in radio range do more than the
[Security](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/security.html) page
promises. For example, an inbound file name escaping the staging directory, a display name that is
unsafe to render, an inbound payload that crashes the receiving process, or two devices that show
matching authentication codes across a relay.

Out of scope: the documented limits of a proximity network. Neither platform authenticates the
remote device, and neither does this plugin. `NearbyConnection.AuthenticationCode` is a comparison
code: two people who read the same six digits to each other have ruled out a machine relaying
between their devices, and nothing more. It does not say who is on the other end, and nothing
enforces the comparison. `NearbyConsentPolicy.AcceptAll` accepts everyone by design; payload
contents are delivered verbatim. Those are design facts, not vulnerabilities. Vulnerabilities in the
underlying Nearby Connections or Multipeer Connectivity frameworks belong with Google or Apple.

## Verifying a release

Every release is built by `release.yml` from `main`, published to nuget.org
through Trusted Publishing (no long-lived API key), and attested with
[build provenance](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/attestations).
Releases are immutable: assets cannot change and the tag cannot move.

nuget.org re-signs the packages it serves, so the attested bytes are the `.nupkg` attached to the
GitHub Release, not the nuget.org download. To verify, once the first release exists:

```bash
gh release download v1.0.0-preview.1 --repo phunkeler/Plugin.Maui.NearbyConnectivity --pattern '*.nupkg'
gh attestation verify Plugin.Maui.NearbyConnectivity.1.0.0-preview.1.nupkg --repo phunkeler/Plugin.Maui.NearbyConnectivity
```
