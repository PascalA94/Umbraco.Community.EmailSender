# Mailjet fixtures: sources

Fetched 16 Nov 2026.

| File | Source | Status |
| --- | --- | --- |
| request-basic.json | https://dev.mailjet.com/_bundle/openapi/openapi-mailjet.yaml, path `/v3.1/send` > post > requestBody > content > application/json > example | verbatim (YAML to JSON) |
| request-attachment.json | https://dev.mailjet.com/docs/email-api/send-api-v31/send-attached-files.md, first cURL `-d` body | verbatim (whitespace only) |
| response-success.json | https://dev.mailjet.com/docs/email-api/send-api-v31/send-in-bulk.md, "API Response" | verbatim |
| response-error-validation.json | https://dev.mailjet.com/docs/email-api/send-api-v31/send-api-errors.md, "Error in validation of the JSON Payload" | verbatim |
| response-error-global.json | same page, "Global Error (example: broken JSON format)" | verbatim |

No documented body for 401 (bad credentials) or 429. The status-codes page (https://dev.mailjet.com/docs/email-api/email-api-overview/status-codes.md) says only "Error responses will usually include an Error Identifier, Error Message and Status Code". Rate-limit page (https://dev.mailjet.com/docs/email-api/email-api-overview/rate-limits.md) gives no body or headers. No `response-error-auth.json` or `response-ratelimit.json`.
