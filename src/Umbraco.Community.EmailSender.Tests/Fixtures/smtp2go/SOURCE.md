# SMTP2GO fixtures

SMTP2GO documents no 429 body (only the status code and an IP timeout of at least 1 minute, https://developers.smtp2go.com/docs/rate-limiting.md), so there is no response-ratelimit.json. It documents no 401 body either; `response-error-permission.json` is the closest official example (it is typed 400 in the OpenAPI).

- request-basic.json: OpenAPI embedded in https://developers.smtp2go.com/reference/send-standard-email.md , `paths./email/send.post.requestBody...schema.example`. Minimally edited: removed `attachments` (kept in request-attachment.json) and `schedule`.
- request-attachment.json: https://developers.smtp2go.com/docs/adding-attachments.md ("A full request example - fileblob", --data body). Verbatim. `fileblob` is a docs placeholder.
- response-success.json: same OpenAPI, `responses.200.content.application/json.examples.Example.value`. Verbatim. Note a nested `data` object with `succeeded`/`failed`.
- response-error-validation.json: https://developers.smtp2go.com/docs/response-codes.md ("Error Codes" example). Verbatim. This is FLAT (`error`, `error_code` at top level), whereas the OpenAPI 400 example nests them under `data`. Both shapes are documented; handle both.
- response-error-permission.json: same OpenAPI, `responses.400.content.application/json.examples.Example.value`. Verbatim.
- response-200-with-failure.synthesised.json: SYNTHESISED. The docs say "this endpoint returns 200 OK even if there are errors in the reponse, please check the `failed` and `failures` properties" (https://developers.smtp2go.com/reference/send-standard-email.md) but print no populated failures example. Structure from the OpenAPI schema; the failure string is the unverified-sender message quoted at https://support.smtp2go.com/hc/en-gb/articles/9150216032537 (as shown in Activity, not confirmed as the API failure text).
