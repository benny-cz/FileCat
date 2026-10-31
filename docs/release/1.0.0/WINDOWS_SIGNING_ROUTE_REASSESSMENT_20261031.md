# Windows signing route after SignPath Foundation refusal — 2026-10-31

## Decision state

The owner received SignPath Foundation's refusal of the FileCat application on 2026-10-31. The provider cited insufficient independent public trust and visibility signals (adoption, outside references and sustained engagement), not a finding about FileCat's code. Its invitation to reapply later is not provider acceptance. The public unsigned `v1.0.0-preview.3` remains unchanged. Stable 1.0.0 is **NO-GO**; no certificate, approved signing service, signed candidate, or publication decision exists.

The owner confirmed on 2026-10-31 that FileCat will remain **free and noncommercial** during the proposed certificate term and that there is **no registered business** to hold a certificate. This makes individual open-source issuance the first route to check; it does not establish Certum's approval or stock availability.

## Current routes

| Route | Fit for the presently recorded individual maintainer | Unresolved before selection |
| --- | --- | --- |
| [Certum Open Source Code Signing in the Cloud](https://shop.certum.eu/open-source-code-signing-on-simplysign.html) | Certum describes a Microsoft-trusted certificate for free/open-source software issued to an individual, starting at €49. Its [identity and project requirements](https://support.certum.eu/en/code-signing-required-documents/) require a public project with the subscriber's relationship visible and prohibit signing commercially distributed software. | The shop currently says **out of stock**. Confirm actual availability, noncommercial eligibility, total price, publisher identity, and whether its mobile/desktop activation can sign in an owner-reviewed, provenance-bound release flow. A manual signing step would require a reviewed contract/pipeline change, not an implicit waiver. |
| [Standard OV certificate for an individual](https://www.sectigo.com/faqs/detail/OV-Code-Signing-Validation-for-Organizations-and-Individuals) | Sectigo states OV certificates can be issued to individuals. [Microsoft lists traditional OV](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) as a worldwide option for non-Store distribution. | Obtain a real CA quote, identity/key-custody requirements, supported signing interface and GitHub pipeline design. Certificate purchase or provider submission is an owner action. |
| [Paid SignPath](https://docs.signpath.io/change-subscription) | Starter lists $500/year before tax and offers trusted-build policy controls. | Its bundled GlobalSign EV certificate is limited to a legally registered organization. An individual would need confirmation that SignPath will accept the project and a separately issued trusted certificate through its [certificate-management route](https://docs.signpath.io/managing-certificates). Paying the subscription alone does not solve identity or signing. |
| [Azure Artifact Signing or Microsoft Store MSIX](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) | Azure signing is available to EU **organizations**, while individual enrollment is limited to the US/Canada. Store MSIX is signed by Microsoft at no certificate cost. | Azure is unavailable to an individual in Czechia absent a qualifying organization. MSIX would change distribution/package behavior and requires a separate product and release decision. Store submission of the current EXE does **not** get free Microsoft signing. |

All prices and availability are observations of the linked provider pages on 2026-10-31, not offers or approvals. A new trusted signature can still encounter SmartScreen reputation warnings; it does not by itself pass FileCat's final Smart App Control or installed-candidate checks.

## Ready-to-send Certum inquiry

> I maintain FileCat, a public MIT-licensed, free and noncommercial desktop application at https://github.com/benny-cz/FileCat. I am an individual in Czechia with no registered business. Your Open Source Code Signing in the Cloud page currently says “out of stock.” Can this certificate be ordered now, and what is the total one-year price and expected identity-verification time? Will the certificate be valid for signing FileCat's Windows EXE and DLL files and its Inno Setup installer/uninstaller? Does SimplySign support an owner-reviewed, provenance-bound GitHub Actions release workflow without exposing the private key or mobile access code to CI? If signing must be interactive, what unattended or attended signing interfaces do you support? Please confirm any noncommercial-use or distribution limits that would apply.

This is a draft for the owner; it has **not** been sent. Do not attach identity documents until using Certum's authenticated submission channel after a provider decision.

## Recommended finite sequence

1. Ask Certum whether the cloud open-source certificate can be ordered for an individual in Czechia, its exact terms and total price, and whether protected release signing can be automated or must be owner-attended. The owner has confirmed the free/noncommercial condition. Do not send identity documents through this repository or chat.
2. If Certum is available and compatible, the owner may select and purchase it. If it is unavailable or unsuitable, request an individual OV quote and signing/key-custody plan from a CA. Consider paid SignPath only after an explicit compatible-certificate and individual-account answer. No provider is selected yet.
3. Before any RC tag, record the chosen certificate's subject, chain, allowed artifact types, timestamping service, key custody and authorized signing actors. Implement the provider-specific protected signing path with negative unsigned/foreign-source controls and exact before/after artifact manifests.
4. Build the frozen candidate, sign the FileCat-owned PE payloads and installer in the required order, then verify the final installed and portable bytes, signatures, chain, timestamp, metadata, loader behavior and Smart App Control on the specified GA Windows tier. Reconcile every remaining release gate and obtain the owner's separate exact stable GO.

**Stop:** no RC tag, signed-asset claim, `REL-SIGN-WINDOWS` acceptance or stable publication until a compatible provider/certificate is actually issued and the protected candidate controls pass. The Foundation refusal does not justify weakening the current release criteria.
