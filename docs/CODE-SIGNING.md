# Code signing (removes the SmartScreen warning)

Windows SmartScreen warns about unsigned downloads. Open-source projects can get a free code-signing
certificate from **SignPath Foundation**. The build workflow is already prepared. Signing switches on by itself
once the settings below exist, and until then builds are simply unsigned.

## 1. Apply (one time, done by the repository owner)

1. Go to <https://signpath.org/apply> and apply with the repository `https://github.com/polemarxos/LocalMind`.
   The conditions: an OSI-approved license (MIT is fine), a public repository, and builds made by GitHub Actions
   (already true).
2. When you are accepted, SignPath creates an organization for you on <https://app.signpath.io>.

## 2. Set up SignPath (app.signpath.io)

1. **Trusted build systems:** add the predefined **GitHub.com** system and link it to the project.
2. **Project:** create a project with the slug `LocalMind`.
3. **Artifact configuration** (set as default). The workflow uploads a zip that contains `LocalMind.exe`:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="LocalMind.exe">
         <authenticode-sign />
       </pe-file>
     </zip-file>
   </artifact-configuration>
   ```

4. **Signing policy:** create one with the slug `release-signing`, using the certificate SignPath gave you.
5. **API token:** create a CI user with *Submitter* rights on that policy, and copy its API token.

## 3. Add the settings to GitHub

In the repository, open **Settings → Secrets and variables → Actions**:

| Kind | Name | Value |
|---|---|---|
| Secret | `SIGNPATH_API_TOKEN` | the API token |
| Variable | `SIGNPATH_ORGANIZATION_ID` | your organization ID (shown in app.signpath.io) |
| Variable (optional) | `SIGNPATH_PROJECT_SLUG` | only if not `LocalMind` |
| Variable (optional) | `SIGNPATH_POLICY_SLUG` | only if not `release-signing` |

After that, every build sends the exe to SignPath, waits for the signed copy, and puts the signed exe in the Release.
SignPath Foundation may need to approve each release request manually at first.
