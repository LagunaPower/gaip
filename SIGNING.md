# Code signing policy

G@IP is an open source project distributed under the MIT License.

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Scope

The Windows release executables and MSI installer produced by the GitHub Actions release workflow are intended to be Authenticode-signed through SignPath. A single trimmed Windows Release executable is built from this public repository on GitHub-hosted runners before it is submitted for signing. The MSI is built from that signed Release executable and is then submitted for its own Authenticode signature.

Unsigned development builds and local builds are not covered by this policy.

## Roles

- Committer and reviewer: Yannick D. ([LagunaPower](https://github.com/LagunaPower)), repository owner and maintainer.
- Approver for release signing requests: Yannick D. ([LagunaPower](https://github.com/LagunaPower)).

Changes contributed by other people are reviewed by the maintainer before they are merged.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

G@IP can access a filesystem location explicitly configured by the user, including a network share used for shared-mode data. It does not include telemetry or an application account service.

## Release signing

Release signing is performed only from the repository's tag-triggered GitHub Actions workflow. The workflow builds and tests the source before submitting the Windows executables to SignPath.

The SignPath integration is enabled only after the SignPath Foundation project has been approved and the required GitHub repository secret and variables have been configured. Once enabled, a signing failure stops the Windows release job; the workflow does not silently substitute an unsigned executable.

The SignPath artifact configurations are stored in:

- `.signpath/artifact-configurations/windows-executables.xml`
- `.signpath/artifact-configurations/windows-msi.xml`

Release tags use the `vMAJOR.MINOR.PATCH` form. The version embedded in the Windows executable is derived from the tag and is checked by the SignPath artifact configuration.

## Verification

On Windows, the Authenticode signature of an extracted release executable can be inspected with PowerShell:

```powershell
Get-AuthenticodeSignature .\GAIP.exe | Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

For a signed public release, `Status` must be `Valid`.
