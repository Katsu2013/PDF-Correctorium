# Code signing policy / コード署名方針

Status: preparation only, 2026-10-28. SignPath Foundation has been selected as the planned signing provider for direct distribution. No application has been submitted through this work, no approval has been received, and no artifact has been signed by SignPath through this work.

GitHubへの直接配布版はSignPath FoundationのOSS向け署名を利用する方針です。以下は申請・連携準備段階の方針であり、署名済み・審査済み・公開済みという案内ではありません。[申請準備と残条件](docs/SignPath-Foundation-Preparation.md)を参照してください。

## Intended signing scope

- Sign the PDF Correctorium direct-distribution MSIX and, if configured and approved, this project's own executable and assemblies.
- Only submit artifacts built from the project's public source by an approved GitHub Actions workflow on GitHub-hosted runners.
- Third-party runtimes, libraries, executables, models and fonts may be included with their applicable licenses. Preserve existing signatures; do not individually re-sign third-party binaries using the project's SignPath access.
- Microsoft Store packages use the Store's signing process. ZIP distribution does not gain automatic installation or updates from this policy.

The MSIX Publisher must exactly match the certificate subject supplied for the approved signing configuration. The Package Name and Publisher for the direct channel must be finalized before its first public signed release. Changes of signing provider or Store/direct-channel migration require a separate identity and data-migration review.

## Proposed team roles

These assignments are a proposal for the repository owner to confirm before application. They do not assert that repository or SignPath permissions have been configured.

| Role | Proposed maintainer | Responsibility |
|---|---|---|
| Authors / committers | [Katsu2013](https://github.com/Katsu2013) | Maintain source and build definitions |
| Reviewers | [Katsu2013](https://github.com/Katsu2013) | Review external contributions and signing-related changes |
| Signing approvers | [Katsu2013](https://github.com/Katsu2013) | Explicitly approve each release signing request |

Use multi-factor authentication for both GitHub and SignPath. Restrict signing credentials to the release workflow, and do not expose them to untrusted pull requests. No private key or signing token belongs in source control, application settings, build logs or chat.

## Release process

1. Build and verify the exact source revision using the repository's versioning and verification policy.
2. Generate an MSIX with the confirmed package identity and update feed, then submit that build artifact for signing.
3. A designated maintainer approves the signing request. Release-signing approval is distinct from end-user update behavior.
4. Verify the returned signature, timestamp, package identity and contents against the submitted package. Publish only the verified signed package.
5. Publish the versioned MSIX first; update the stable App Installer feed only after the package is available. Users with the configured direct-distribution installation then receive updates through Windows App Installer.

If signing, approval or verification fails, do not publish an unsigned replacement or advance the update feed. The current local-certificate packaging script does not yet implement this SignPath handoff.

## Privacy policy

PDF Correctorium processes PDF documents, OCR text, project data and document images locally. Its bundled RapidOCR recognition does not upload document content to an OCR service. The current application source does not implement automatic analytics or crash-report uploads.

Network access can occur when a user opens a web link or the release page, manually checks a configured update feed, or installs a distribution whose updates are managed by Windows App Installer or Microsoft Store. An external browser or Windows performs the applicable link opening or package download. These services can receive normal connection data such as the IP address and requested URL. This does not upload the user's PDF or project to the project's signing service. A web link opened by the user may itself contain information in its URL.

Document content is not sent to SignPath as part of runtime use. The planned signing workflow submits application build artifacts and build-origin metadata, not user documents or private test inputs. Use generated, non-private fixtures for public CI.

External services have their own policies: [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement), [Microsoft Privacy Statement](https://privacy.microsoft.com/privacystatement), and [SignPath privacy information](https://signpath.io/privacy-policy).

## Acknowledgement after approval

The following required acknowledgement is reserved for activation after approval and signing integration. Its presence here as a template must not be presented as evidence of approval:

> Free code signing provided by SignPath.io, certificate by SignPath Foundation.

When activated, link the provider names to [SignPath.io](https://signpath.io/) and [SignPath Foundation](https://signpath.org/), and link this policy from the homepage and download/release pages. The certificate is issued to SignPath Foundation; the exact certificate subject will be taken from the approved configuration rather than guessed from this document.

References: [Foundation conditions](https://signpath.org/terms), [GitHub integration](https://docs.signpath.io/trusted-build-systems/github), [MSIX signing support](https://docs.signpath.io/artifact-configuration/reference).
