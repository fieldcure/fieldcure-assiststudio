# Public API Auth & Rate Limiting Plan

We will expose `api.example.com` to third-party integrators next month.
This document outlines the authentication, rate limiting, logging, and
deployment approach.

## Authentication

- API keys issued via the customer dashboard. Format: 32-character random hex.
- Keys passed via `?api_key=...` query parameter on every request.
- Server validates the key against the `api_keys` table on every request.
- Keys never expire. Customers can rotate manually from the dashboard.

## Rate Limiting

- 1000 requests per hour per API key.
- Counter stored in Redis with a 1-hour TTL.
- On limit exceeded, response is 429 with a `Retry-After` header.

## Logging

- All requests logged with full URL and headers, for debugging purposes.
- Logs retained 90 days in S3 (standard tier).
- Access to logs limited to engineering team.

## Deployment

- Single region: `us-east-1`.
- Health check endpoint: `GET /health` returns `200 OK` if the process is up.
- Auto-scaling group, min 2 / max 10 instances.

## Out of scope (for now)

- mTLS
- IP allowlisting
- Per-endpoint quotas
