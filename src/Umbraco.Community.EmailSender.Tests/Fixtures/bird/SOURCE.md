# Bird fixtures

- request-basic.json: https://bird.com/docs/get-started/send-your-first-email.curl.md (cURL -d body). Minimally edited: added a `text` field (documented in OpenAPI `EmailMessageSendRequest.properties.text`).
- request-attachment.json: https://bird.com/docs/guides/email/attachments.curl.md (cURL -d body). Verbatim; `content` is truncated in the docs ("JVBERi0xLjcKJ...") so it is a placeholder, not valid base64.
- response-success.json: OpenAPI https://bird.com/openapi.json , path `paths./v1/email/messages.post.responses.202.content.application/json.example`. Verbatim.
- response-error-validation.json: https://bird.com/docs/api/errors.md (the envelope example). Verbatim (generic, not send-specific).
- response-ratelimit.json: https://bird.com/docs/guides/rate-limits.md (429 body). Verbatim. Headers: `Retry-After` (seconds), `RateLimit-Policy: "email_send";q=1000;w=60`, `RateLimit: "email_send";r=0;t=35` (RFC 9651 structured fields).
- response-error-auth.synthesised.json: SYNTHESISED. https://bird.com/docs/api/authentication.md states 401 uses the standard envelope with type `auth_error` (https://bird.com/docs/api/errors.md status table) but prints no 401 body; `code` and `message` values here are placeholders.
