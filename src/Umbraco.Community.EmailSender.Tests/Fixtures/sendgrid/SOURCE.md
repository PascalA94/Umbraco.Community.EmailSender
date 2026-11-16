# SendGrid fixtures

SendGrid success is `202 Accepted` with an empty body (OpenAPI `paths./v3/mail/send.post.responses.202`, no content), so there is no response-success.json. With `sandbox_mode.enable: true` a valid request returns `200 OK` instead.

- request-basic.json: https://www.twilio.com/docs/sendgrid/for-developers/sending-email/api-getting-started.md (cURL --data body). Minimally edited: added a second `content` item `text/html` (value text invented, shape from OpenAPI `PostMailSend.content.application/json.schema.properties.content`).
- request-attachment.json: https://raw.githubusercontent.com/twilio/sendgrid-oai/main/spec/json/tsg_mail_v3.json, path `components.requestBodies.PostMailSend.content.application/json.examples.MailSendNoTemplate.value`. Verbatim. Shows cc, bcc, reply_to, attachments, tracking_settings, sandbox flag.
- response-error-auth.json: same file, `components.responses.PostMailSend401...examples."Unauthorized request".value`. Verbatim.
- response-error-validation.json: https://www.twilio.com/docs/sendgrid/for-developers/sending-email/sandbox-mode.md ("Invalid Request Body" Response). Minimally edited: the docs print a raw newline and unescaped quotes inside the message string, which is invalid JSON; escaped them.
- response-ratelimit.json: https://www.twilio.com/docs/sendgrid/api-reference/how-to-use-the-sendgrid-v3-api/rate-limits (429 example). Minimally edited: docs show a trailing comma after the array item and `field: null`; trailing comma removed. The 429 headers there are `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` (unix epoch seconds, example 1392815263).
