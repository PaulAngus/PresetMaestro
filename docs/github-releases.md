# GitHub application releases

Build, validate, sign and package releases locally on Windows and Mac. GitHub Actions is disabled. Run the [local validation checks](windows-macos-development.md#build-and-verify-on-windows) before uploading the resulting archives to GitHub Releases.

Use a clean checkout of the selected release commit and PowerShell 7. Set the
same explicit release version on Windows and macOS; the examples use `$version`.
Use a short checkout path on Windows. Existing restore/build paths are relative
to the checkout and may exceed Windows path limits in deeply nested folders.

```powershell
./Publish-App.ps1 -Version $version
```

The normal Windows publish script creates
`publish/PresetMaestro-<version>-win-x64.zip` and `.zip.sha256`, containing the
standalone EXE and dependency notices. Users extract and run the EXE.

On the signing Mac:

```powershell
./Publish-Mac.ps1 -Version $version -Architecture arm64 -SigningIdentity 'Developer ID Application: YOUR NAME (TEAMID)' -NotaryProfile 'YOUR-KEYCHAIN-PROFILE'
./Publish-Mac.ps1 -Version $version -Architecture x64 -SigningIdentity 'Developer ID Application: YOUR NAME (TEAMID)' -NotaryProfile 'YOUR-KEYCHAIN-PROFILE'
```

Upload `PresetMaestro-<version>-osx-arm64.zip` and
`PresetMaestro-<version>-osx-x64.zip` with their `.zip.sha256` companions from
`publish-macos/<runtime>/<version>/`. Do not use `-SkipArchive` for a release.
The Mac's `ditto` packaging preserves the `.app` permissions and symbolic links.
Users extract and copy the `.app` to Applications. ZIP is the chosen Mac format;
a DMG is not required. A Mac ZIP cross-built on Windows is preparation input,
not a signed public release.

## Windows signing

On the Windows build PC, install the Windows SDK signing tools and Certum card
software, activate/register the approved certificate in `Cert:\CurrentUser\My`,
and connect the reader/card. These tools are not application-user dependencies.
List available certificates without exporting the private key:

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Select-Object Subject, Thumbprint, NotAfter, HasPrivateKey
$env:MBU_WINDOWS_SIGNING_THUMBPRINT = 'YOUR-EXACT-40-CHARACTER-THUMBPRINT'
./Publish-App.ps1 -Version $version -Sign
```

The card software may display a PIN prompt. Enter the PIN there only; never put
it or a private key in scripts, environment variables, Git or chat. SignTool is
discovered on PATH or in the installed Windows SDK. Override its location with
`-SignToolPath 'C:/full/path/to/signtool.exe'` or `MBU_SIGNTOOL_PATH` if necessary.

The publisher signs the final EXE with SHA-256 and an RFC 3161 SHA-256 timestamp
(default `http://time.certum.pl`, configurable with `-TimestampServer`). It checks
SignTool verification, Windows trust, the exact signer and the presence of a
timestamp before ZIP/checksum creation.
Missing certificates, token/PIN problems, timestamp failures and verification
failures stop the script. Upload only the ZIPs/checksums from a successful run.

`-Sign` requires a configured certificate and fails before building if absent.
Supplying a thumbprint (argument or environment) also enables signing. Without
either, the script explicitly reports an unsigned development build. To make an
unsigned development build despite an existing environment setting, pass
`-SigningCertificateThumbprint ''` and omit `-Sign`.

Initial Certum card signing remains pending certificate approval and card
arrival. Once available, publish with `-Sign`, extract each new ZIP and check
`Get-AuthenticodeSignature` reports `Valid`, the expected signer and a nonempty
`TimeStamperCertificate`; verify the ZIP SHA-256 sidecar against `Get-FileHash`.
Then run the required application workflows on the available machines and record
clean Windows 10/11 coverage separately. Simulated signing tests do not qualify
the certificate, token, timestamp service or downloaded application behavior.

References: [Microsoft SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool),
[Certum signing instructions](https://files.certum.eu/documents/manual_en/Signing_with_the_use_of_jarsigner_tool_and_signtool.pdf).

## Upload

Create a draft GitHub Release for an existing tag pointing to the tested commit.
Set `$tag` to that tag and `$archive` to one exact ZIP path:

```powershell
gh release create $tag --repo PaulAngus/PresetMaestro --verify-tag --draft --title $tag --notes-file release-notes.txt
gh release upload $tag $archive "$archive.sha256" --repo PaulAngus/PresetMaestro
```

Create the draft once; repeat the upload command for the other two ZIPs.
Avoid recursive wildcards and `--clobber`: older builds and silently replaced
release files must not enter the distribution. Download the assets, verify their
checksums, extract them, and exercise first launch, settings and the required
MIDI/device workflows before publishing the draft. Record all clean-machine
gaps; a successful package or VM build is not full functional qualification.
GitHub's automatic source archives are not application downloads.

## Credentials

Creating ZIPs requires no developer key. Public Mac releases need a Developer ID
Application certificate/private key and notarization credentials configured
locally in the signing Mac's keychain. Supply only the identity and profile
names to the publish script. Do not send or commit private keys.
The default ad hoc signature is for development testing. Windows signing uses
a separate certificate; an Apple developer key does not sign Windows EXEs.
ZIP compression does not bypass browser or OS security checks.
The publish scripts do not upload automatically; GitHub authentication is needed
on the maintainer machine that uploads the assets.

References: [GitHub release creation](https://cli.github.com/manual/gh_release_create),
[asset upload](https://cli.github.com/manual/gh_release_upload),
[Apple distribution](https://developer.apple.com/developer-id/).
