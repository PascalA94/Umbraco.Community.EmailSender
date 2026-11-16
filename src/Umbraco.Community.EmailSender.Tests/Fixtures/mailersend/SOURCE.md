# MailerSend fixtures

MailerSend success is `202 Accepted` with an EMPTY body; the message id is in the `x-message-id` response header (https://developers.mailersend.com/api/v1/email.md , "Sending queued"). So there is no response-success.json.

- request-basic.json: JSON tab of https://developers.mailersend.com/api/v1/email.md . Verbatim (re-indented only).
- request-attachment.json: https://developers.mailersend.com/guides/adding-attachments-to-emails.md , cURL --data body. Verbatim. `content` is a placeholder in the docs, not real base64.
- response-error-validation.json: https://developers.mailersend.com/api/v1/email.md "Validation error" and https://developers.mailersend.com/general.md "Validation errors". Verbatim.
- response-ratelimit.json: https://developers.mailersend.com/general.md "Rate limits" example (HTTP/2 429). Verbatim body. Headers in that example: `x-ratelimit-limit`, `x-ratelimit-remaining`, `retry-after: 59`, `x-ratelimit-reset: 1629291024`.
- response-error-auth.synthesised.json: SYNTHESISED. The docs list 401 "The provided API token is invalid" (https://developers.mailersend.com/general.md , HTTP status codes) but print no 401 body. The `{"message": ...}` shape is inferred from the other documented error bodies; the exact text is a guess ("Unauthenticated." is not from the docs).
