# Mailtrap fixtures: sources

Fetched 16 Nov 2026 from Mailtrap's own OpenAPI repository, https://github.com/mailtrap/mailtrap-openapi (branch main, `specs/`). The repository README says these are the specs behind https://docs.mailtrap.io/developers.

| File | Source | Status |
| --- | --- | --- |
| request-basic.json | `specs/email-sending-transactional.openapi.yml`, `paths./api/send.post.x-codeSamples` (cURL `-d` body) | verbatim |
| request-basic-html-text.json | same file, Node.js code sample object (`client.send({...})`) rewritten as JSON | minimally edited (JS object to JSON) |
| request-attachment.synthesised.json | same file, `components.schemas.EmailAttachments`, `EmailRecipients`, `EmailReplyTo`, `EmailSendingHeaders` | SYNTHESISED |
| response-success.json | same file, `paths./api/send.post.responses.200.content.application/json.example` | verbatim |
| response-error-validation.json | same file, `components.responses.BadRequest.content.application/json.example` | verbatim |
| response-error-auth.json | same file, `components.responses.Unauthorized...example` | verbatim |
| response-ratelimit.json | `specs/sandbox-sending.openapi.yml`, `paths./api/send/{sandbox_id}.post.responses.429...example` | verbatim. This is the SANDBOX host (`sandbox.api.mailtrap.io`). The live `send.api.mailtrap.io` spec declares no 429. |
