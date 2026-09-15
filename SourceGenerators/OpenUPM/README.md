# Initial Source Generator registration on OpenUPM

This directory prepares the add-on's initial registration. It does not submit
or publish it. The base package already uses Git-tag tracking; leave that
registration unchanged.

## Registration

1. Follow OpenUPM's [package submission instructions](https://openupm.com/docs/adding-upm-package.html).
2. Use [jp.rhycol.openapicodegen.source-generator.yml](jp.rhycol.openapicodegen.source-generator.yml)
   as the registration data. In an OpenUPM repository PR, its destination is
   `data/packages/jp.rhycol.openapicodegen.source-generator.yml`.
3. Confirm the name/repository, `trackingMode: githubRelease`, asset prefix
   `jp.rhycol.openapicodegen.source-generator-`, and `minVersion: 0.5.0`.
4. Wait for OpenUPM to accept and reflect the registration before the first
   production release. Registration alone does not publish version `0.5.0`.

The initial release must attach
`jp.rhycol.openapicodegen.source-generator-0.5.0.tgz`. OpenUPM distributes that
prebuilt package rather than building the analyzer. See the official
[GitHub Release asset specification](https://openupm.com/docs/adding-upm-package.html#publishing-from-a-github-release-asset).

## First publication check

Follow [RELEASE.md](../../RELEASE.md). The tag-ref follow-up uses the
[official OIDC action](https://openupm.com/docs/github-action-publish) to check
base publication before add-on publication, then compares the registry
tarball with the verified Release asset. No OpenUPM Secret is required.

Keep the accepted registration, first Release, follow-up Actions run and byte
comparison result as publication evidence. Missing registration or mismatched
bytes is a failure. Dry-run results alone do not prove production CD success.
