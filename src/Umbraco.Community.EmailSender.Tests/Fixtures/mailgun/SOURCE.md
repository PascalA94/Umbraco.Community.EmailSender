# Mailgun fixtures: sources

Fetched 16 Nov 2026 via the `.md` endpoint of each page. Mailgun sends multipart/form-data, so request fixtures are curl commands, not JSON.

| File | Source | Status |
| --- | --- | --- |
| request-basic.curl.txt | https://documentation.mailgun.com/docs/mailgun/user-manual/sending-messages/send-http.md, "Send With Text and HTML Versions" | verbatim |
| request-attachment.curl.txt | https://documentation.mailgun.com/docs/mailgun/user-manual/sending-messages/send-attachments.md (two examples joined, comment line added) | verbatim apart from the joining comment |
| response-success.json | https://documentation.mailgun.com/docs/mailgun/quickstart.md, "You're response will look like" | minimally edited: the page's markdown auto-linked the id (`[message-id@your-domain.com](mailto:...)`); I wrote the plain string. Real ids are angle-bracketed (RFC 2392), not shown on the page. |

The OpenAPI-derived page (https://documentation.mailgun.com/docs/mailgun/api-reference/send/mailgun/messages/post-v3--domain-name--messages.md) lists 400, 401, 429 and 500 but every example is the placeholder `Example (unknown)`. The only documented error body shape is `{"message": "<string>"}` (400, 429, 500); 401 has no documented body. No error fixtures.
