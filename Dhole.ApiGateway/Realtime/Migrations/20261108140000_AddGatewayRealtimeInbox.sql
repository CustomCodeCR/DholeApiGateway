-- F3 candidate for rt-20261107-01: NOT executed automatically.
-- Apply by reviewed migration batch only when F9 approval and database backup exist.
-- Gateway's own PostgreSQL database (not CRM database).
BEGIN;
CREATE SCHEMA IF NOT EXISTS realtime;
CREATE TABLE IF NOT EXISTS realtime.gateway_inbox (
    event_id uuid NOT NULL,
    instance_id varchar(120) NOT NULL,
    processed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (event_id, instance_id),
    CONSTRAINT ck_rt_instance_not_empty CHECK (length(trim(instance_id)) > 0)
);
CREATE INDEX IF NOT EXISTS ix_rt_gateway_inbox_processed
    ON realtime.gateway_inbox (processed_at);

CREATE TABLE IF NOT EXISTS realtime.gateway_deadletter (
    instance_id varchar(120) NOT NULL,
    stream_name varchar(120) NOT NULL,
    stream_id varchar(80) NOT NULL,
    reason varchar(100) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (instance_id, stream_name, stream_id)
);
CREATE INDEX IF NOT EXISTS ix_rt_gateway_dlq_created_at
    ON realtime.gateway_deadletter (created_at);
COMMIT;

-- Down intentionally omitted. Retain evidence for replay/forensics.
-- No secret, token, raw payload or personally identifying data is persisted.
