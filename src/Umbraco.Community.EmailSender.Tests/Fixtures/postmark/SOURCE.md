# Postmark fixtures: sources

Fetched 16 Nov 2026. HTML-to-text of the page; JSON compared by eye to the page's code blocks.

| File | Source | Status |
| --- | --- | --- |
| request-basic.json | https://postmarkapp.com/developer/api/email-api ("Send a single email", curl `-d` body) | verbatim, with the HTML quote escaped for JSON validity |
| request-attachment.json | same page, "Example body format" under "Send a single email" | verbatim (whitespace only) |
| response-success.json | same page, "Example response" (HTTP 200) | verbatim |
| response-error-validation.json | https://postmarkapp.com/developer/api/overview ("HTTP response codes", 422 example) | verbatim |
| response-error-auth.synthesised.json | message text from the error code table at https://postmarkapp.com/developer/api/overview ("API error codes", code 10, HTTP 401) | SYNTHESISED: the page gives the code and message but no sample body |

No documented body for 429. The page says only "429 - Rate Limit Exceeded". No `response-ratelimit.json`.
