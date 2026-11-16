# Elastic Email fixtures

Elastic Email documents NO error bodies for /emails/transactional (the OpenAPI lists 400/409/422/503 only for the Idempotency-Key header, with no schema), so there are no response-error-*.json or response-ratelimit.json files and none were invented. The vendor's own skill file says the v4 API "doesn't return 429" and returns 5xx under overload (see elasticemail.md).

- request-basic.json: shape from the Request sample at https://elasticemail.com/developers/api-documentation/rest-api (Send Transactional Email) and OpenAPI `components.schemas.EmailTransactionalMessageData` in https://github.com/ElasticEmail/elasticemail-csharp/blob/master/api/openapi.yaml . EDITED: the docs sample is all placeholder strings; values here (John Doe <email@domain.com>, "Hello!", ContentType HTML/PlainText) are the docs' own placeholders, `To` and the `Body` parts are filled in. `ContentType` enum is `HTML`, `PlainText`, `AMP`, `CSS` per the schema.
- request-attachment.synthesised.json: SYNTHESISED from the `MessageAttachment` schema (`BinaryContent` base64, `Name`, `ContentType`). The docs show only "string" placeholders.
- response-success.json: Response sample (200) of the same docs page. Verbatim.
