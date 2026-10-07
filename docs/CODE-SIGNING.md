# Code signing (Azure Artifact Signing)

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

WO-157. Windows Security, other antivirus programs and **Smart App Control** judge a program by its publisher
signature. Smart App Control has **no exclusions**: an unsigned program it blocks stays blocked, whatever the player
allows. Signing every shipped exe and DLL, and Setup itself, is the real fix for "A MOD FILE WAS REMOVED" and "Windows
blocked a file the mod needs". The mod and its installer never add exclusions and never change Windows' security
settings.

`tools\Build-Installer.ps1` signs when the settings below are present, and builds unsigned (said in the build log,
`release\BUILD-<version>.log`) when none are. Some set and some missing stops the build: a half-configured signing never
quietly ships unsigned.

## What gets signed

1. After the payload is published and its tests ran: every `.exe` and `.dll` in `release\KCDMP` (and `MasterServer\`)
   that has **no valid signature yet** — the mod's own builds and unsigned third-party DLLs. Microsoft's runtime files keep
   their own signatures.
2. Setup (`KingdomComeTogether-Setup-<version>.exe`) and the uninstaller inside it, through Inno Setup's `SignTool`.
3. Every file is then checked: a valid signature with a timestamp, or the build stops.

The install manifest is written **after** signing, so it describes the signed bytes (the launcher and Setup compare
against it).

## The maintainer's setup (once)

1. **The account.** In the Azure portal: an *Artifact Signing* account and its identity validation (*Individual*:
   validating needs the role *Artifact Signing Identity Verifier*; Microsoft takes the identity from the subscription's
   billing account, which must be of type *Individual*, and at the time of writing individuals must be in the United
   States or Canada), then a **certificate profile** of type *Public Trust* with that identity. Note the account's **endpoint** (it names its region, for example
   `https://<region>.codesigning.azure.net/`), the **account name** and the **profile name**.
2. **Your sign-in.** Give your Azure identity the role *Artifact Signing Certificate Profile Signer* on the account. On the
   build machine sign in with the Azure CLI (`az login`), or set a service principal in `AZURE_TENANT_ID`,
   `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` (the dlib uses Azure's default credential chain). Those values are never
   read or stored by the build.
3. **The tools.**
   - `signtool.exe` (x64) from the Windows SDK (found by itself under `Windows Kits\10\bin\<version>\x64`).
   - The *Microsoft.ArtifactSigning.Client* package (nuget.org): its `bin\x64\Azure.CodeSigning.Dlib.dll`. The dlib's
     architecture must match signtool's (both x64).
4. **The settings** — in the environment, or in `tools\signing.local.json` (**git-ignored, never committed**):

| Environment variable | `signing.local.json` key | What |
|---|---|---|
| `KCDMP_SIGN_ENDPOINT` | `Endpoint` | the account's endpoint (its own region) |
| `KCDMP_SIGN_ACCOUNT` | `Account` | the Artifact Signing account name |
| `KCDMP_SIGN_PROFILE` | `Profile` | the certificate profile name |
| `KCDMP_SIGN_DLIB` | `Dlib` | full path of `Azure.CodeSigning.Dlib.dll` (x64) |
| `KCDMP_SIGN_SIGNTOOL` | `SignTool` | optional: full path of `signtool.exe` |
| `KCDMP_SIGN_TIMESTAMP` | `Timestamp` | optional: default `http://timestamp.acs.microsoft.com` |

Example `tools\signing.local.json` (placeholders):

```json
{
  "Endpoint": "https://<region>.codesigning.azure.net/",
  "Account": "<your account>",
  "Profile": "<your certificate profile>",
  "Dlib": "C:\\tools\\artifactsigning\\bin\\x64\\Azure.CodeSigning.Dlib.dll"
}
```

5. **Build** as always (`tools\Build-Installer.ps1`, or `-ReleaseCandidate` for a candidate). The build log says
   `Code signing: ON (...)` and lists the files it signed; `signed : yes` at the end.

## Notes

* The certificates behind Artifact Signing live for days, not years: the **timestamp** is what keeps a signature valid
  after the certificate expires. It is never optional here.
* A signature is new to Windows' reputation services at first; Smart App Control and SmartScreen trust a signed
  publisher sooner than an unsigned file, but a brand-new identity can still be met with a warning for a while.
* `tools\Test-WO157Static.ps1` checks that `tools\signing.local.json` is ignored and untracked, and that no endpoint or
  account is written into the scripts.
* Region/endpoint mismatch is the usual "403 Forbidden" from signtool: the endpoint must be the account's own region.
