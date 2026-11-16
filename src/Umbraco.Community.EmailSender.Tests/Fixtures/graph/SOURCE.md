# Microsoft Graph sendMail fixtures: sources

Fetched 16 Nov 2026 from the docs repository `microsoftgraph/microsoft-graph-docs-contrib` (raw markdown, branch main).

| File | Source | Status |
| --- | --- | --- |
| request-basic.json | https://raw.githubusercontent.com/microsoftgraph/microsoft-graph-docs-contrib/main/api-reference/v1.0/api/user-sendmail.md , "Example 1" | verbatim (HTTP line and header removed) |
| request-headers.json | same file, "Example 2" | verbatim |
| request-attachment.json | same file, "Example 3" | verbatim |
| response-success.txt | same file, "Response" (no body) | verbatim. Note: msgraph-metadata OpenAPI declares `204` for this operation, see graph.md section 5. |
| response-error-validation.json | https://raw.githubusercontent.com/microsoftgraph/microsoft-graph-docs-contrib/main/concepts/errors.md , "full JSON error body" | verbatim. It is the generic error example (an upload-fragment error), not a sendMail-specific one. |
| response-ratelimit.txt | https://raw.githubusercontent.com/microsoftgraph/microsoft-graph-docs-contrib/main/concepts/throttling.md , "Sample response" | verbatim, closing brace added (the page's snippet is cut by my line range) |
| token-request.txt | https://raw.githubusercontent.com/microsoftgraph/microsoft-graph-docs-contrib/main/concepts/auth-v2-service.md , "Token request" | verbatim (placeholders from the page) |
| token-response.json | same file, "Token response" | verbatim |
| token-error.json | https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-client-creds-grant-flow , "Error response" | verbatim apart from the timestamp placeholder spacing |

Not documented: a sendMail-specific 401/403 body. The generic error envelope above applies. No `response-error-auth.json`.
