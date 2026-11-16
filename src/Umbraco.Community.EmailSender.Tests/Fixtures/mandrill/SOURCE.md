# Mandrill (Mailchimp Transactional) fixtures: sources

Fetched 16 Nov 2026 via the `.md` endpoint of each page.

| File | Source | Status |
| --- | --- | --- |
| request-basic.json | https://mailchimp.com/developer/transactional/guides/send-first-email.md (cURL block, `MESSAGE=` value) | verbatim. Note the guide posts to `/api/1.0/messages/send`, the API reference says `/api/1.4/messages/send`. |
| request-basic-html-text.synthesised.json | built from the field list in https://mailchimp.com/developer/transactional/api/messages/send.md (type `MessagesRequestComponent`) | SYNTHESISED: no published example has html+text, cc/bcc or headers. `Reply-To` through `headers` is inferred from "optional extra headers ... (most headers are allowed)". |
| request-attachment.synthesised.json | same schema (`attachments[]` type/name/content, `images[]` type/name/content) | SYNTHESISED |
| response-success.json | https://mailchimp.com/developer/transactional/api/messages/send.md, "Examples > Response" | verbatim. It is a schema-generated placeholder: `status` `sent` sits beside `reject_reason` and `queued_reason`, which would not co-occur. |
| response-error-auth.synthesised.json | error type `401 Unauthorized Error` on the same page (`status`, `code` 401, `name` Invalid_Key, `message`) | SYNTHESISED: page gives field names and fixed values, not message text |

Not documented anywhere I could fetch: validation error body, rate-limit body.
