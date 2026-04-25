# Internal URL Shortener Service – Technical Design

## 1. API Endpoints

```
POST /api/v1/shorten
  Body: { "url": "https://...", "ttl_days": 30, "custom_code": "optional" }
  Response: { "code": "abc123", "short_url": "https://short.internal/abc123", "expires_at": "2025-02-15T10:00:00Z" }

GET /api/v1/:code
  Redirect to original URL (301 permanent or 302 temporary)
  Response: 404 if expired or not found

GET /api/v1/list?limit=50&offset=0
  Response: { "urls": [...], "total": 142, "limit": 50, "offset": 0 }
  Requires authentication
```

## 2. Storage

**Database:** PostgreSQL (ACID compliance, indexing, built-in TTL via triggers)

**Schema:**
```sql
CREATE TABLE short_urls (
  id BIGSERIAL PRIMARY KEY,
  code VARCHAR(8) NOT NULL UNIQUE,
  original_url TEXT NOT NULL,
  created_by VARCHAR(255) NOT NULL,
  created_at TIMESTAMP DEFAULT NOW(),
  expires_at TIMESTAMP NOT NULL,
  click_count INT DEFAULT 0,
  last_accessed_at TIMESTAMP,
  custom_code BOOLEAN DEFAULT FALSE
);

CREATE INDEX idx_code ON short_urls(code);
CREATE INDEX idx_expires_at ON short_urls(expires_at);
CREATE INDEX idx_created_by ON short_urls(created_by);
```

**Cleanup:** Hourly cron job deletes rows where `expires_at < NOW()`.

## 3. Key Generation

**Algorithm:** Base62 encoding of sequential ID with optional salt

```
generate_code(id: int64) -> string:
  // Offset by random salt (1M-1B) to avoid sequential guessing
  offset_id = id + SALT
  code = base62_encode(offset_id)
  // Pad/truncate to 8 characters
  return code.substring(0, 8)
```

**Collision handling:** Unique constraint on `code` column; retry with next ID if collision occurs (rare at 100 engineers, ~1-2 URLs/person/day = ~200/day = negligible collision risk).

**Code space:** 62^8 ≈ 218 trillion possible codes; sufficient for 100 years at 1M URLs/day.

## 4. Authentication & Rate Limiting

**Auth:** OAuth2 / OIDC (corporate SSO) or API key for service-to-service calls.

```
Header: Authorization: Bearer <token>
Validate against internal identity provider; extract user_id for audit logging.
```

**Rate Limiting:** Redis-backed sliding window

```
POST /shorten:    50 req/user/hour
GET /list:        100 req/user/hour
GET /:code:       1000 req/IP/hour (public redirect, loose limit)

Exceeded: Return 429 with Retry-After header
```

## 5. Expiration Policy

| Use Case | Default TTL | Max TTL | Notes |
|----------|-------------|---------|-------|
| Temporary links (Slack, email) | 7 days | 30 days | Auto-delete; users can request extension |
| Internal docs/wikis | 90 days | 365 days | Long-lived, audit trail required |
| API documentation | 365 days | — | Renewal before expiry recommended |

**Soft vs. Hard Delete:**
- Soft delete: Mark `deleted_at` timestamp; return 410 Gone instead of 404
- Hard delete: Purge after 30 days of soft deletion (allows accidental recovery)

**Expiration workflow:**
```
User creates URL with ttl_days=30
expires_at = NOW() + INTERVAL '30 days'
Cron job (hourly): DELETE FROM short_urls WHERE expires_at < NOW()
User can extend: PUT /api/v1/:code { "new_ttl_days": 60 }
```

## Deployment Notes

- **Scaling:** Stateless API servers behind load balancer; PostgreSQL read replicas for list queries
- **Monitoring:** Track code generation latency, redirect hit rates, expiration churn
- **Backups:** Daily snapshots; 30-day retention