# Resend fixtures

- request-basic.json: first Send Email example, https://resend.com/docs/api-reference/emails/send-email.md (cURL block). Edited: added a `text` field (documented in the same page) and converted the cURL -d body to plain JSON.
- request-attachment.json: https://resend.com/docs/dashboard/emails/embed-inline-images.md (cURL "Local image example", `content` base64 + `content_id`). Edited: removed a trailing `%` copied from the docs' base64 string (a shell artefact), dropped nothing else.
- response-success.json: Response tab of https://resend.com/docs/api-reference/emails/send-email.md, verbatim.
- *.synthesised.json: NOT copied from an example. Resend's docs list status, name and message per error (https://resend.com/docs/api-reference/errors.md) but print no JSON body. Shape `{statusCode,name,message}` taken from the official SDK source, https://github.com/resend/resend-dotnet/blob/master/src/Resend/Payloads/ErrorResponse.cs and https://github.com/resend/resend-node/blob/main/src/interfaces.ts (`ErrorResponse`). Values copied from the errors page.
- Rate-limit headers (not a body): see https://resend.com/docs/api-reference/rate-limit.md
