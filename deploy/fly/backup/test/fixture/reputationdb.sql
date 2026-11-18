-- SYNTHETIC fixture shaped after CONTRACTS 5 (a subset of columns). T-10.05 round trip only.
CREATE TABLE reviews (id uuid PRIMARY KEY, booking_id uuid NOT NULL UNIQUE, practitioner_id uuid NOT NULL, client_user_id varchar(64) NOT NULL, rating smallint NOT NULL CHECK (rating BETWEEN 1 AND 5), text varchar(2000) NOT NULL, status varchar(20) NOT NULL);
CREATE TABLE moderation_events (id bigserial PRIMARY KEY, review_id uuid NOT NULL REFERENCES reviews(id), kind varchar(20) NOT NULL, note varchar(500));
CREATE TABLE rating_summaries (practitioner_id uuid PRIMARY KEY, rating_count int NOT NULL, rating_avg numeric(3,2) NOT NULL, version bigint NOT NULL);
INSERT INTO reviews SELECT md5('demo-r-' || n)::uuid, md5('demo-rb-' || n)::uuid, md5('demo-p-' || (1 + n % 10))::uuid, 'demo-client-' || (1 + n % 15), 1 + n % 5, 'demo review text ' || n, 'Published' FROM generate_series(1, 40) n;
INSERT INTO moderation_events (review_id, kind, note) SELECT md5('demo-r-' || (1 + n % 40))::uuid, 'Created', NULL FROM generate_series(1, 60) n;
INSERT INTO rating_summaries SELECT md5('demo-p-' || p)::uuid, 4, 3.75, 1 FROM generate_series(1, 10) p;
